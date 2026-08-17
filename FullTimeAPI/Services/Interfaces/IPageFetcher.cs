using FullTimeAPI.Models;

namespace FullTimeAPI.Services.Interfaces
{
    public interface IPageFetcher
    {
        Task<PageFetchResult> GetHtmlAsync(string url);
    }
}
