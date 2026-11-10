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
        private readonly bool _logTraffic;
        private readonly TimeSpan _contextMaxAge;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private readonly SemaphoreSlim _contextLock = new(1, 1);
        private readonly SemaphoreSlim _cookieLock = new(1, 1);
        // Caps how many pages Chromium loads at once. Services fan out (e.g. a league search
        // fetches every matching league in parallel), and on a 1-CPU VPS an unbounded burst of
        // tabs pegs the CPU and gets the server throttled by the host.
        private readonly SemaphoreSlim _pageSlots;
        private readonly IAsyncPolicy<PageFetchResult> _retryPolicy;
        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private SharedContext? _sharedContext;

        // Third-party hosts the FA pages pull in (analytics, social embeds, surveys, calendar
        // widget) that contribute nothing to the HTML being scraped. Deliberately a blocklist,
        // not an allowlist: Cloudflare's challenge scripts and OneTrust's consent banner (which
        // the 403 workaround clicks) must keep loading, and an allowlist would silently break
        // the moment the FA adds a host either of those needs.
        private static readonly string[] BlockedHostSuffixes =
        {
            "googletagmanager.com",
            "google-analytics.com",
            "analytics.google.com",
            "doubleclick.net",
            "googlesyndication.com",
            "googleadservices.com",
            "cloudflareinsights.com",
            "twitter.com",
            "twimg.com",
            "x.com",
            "ecal.com",
            "survicate.com",
            "survicate-cdn.com",
            "facebook.net",
            "facebook.com",
            "hotjar.com",
            "clarity.ms",
        };

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
            // Adds downloaded-bytes totals to the per-fetch log line. Off by default because it
            // costs one extra CDP round-trip per request; flip it on to diagnose bandwidth use.
            _logTraffic = configuration.GetValue("Playwright:LogTraffic", false);
            _pageSlots = new SemaphoreSlim(Math.Max(1, configuration.GetValue("Playwright:MaxConcurrentPages", 1)));
            // The shared context is recycled periodically so its cookies and in-memory HTTP cache
            // can't grow or go stale indefinitely.
            _contextMaxAge = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("Playwright:ContextMaxAgeMinutes", 360)));

            // Mirrors the previous HttpClient retry policy: retry transient failures and cases
            // where FullTime bounced the request to an unexpected page (e.g. a bad division ID
            // redirecting to /home) - that's usually transient, so retrying often recovers it.
            var generalRetry = Policy<PageFetchResult>
                .Handle<Exception>()
                .OrResult(r => (!r.IsSuccess && r.StatusCode != 404) || r.LooksBounced)
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

            // 404s get a single quick retry instead of the full three. Most are genuine (an ID
            // that doesn't exist - those 404 every time, and the old policy spent 14s of backoff
            // and three extra fetches confirming it), but FullTime also returns occasional
            // transient 404s during bursts like a league search's parallel division fetches -
            // seen in testing for leagues that loaded fine moments later - so one retry is kept.
            var notFoundRetry = Policy<PageFetchResult>
                .HandleResult(r => r.StatusCode == 404)
                .WaitAndRetryAsync(retryCount: 1, sleepDurationProvider: _ => TimeSpan.FromSeconds(2));

            _retryPolicy = Policy.WrapAsync(generalRetry, notFoundRetry);
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

                // Dispose any previous driver process before creating a new one. This must be
                // keyed on _playwright, not _browser: if the previous LaunchAsync threw (browser
                // never actually started), _browser stays null but _playwright was still created -
                // gating this on "_browser != null" left that driver process orphaned forever,
                // leaking one Node "run-driver" process per failed launch attempt (seen in
                // production: dozens of leaked processes and several GB of memory within minutes
                // while Chrome kept failing to launch for an unrelated reason).
                if (_playwright != null)
                {
                    _logger.LogWarning("Relaunching Playwright browser (previous instance missing, disconnected, or failed to start)");
                    _playwright.Dispose();
                    _playwright = null;
                    _browser = null;
                }

                _playwright = await Playwright.CreateAsync();
                try
                {
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
                            "--disable-blink-features=AutomationControlled",
                            // Channel="chromium" runs the real Chrome binary, which (unlike
                            // chromium-headless-shell) has crash reporting enabled by default as an
                            // "official build". On the production VPS (a systemd service running
                            // with a plain "User=" directive, no PAM session) it crashed on every
                            // single launch with "chrome_crashpad_handler: --database is required"
                            // before a single page could load, because crashpad couldn't resolve a
                            // default database directory. Neither of these two flags actually fixed
                            // that in testing - they're kept as harmless extra cover - the real fix
                            // was environmental: the systemd unit had no $HOME at all (systemd does
                            // NOT set it for a bare "User=" without PAM, even for a real user), so
                            // crashpad had nothing to resolve a path from. Setting
                            // Environment=HOME=/tmp in the unit's [Service] section is what
                            // actually resolved it - see the README's deployment notes.
                            "--disable-crash-reporter",
                            "--crash-dumps-dir=/tmp"
                        }
                    });
                }
                catch
                {
                    // Don't leave a driver process behind if the browser itself failed to launch -
                    // otherwise every failed attempt (e.g. the crashpad issue above) leaks one.
                    _playwright.Dispose();
                    _playwright = null;
                    throw;
                }
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
            // Taken per attempt (not around the whole retry loop) so a fetch waiting out a retry
            // backoff doesn't hold a slot other requests could be using.
            await _pageSlots.WaitAsync();
            SharedContext? shared = null;
            IPage? page = null;
            try
            {
                shared = await AcquireContextAsync();
                var traffic = new TrafficCounter(_logTraffic);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                page = await NewScrapingPageAsync(shared.Context, traffic);

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
                        traffic.UsedCookieWorkaround = await EnsureCookiesAcceptedAsync(shared, traffic, blockedAtUtc: DateTime.UtcNow);

                        response = await page.ReloadAsync(new PageReloadOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = 30000
                        });

                        // Still blocked after the workaround: the shared session itself may have
                        // gone bad. Retire it so the retry policy's next attempt starts from a
                        // completely fresh context, which is what every fetch used to get.
                        if (response != null && response.Status == 403)
                            await RetireContextAsync(shared, "still 403 after cookie workaround");
                    }
                }
                catch (PlaywrightException ex)
                {
                    _logger.LogWarning(ex, "Navigation failed for {Url}", url);
                    return new PageFetchResult { Content = string.Empty, StatusCode = 0, FinalUrl = url };
                }

                var content = await page.ContentAsync();
                var finalPath = new Uri(page.Url).AbsolutePath.TrimEnd('/');
                await traffic.LogAsync(_logger, url, response?.Status ?? 0, stopwatch.Elapsed);

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
            finally
            {
                if (page != null)
                    await ClosePageQuietlyAsync(page);
                if (shared != null)
                    await ReleaseContextAsync(shared);
                _pageSlots.Release();
            }
        }

        // Every page (the target page and the apex consent page) gets the same request filtering
        // and traffic accounting.
        private static async Task<IPage> NewScrapingPageAsync(IBrowserContext context, TrafficCounter traffic)
        {
            var page = await context.NewPageAsync();
            page.RequestFinished += traffic.OnRequestFinished;

            // navigator.webdriver is true by default for a CDP-driven browser. Patching it back to
            // undefined is a common anti-detection trick, but testing has shown it's not what
            // actually clears FA's 403s (Channel="chromium" in EnsureBrowserAsync is) - kept as
            // harmless extra cover, not load-bearing.
            await page.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

            // Skip assets and third-party trackers we don't need for HTML scraping.
            await page.RouteAsync("**/*", async route =>
            {
                if (ShouldBlock(route.Request))
                {
                    traffic.AddBlocked();
                    await route.AbortAsync();
                }
                else
                {
                    await route.ContinueAsync();
                }
            });

            return page;
        }

        private static bool ShouldBlock(IRequest request)
        {
            if (request.ResourceType is "image" or "stylesheet" or "font" or "media" or "texttrack" or "manifest")
                return true;

            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
                return false;

            var host = uri.Host;
            foreach (var suffix in BlockedHostSuffixes)
            {
                if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        // Every fetch used to get a brand-new context, so it never had the cookies from the last
        // successful cookie-accept - meaning every table/results/search fetch hit the 403 and
        // re-ran the whole workaround (measured: ~6.3 MB and ~140 requests each, vs ~30-50 KB for
        // a plain page). Sharing one context across fetches keeps those cookies, so the
        // workaround runs once per session instead of once per fetch.
        private async Task<SharedContext> AcquireContextAsync()
        {
            await _contextLock.WaitAsync();
            try
            {
                var current = _sharedContext;
                if (current != null && (!ReferenceEquals(current.Browser, _browser) || DateTime.UtcNow - current.CreatedUtc > _contextMaxAge))
                {
                    _logger.LogInformation("Recycling shared browser context (browser relaunched or context older than {MaxAge})", _contextMaxAge);
                    await RetireLockedAsync(current);
                    current = null;
                }

                if (current == null)
                {
                    var browser = _browser!;
                    var context = await browser.NewContextAsync(new BrowserNewContextOptions
                    {
                        UserAgent = UserAgent,
                        Locale = "en-GB",
                        TimezoneId = "Europe/London",
                        ViewportSize = new ViewportSize { Width = 1920, Height = 1080 }
                    });
                    current = new SharedContext(context, browser);
                    _sharedContext = current;
                }

                current.Users++;
                return current;
            }
            finally
            {
                _contextLock.Release();
            }
        }

        private async Task ReleaseContextAsync(SharedContext shared)
        {
            await _contextLock.WaitAsync();
            try
            {
                shared.Users--;
                if (shared.Retired && shared.Users == 0)
                    await CloseContextQuietlyAsync(shared.Context);
            }
            finally
            {
                _contextLock.Release();
            }
        }

        private async Task RetireContextAsync(SharedContext shared, string reason)
        {
            await _contextLock.WaitAsync();
            try
            {
                if (shared.Retired)
                    return;
                _logger.LogWarning("Retiring shared browser context: {Reason}", reason);
                await RetireLockedAsync(shared);
            }
            finally
            {
                _contextLock.Release();
            }
        }

        // Caller holds _contextLock. A retired context stops being handed out immediately, but
        // is only closed once the last in-flight fetch using it has released it.
        private async Task RetireLockedAsync(SharedContext shared)
        {
            shared.Retired = true;
            if (ReferenceEquals(_sharedContext, shared))
                _sharedContext = null;
            if (shared.Users == 0)
                await CloseContextQuietlyAsync(shared.Context);
        }

        // Several fetches can hit the 403 at the same moment (e.g. a burst after the context was
        // recycled). Only the first needs to visit the apex page; the rest wait for it and then
        // just reload, since the cookies it sets are shared through the context.
        // Returns true if this call actually ran the apex visit.
        private async Task<bool> EnsureCookiesAcceptedAsync(SharedContext shared, TrafficCounter traffic, DateTime blockedAtUtc)
        {
            await _cookieLock.WaitAsync();
            try
            {
                if (shared.CookiesAcceptedUtc > blockedAtUtc.AddSeconds(-5))
                    return false;

                await AcceptCookiesOnApexAsync(shared.Context, traffic);
                shared.CookiesAcceptedUtc = DateTime.UtcNow;
                return true;
            }
            finally
            {
                _cookieLock.Release();
            }
        }

        // Opens the apex domain in its own tab within the same context/session as the blocked
        // page and genuinely clicks the OneTrust cookie-consent button. Waiting for cf_clearance
        // or just visiting home was already tried and confirmed insufficient on its own - the
        // consent click plus the caller's subsequent RELOAD of the blocked tab is what clears it.
        private async Task AcceptCookiesOnApexAsync(IBrowserContext context, TrafficCounter traffic)
        {
            var apexPage = await NewScrapingPageAsync(context, traffic);
            try
            {
                // DOMContentLoaded rather than NetworkIdle: NetworkIdle waited for every tracker
                // and embed on the home page to finish. The click below already waits for the
                // consent button itself to appear, which is the only thing that matters here.
                await apexPage.GotoAsync(ApexUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30000
                });

                await apexPage.Locator("#onetrust-accept-btn-handler").ClickAsync(new LocatorClickOptions
                {
                    Timeout = 15000
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
                await ClosePageQuietlyAsync(apexPage);
            }
        }

        private static async Task ClosePageQuietlyAsync(IPage page)
        {
            try
            {
                await page.CloseAsync();
            }
            catch (PlaywrightException)
            {
                // Context or browser already gone.
            }
        }

        private static async Task CloseContextQuietlyAsync(IBrowserContext context)
        {
            try
            {
                await context.CloseAsync();
            }
            catch (PlaywrightException)
            {
                // Browser already gone.
            }
        }

        private sealed class SharedContext
        {
            public SharedContext(IBrowserContext context, IBrowser browser)
            {
                Context = context;
                Browser = browser;
            }

            public IBrowserContext Context { get; }
            public IBrowser Browser { get; }
            public DateTime CreatedUtc { get; } = DateTime.UtcNow;
            public DateTime CookiesAcceptedUtc { get; set; } = DateTime.MinValue;
            // Guarded by _contextLock.
            public int Users { get; set; }
            public bool Retired { get; set; }
        }

        // Per-fetch tally of what the browser actually downloaded, logged once the fetch is done,
        // so traffic can be checked in production logs (journalctl) without attaching a debugger.
        private sealed class TrafficCounter
        {
            private readonly bool _measureBytes;
            private readonly List<Task<long>> _sizes = new();
            private int _requests;

            public TrafficCounter(bool measureBytes) => _measureBytes = measureBytes;

            private int _blocked;
            public void AddBlocked() => Interlocked.Increment(ref _blocked);
            public bool UsedCookieWorkaround;

            public void OnRequestFinished(object? sender, IRequest request)
            {
                Interlocked.Increment(ref _requests);
                if (_measureBytes)
                    lock (_sizes)
                        _sizes.Add(MeasureAsync(request));
            }

            private static async Task<long> MeasureAsync(IRequest request)
            {
                try
                {
                    var sizes = await request.SizesAsync();
                    return sizes.ResponseBodySize + sizes.ResponseHeadersSize;
                }
                catch (PlaywrightException)
                {
                    return 0; // page/context already closed
                }
            }

            public async Task LogAsync(ILogger logger, string url, int status, TimeSpan elapsed)
            {
                if (!_measureBytes)
                {
                    logger.LogInformation(
                        "Fetched {Url} status={Status} in {Ms}ms: {Requests} requests, {Blocked} blocked, cookieWorkaround={Workaround}",
                        url, status, (long)elapsed.TotalMilliseconds, _requests, _blocked, UsedCookieWorkaround);
                    return;
                }

                Task<long>[] pending;
                lock (_sizes)
                    pending = _sizes.ToArray();
                var bytes = (await Task.WhenAll(pending)).Sum();

                logger.LogInformation(
                    "Fetched {Url} status={Status} in {Ms}ms: {Requests} requests, {Blocked} blocked, {KB} KB downloaded, cookieWorkaround={Workaround}",
                    url, status, (long)elapsed.TotalMilliseconds, _requests, _blocked, bytes / 1024, UsedCookieWorkaround);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_sharedContext != null)
                await CloseContextQuietlyAsync(_sharedContext.Context);

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
            _contextLock.Dispose();
            _cookieLock.Dispose();
            _pageSlots.Dispose();
        }
    }
}
