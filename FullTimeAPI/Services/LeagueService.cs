using FullTimeAPI.Framework;
using FullTimeAPI.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;

namespace FullTimeAPI.Services
{
    public class LeagueService : ILeagueService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<LeagueService> _logger;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);
        private const string BaseUrl = "https://fulltime.thefa.com/table.html";
        private const int MaxItemsPerPage = 10000;

        public LeagueService(HttpClient httpClient, IMemoryCache memoryCache, ILogger<LeagueService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<LeagueTable>> GetLeagueStandings(string divionId)
        {
            if (string.IsNullOrWhiteSpace(divionId))
                throw new ArgumentException("League ID cannot be empty", nameof(divionId));

            string cacheKey = $"League-{divionId}";

            if (_memoryCache.TryGetValue(cacheKey, out List<LeagueTable> cachedList) && cachedList?.Any() == true)
            {
                _logger.LogInformation("Retrieved league {LeagueId}", divionId);
                return cachedList;
            }

            try
            {
                var leagueTable = await FetchAndParseLeague(divionId);

                _memoryCache.Set(cacheKey, leagueTable, DateTimeOffset.Now.Add(_cacheDuration));
                return leagueTable;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching league {LeagueId}", divionId);
                throw;
            }
        }

        private async Task<List<LeagueTable>> FetchAndParseLeague(string leagueId)
        {
            var url = $"{BaseUrl}?selectedDivision={leagueId}&itemsPerPage={MaxItemsPerPage}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var document = new HtmlDocument();
            document.LoadHtml(content);

            var resultsNode = document.GetElementbyId("league-table");
            if (resultsNode == null)
            {
                _logger.LogWarning("No league table found");
                return new List<LeagueTable>();
            }

            var resultNodes = resultsNode.SelectNodes("//div[@class='table-scroll']/table/tbody/tr");
            if (resultNodes == null)
            {
                _logger.LogWarning("No result nodes found for league");
                return new List<LeagueTable>();
            }

            return resultNodes.Select(ParseLeagueRow).Where(result => result != null).ToList();
        }

        private LeagueTable ParseLeagueRow(HtmlNode item)
        {
            try
            {
                var position = int.Parse(Helpers.NormalizeText(item.SelectSingleNode("td[1]")?.InnerText ?? string.Empty));
                var teamName = Helpers.NormalizeText(item.SelectSingleNode("td[2]/a")?.InnerText ?? string.Empty);
                var gamesPlayed = int.Parse(Helpers.NormalizeText(item.SelectSingleNode("td[3]")?.InnerText ?? string.Empty));
                var wins = int.Parse(Helpers.NormalizeText(item.SelectSingleNode("td[4]")?.InnerText ?? string.Empty));
                var draws = int.Parse(Helpers.NormalizeText(item.SelectSingleNode("td[5]")?.InnerText ?? string.Empty));
                var losses = int.Parse(Helpers.NormalizeText(item.SelectSingleNode("td[6]")?.InnerText ?? string.Empty));
                var gd = int.Parse(Helpers.NormalizeText(item.SelectSingleNode("td[7]")?.InnerText ?? string.Empty));
                var pointsString = Helpers.NormalizeText(item.SelectSingleNode("td[8]")?.InnerText ?? string.Empty).Replace("*", "");
                var points = int.Parse(pointsString);

                return new LeagueTable
                {
                    Position = position,
                    TeamName = teamName,
                    GamesPlayed = gamesPlayed,
                    GamesWon = wins,
                    GamesDrawn = draws,
                    GamesLost = losses,
                    GoalDifference = gd,
                    Points = points
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing league row");
                return null;
            }
        }
    }
}
