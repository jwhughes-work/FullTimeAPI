using FullTimeAPI.Framework;
using FullTimeAPI.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;

namespace FullTimeAPI.Services
{
    public class FixturesService : IFixturesService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<FixturesService> _logger;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);
        private const string BaseUrl = "https://fulltime.thefa.com/fixtures.html";
        private const int MaxItemsPerPage = 10000;

        public FixturesService(HttpClient httpClient, IMemoryCache memoryCache, ILogger<FixturesService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<Fixture>> GetFixturesByDivision(string divisionId, string specificTeamName = "")
        {
            if (string.IsNullOrWhiteSpace(divisionId))
                throw new ArgumentException("divison ID cannot be empty", nameof(divisionId));

            string cacheKey = $"Fixtures-{divisionId}-{specificTeamName}";

            if (_memoryCache.TryGetValue(cacheKey, out List<Fixture> cachedList) && cachedList?.Any() == true)
            {
                _logger.LogInformation("Retrieved fixtures from cache for divison {LeagueId}", divisionId);
                return cachedList;
            }

            try
            {
                var fixtures = await FetchAndParseFixtures(divisionId);
                var filteredFixtures = FilterByTeam(fixtures, specificTeamName);

                _memoryCache.Set(cacheKey, filteredFixtures, DateTimeOffset.Now.Add(_cacheDuration));
                return filteredFixtures;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching fixtures for divison {LeagueId}", divisionId);
                throw;
            }
        }

        private async Task<List<Fixture>> FetchAndParseFixtures(string divisionId)
        {
            var url = $"{BaseUrl}?selectedDivision={divisionId}&itemsPerPage={MaxItemsPerPage}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var document = new HtmlDocument();
            document.LoadHtml(content);

            var results = document.DocumentNode.SelectNodes("//div[@class='fixtures-table table-scroll']/table/tbody/tr");
            if (results == null)
            {
                _logger.LogWarning("No fixtures found for divison {divison}", divisionId);
                return new List<Fixture>();
            }

            return results.Select(ParseFixtureRow).Where(fixture => fixture != null).ToList();
        }

        private Fixture ParseFixtureRow(HtmlNode item)
        {
            try
            {
                // Extract home team
                var homeTeamNode = item.SelectSingleNode(".//td[contains(@class, 'home-team')]");
                var homeTeam = homeTeamNode != null
                    ? Helpers.NormalizeText(homeTeamNode.InnerText)
                    : string.Empty;

                // Extract away team
                var awayTeamNode = item.SelectSingleNode(".//td[contains(@class, 'road-team')]");
                var awayTeam = awayTeamNode != null
                    ? Helpers.NormalizeText(awayTeamNode.InnerText)
                    : string.Empty;

                // Extract location 
                var locationNode = item.SelectSingleNode(".//td[contains(@class, 'left cell-divider') and not(.//span) and not(contains(@class, 'home-team')) and not(contains(@class, 'road-team'))]/a");
                var location = locationNode != null
                    ? Helpers.NormalizeText(locationNode.InnerText)
                    : string.Empty;

                // Extract competition
                string competition = string.Empty;
                if (locationNode?.ParentNode != null)
                {
                    var nextTdNode = locationNode.ParentNode.SelectSingleNode("following-sibling::td");
                    competition = nextTdNode != null
                        ? Helpers.NormalizeText(nextTdNode.InnerText)
                        : string.Empty;
                }

                // Extract date and time
                var dateTimeNode = item.SelectSingleNode(".//td[contains(@class, 'left cell-divider') and .//span]");
                string date = string.Empty;
                string time = string.Empty;
                if (dateTimeNode != null)
                {
                    var dateNode = dateTimeNode.SelectSingleNode(".//span[1]");
                    var timeNode = dateTimeNode.SelectSingleNode(".//span[2]");
                    date = dateNode != null
                        ? Helpers.NormalizeText(dateNode.InnerText)
                        : string.Empty;
                    time = timeNode != null
                        ? Helpers.NormalizeText(timeNode.InnerText)
                        : string.Empty;
                }
                var fixtureDateTime = $"{date} {time}".Trim();

                return new Fixture
                {
                    HomeTeam = homeTeam,
                    AwayTeam = awayTeam,
                    Location = location,
                    FixtureDateTime = fixtureDateTime,
                    Competition = competition
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing fixture row");
                return null;
            }
        }
        
        private List<Fixture> FilterByTeam(List<Fixture> fixtures, string specificTeamName)
        {
            if (string.IsNullOrEmpty(specificTeamName))
                return fixtures;

            return fixtures
                .Where(f => f.AwayTeam.Contains(specificTeamName, StringComparison.OrdinalIgnoreCase) ||
                           f.HomeTeam.Contains(specificTeamName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}