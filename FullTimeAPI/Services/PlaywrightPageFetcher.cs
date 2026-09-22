using FullTimeAPI.Models;
using FullTimeAPI.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using Polly;
using Polly.Retry;

namespace FullTimeAPI.Services
{
    // FullTime sits behind Cloudflare, which fingerprints the TLS handshake and blocks .NET's
    // HttpClient outright (403) even with a browser-matching User-Agent header - curl and real
    // browsers pass, HttpClient doesn't. Routing requests through a real headless Chromium instance
    // sidesteps that entirely since the handshake is genuinely Chromium's.
    public class PlaywrightPageFetcher : IPageFetcher, IAsyncDisposable
    {
        private readonly ILogger<PlaywrightPageFetcher> _logger;
        private readonly bool _headless;
        private readonly int _debugPauseSeconds;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private readonly AsyncRetryPolicy<PageFetchResult> _retryPolicy;
        private IPlaywright? _playwright;
        private IBrowser? _browser;

        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

        // Deep-linking straight to table.html/results.html/search.html gets a Cloudflare 403 -
        // reproducible even in a normal browser, not just Playwright. Just visiting the home page
        // first (with or without waiting for cf_clearance) does NOT fix it - confirmed by testing.
        // The combination that actually works, verified manually and reproduced here: accept the
        // cookie-consent banner on the apex domain in a separate tab, THEN RELOAD (not re-navigate)
        // the tab that already attempted the blocked URL once. See AcceptCookiesOnApexAsync.
        private const string ApexUrl = "https://fulltime.thefa.com";

        public PlaywrightPageFetcher(ILogger<PlaywrightPageFetcher> logger, IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            // Lets a dev flip appsettings.Development.json's Playwright:Headless to false to watch
            // the browser while debugging a scrape locally; defaults to headless everywhere else.
            _headless = configuration.GetValue("Playwright:Headless", true);
            // How long to leave the window open after a fetch completes so a dev can actually look
            // at it - Playwright.GetHtmlAsync grabs the content and closes the context immediately,
            // so with this at 0 the window flashes up and vanishes before you can react.
            _debugPauseSeconds = configuration.GetValue("Playwright:DebugPauseSeconds", 0);

            // Mirrors the previous HttpClient retry policy: retry transient failures and cases
            // where FullTime bounced the request to an unexpected page (e.g. a bad division ID
            // redirecting to /home) - that's usually transient, so retrying often recovers it.
            _retryPolicy = Policy<PageFetchResult>
                .Handle<Exception>()
                .OrResult(r => !r.IsSuccess || r.LooksBounced)
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }

        // _browser is a singleton for the app's lifetime. If the underlying Chromium process
        // crashes or is killed (e.g. OOM under /dev/shm pressure on a small VPS), the old code
        // only checked "is _browser non-null" - it stayed non-null forever, so every request
        // after a crash failed permanently with TargetClosedException until the whole app was
        // restarted. Checking IsConnected lets a dead browser be detected and relaunched.
        private async Task EnsureBrowserAsync()
        {
            if (_browser != null && _browser.IsConnected)
                return;

            await _initLock.WaitAsync();
            try
            {
                if (_browser != null && _browser.IsConnected)
                    return;

                if (_browser != null)
                {
                    _logger.LogWarning("Playwright browser was disconnected (likely crashed) - relaunching");
                    _playwright?.Dispose();
                }

                _playwright = await Playwright.CreateAsync();
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = _headless,
                    // Playwright's default headless=true launches "chromium-headless-shell" - a
                    // separate, stripped-down binary with a materially different fingerprint from
                    // real Chrome. That mismatch is what Cloudflare was actually catching: with
                    // this flag absent, the cookie-accept+reload workaround below 403'd every
                    // single time in default (shell) headless mode, but passed every time in fully
                    // headed mode. Channel="chromium" (supported since Playwright 1.49) opts into
                    // "new" headless mode instead - the real Chrome binary running windowless, not
                    // the shell - and that alone was enough to pass in headless mode too, confirmed
                    // twice against the live site, with no display/Xvfb required.
                    Channel = "chromium",
                    Args = new[]
                    {
                        // Chromium's sandbox needs unprivileged user namespaces, which many VPS
                        // kernels/containers restrict (and it refuses to start at all when the
                        // process runs as root, which is common for bare systemd deployments).
                        // Disabling it is the standard approach for server-side headless Chromium.
                        "--no-sandbox",
                        // /dev/shm is tiny (often 64MB) by default on VPS/containers, and Chromium
                        // can crash outright if it runs out under load - not confirmed as the cause
                        // of an actual crash here, but this is the standard preventative flag for
                        // server-side Chromium (falls back to /tmp instead of shared memory).
                        "--disable-dev-shm-usage",
                        // Speculative hardening carried over from earlier debugging of the 403s -
                        // stops Chromium exposing navigator.webdriver/etc. as an automated browser.
                        // Testing since has shown this flag is NOT what fixes the 403s (Channel=
                        // "chromium" above is) - kept as harmless extra cover, not load-bearing.
                        "--disable-blink-features=AutomationControlled"
                    }
                });
            }
            finally
            {
                _initLock.Release();
            }
        }

        public async Task<PageFetchResult> GetHtmlAsync(string url)
        {
            // EnsureBrowserAsync runs inside the retry loop (not just once up front) so that if
            // the browser is found dead partway through, a retry attempt relaunches it instead of
            // burning all 3 attempts against a browser that's already gone.
            var result = await _retryPolicy.ExecuteAsync(async () =>
            {
                await EnsureBrowserAsync();
                return await FetchOnce(url);
            });

            if (!result.IsSuccess)
                _logger.LogWarning(
                    "Non-success response for {Url}. status={Status} finalUrl={FinalUrl}",
                    url, result.StatusCode, result.FinalUrl);

            return result;
        }

        private async Task<PageFetchResult> FetchOnce(string url)
        {
            await using var context = await _browser!.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = UserAgent,
                Locale = "en-GB",
                TimezoneId = "Europe/London",
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 }
            });

            var page = await context.NewPageAsync();

            // navigator.webdriver is true by default for a CDP-driven browser. Patching it back to
            // undefined is a common anti-detection trick, but testing has shown it's not what
            // actually clears FA's 403s (Channel="chromium" in EnsureBrowserAsync is) - kept as
            // harmless extra cover, not load-bearing.
            await page.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

            // Skip assets we don't need for HTML scraping - keeps each fetch fast.
            await page.RouteAsync("**/*", async route =>
            {
                if (route.Request.ResourceType is "image" or "stylesheet" or "font" or "media")
                    await route.AbortAsync();
                else
                    await route.ContinueAsync();
            });

            var requestedPath = new Uri(url).AbsolutePath.TrimEnd('/');
            var isApexHomePage = requestedPath.Equals("/home/index", StringComparison.OrdinalIgnoreCase);

            IResponse? response;
            try
            {
                response = await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30000
                });

                if (!isApexHomePage && response != null && response.Status == 403)
                {
                    _logger.LogInformation("Got 403 for {Url} - retrying via cookie-accept+reload workaround", url);
                    await AcceptCookiesOnApexAsync(context);

                    response = await page.ReloadAsync(new PageReloadOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 30000
                    });
                }
            }
            catch (PlaywrightException ex)
            {
                _logger.LogWarning(ex, "Navigation failed for {Url}", url);
                return new PageFetchResult { Content = string.Empty, StatusCode = 0, FinalUrl = url };
            }

            var content = await page.ContentAsync();
            var finalPath = new Uri(page.Url).AbsolutePath.TrimEnd('/');

            if (_debugPauseSeconds > 0)
            {
                _logger.LogInformation("Debug pause: leaving browser window open for {Seconds}s", _debugPauseSeconds);
                await page.WaitForTimeoutAsync(_debugPauseSeconds * 1000);
            }

            return new PageFetchResult
            {
                Content = content,
                StatusCode = response?.Status ?? 0,
                FinalUrl = page.Url,
                LooksBounced = !string.Equals(requestedPath, finalPath, StringComparison.OrdinalIgnoreCase)
            };
        }

        // Opens the apex domain in its own tab within the same context/session as the blocked
        // page and genuinely clicks the OneTrust cookie-consent button. Waiting for cf_clearance
        // or just visiting home was already tried and confirmed insufficient on its own - the
        // consent click plus the caller's subsequent RELOAD of the blocked tab is what clears it.
        private async Task AcceptCookiesOnApexAsync(IBrowserContext context)
        {
            var apexPage = await context.NewPageAsync();
            try
            {
                await apexPage.GotoAsync(ApexUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 30000
                });

                await apexPage.Locator("#onetrust-accept-btn-handler").ClickAsync(new LocatorClickOptions
                {
                    Timeout = 8000
                });
            }
            catch (Exception ex)
            {
                // Non-fatal - if the banner isn't present or the click fails, the caller's reload
                // just won't have the fix applied and will fall through to the normal retry policy.
                _logger.LogWarning(ex, "Failed to accept cookie banner on apex domain");
            }
            finally
            {
                await apexPage.CloseAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_browser != null && _browser.IsConnected)
            {
                try
                {
                    await _browser.CloseAsync();
                }
                catch (PlaywrightException)
                {
                    // Already gone - nothing to clean up.
                }
            }

            _playwright?.Dispose();
            _initLock.Dispose();
        }
    }
}
