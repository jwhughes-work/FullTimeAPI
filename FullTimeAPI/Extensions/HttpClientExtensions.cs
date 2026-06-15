using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;

namespace FullTimeAPI.Extensions
{
    public static class HttpClientExtensions
    {
        public static IServiceCollection AddResilientHttpClients(this IServiceCollection services)
        {
            // Retry policy with exponential backoff.
            // Only retries transient failures (network errors, 5xx, 408) and timeouts -
            // retrying genuine 4xx responses (e.g. 404) just wastes time before failing anyway.
            var retryPolicy = HttpPolicyExtensions
                .HandleTransientHttpError()
                .Or<TimeoutRejectedException>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

            // Timeout policy
            var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(30));

            services.AddHttpClient("resilient", client =>
                {
                    // FullTime can return 403/empty responses to requests without a browser-like User-Agent.
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                })
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    // Don't silently follow redirects: if FullTime bounces a division request to
                    // /home (e.g. when it doesn't like the request), auto-redirect would hand us a
                    // 200 for the wrong page and the parser would return blank. Surfacing the 3xx
                    // instead lets EnsureSuccessStatusCode throw so it's visible and retried.
                    AllowAutoRedirect = false,
                    AutomaticDecompression = System.Net.DecompressionMethods.All,
                })
                .AddPolicyHandler(retryPolicy)
                .AddPolicyHandler(timeoutPolicy);

            return services;
        }
    }
}