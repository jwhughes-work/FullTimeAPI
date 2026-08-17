namespace FullTimeAPI.Models
{
    public class PageFetchResult
    {
        public string Content { get; init; } = string.Empty;
        public int StatusCode { get; init; }
        public string FinalUrl { get; init; } = string.Empty;
        public bool IsSuccess => StatusCode is >= 200 and < 300;

        // True when the final URL's path differs from the requested one (e.g. FullTime bounced
        // an invalid division ID to /home). Used by the fetcher's retry policy only.
        public bool LooksBounced { get; init; }
    }
}
