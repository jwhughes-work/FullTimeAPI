using FullTimeAPI.Models;
using FullTimeAPI.Services.Interfaces;
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
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private readonly AsyncRetryPolicy<PageFetchResult> _retryPolicy;
        private IPlaywright? _playwright;
        private IBrowser? _browser;

        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

        public PlaywrightPageFetcher(ILogger<PlaywrightPageFetcher> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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

        private async Task EnsureBrowserAsync()
        {
            if (_browser != null)
                return;

            await _initLock.WaitAsync();
            try
            {
                if (_browser != null)
                    return;

                _playwright = await Playwright.CreateAsync();
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    // Chromium's sandbox needs unprivileged user namespaces, which many VPS
                    // kernels/containers restrict (and it refuses to start at all when the
                    // process runs as root, which is common for bare systemd deployments).
                    // Disabling it is the standard approach for server-side headless Chromium.
                    Args = new[] { "--no-sandbox" }
                });
            }
            finally
            {
                _initLock.Release();
            }
        }

        public async Task<PageFetchResult> GetHtmlAsync(string url)
        {
            await EnsureBrowserAsync();

            var result = await _retryPolicy.ExecuteAsync(() => FetchOnce(url));

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
                UserAgent = UserAgent
            });

            var page = await context.NewPageAsync();

            // Skip assets we don't need for HTML scraping - keeps each fetch fast.
            await page.RouteAsync("**/*", async route =>
            {
                if (route.Request.ResourceType is "image" or "stylesheet" or "font" or "media")
                    await route.AbortAsync();
                else
                    await route.ContinueAsync();
            });

            IResponse? response;
            try
            {
                response = await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30000
                });
            }
            catch (PlaywrightException ex)
            {
                _logger.LogWarning(ex, "Navigation failed for {Url}", url);
                return new PageFetchResult { Content = string.Empty, StatusCode = 0, FinalUrl = url };
            }

            var content = await page.ContentAsync();
            var requestedPath = new Uri(url).AbsolutePath.TrimEnd('/');
            var finalPath = new Uri(page.Url).AbsolutePath.TrimEnd('/');

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
            if (_browser != null)
                await _browser.CloseAsync();

            _playwright?.Dispose();
            _initLock.Dispose();
        }
    }
}
