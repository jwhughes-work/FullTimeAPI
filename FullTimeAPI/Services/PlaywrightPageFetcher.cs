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

        // Deep-linking straight to table.html/results.html gets Cloudflare's "Attention Required"
        // block page - reproducible even in a normal browser, not just Playwright - whereas
        // reaching the same URL by browsing from the home page first works. Mirror that here:
        // visit home in the same context/session before the real request.
        private const string HomeUrl = "https://fulltime.thefa.com/home/index.html";

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
                    Args = new[]
                    {
                        // Chromium's sandbox needs unprivileged user namespaces, which many VPS
                        // kernels/containers restrict (and it refuses to start at all when the
                        // process runs as root, which is common for bare systemd deployments).
                        // Disabling it is the standard approach for server-side headless Chromium.
                        "--no-sandbox",
                        // /dev/shm is tiny (often 64MB) by default on VPS/containers, and Chromium
                        // uses it for shared memory - under load it runs out and the renderer/
                        // browser process crashes outright. This is the standard fix: fall back to
                        // /tmp instead of shared memory.
                        "--disable-dev-shm-usage",
                        // Cloudflare's bot management fingerprints the CDP-driven "automation"
                        // flag Chromium normally exposes (navigator.webdriver, etc.) and 403s
                        // table.html/results.html specifically even with a valid session - this
                        // is the standard flag to stop Chromium advertising itself as automated.
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

            // navigator.webdriver is true by default for a CDP-driven browser and is one of the
            // first things Cloudflare's bot management checks - patch it back to undefined like a
            // normal browser before any page script (including Cloudflare's own) can observe it.
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

            IResponse? response;
            try
            {
                if (!requestedPath.Equals("/home/index", StringComparison.OrdinalIgnoreCase))
                {
                    await page.GotoAsync(HomeUrl, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 30000
                    });
                }

                response = await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30000,
                    Referer = HomeUrl
                });
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
