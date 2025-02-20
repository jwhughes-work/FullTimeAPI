using FullTimeAPI.Framework;
using FullTimeAPI.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;

namespace FullTimeAPI.Services
{
    public class ClubService : IClubService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<FixturesService> _logger;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);
        private const string BaseUrl = "https://fulltime.thefa.com/home/search.html";

        public ClubService(HttpClient httpClient, IMemoryCache memoryCache, ILogger<FixturesService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<ClubSearch>> FindClubs(string searchName) 
        {
            if(string.IsNullOrEmpty(searchName))
                throw new ArgumentException("club name cannot be empty", nameof(searchName));

            string cacheKey = $"ClubSearch-{searchName}";

            if (_memoryCache.TryGetValue(cacheKey, out List<ClubSearch> cachedList) && cachedList?.Any() == true)
            {
                _logger.LogInformation("Retrieved club search");
                return cachedList;
            }

            try
            {
                var clubs = await FetchAndParseClubSearch(searchName);
                _memoryCache.Set(cacheKey, clubs, DateTimeOffset.Now.Add(_cacheDuration));
                return clubs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching club search");
                throw;
            }
        }

        private async Task<List<ClubSearch>> FetchAndParseClubSearch(string team)
        {
            var url = $"{BaseUrl}?partLeagueOrClubNameSearchFilter={team}&clubSearchFilter=true";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var document = new HtmlDocument();
            document.LoadHtml(content);

            var results = document.DocumentNode.SelectNodes("//a[contains(@href, '/home/club/')]");
            if (results == null)
            {
                _logger.LogWarning("No clubs found for search {team}", team);
                return new List<ClubSearch>();
            }

            return results.Select(ParseClubRow).Where(fixture => fixture != null).ToList();
        }

        private ClubSearch ParseClubRow(HtmlNode item)
        {
            try
            {
                var href = item.GetAttributeValue("href", "");
                var clubId = href.Split('/').Last().Replace(".html", "");
                
                var nameNode = item.SelectSingleNode(".//p[@class='bold nomargin truncate']");
                if (nameNode == null)
                {
                    _logger.LogWarning("Club name not found for {href}", href);
                    return null;
                }
                
                string clubName = string.Join("", nameNode.ChildNodes.Select(node => node.InnerText)).Trim();

                var teamsNode = item.SelectSingleNode(".//p[@class='smaller']/strong");
                int numberOfTeams = 0;
                if (teamsNode != null && int.TryParse(teamsNode.InnerText.Trim(), out int parsedTeams))
                {
                    numberOfTeams = parsedTeams;
                }

                return new ClubSearch
                {
                    Id = clubId,
                    Name = clubName,
                    NumberOfTeams = numberOfTeams
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing club row");
                return null;
            }
        }
    }
}
