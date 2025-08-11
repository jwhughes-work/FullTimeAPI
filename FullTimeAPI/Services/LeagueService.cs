using FullTimeAPI.Framework;
using FullTimeAPI.Models;
using FullTimeAPI.Services.Interfaces;
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

        public LeagueService(IHttpClientFactory httpClientFactory, IMemoryCache memoryCache, ILogger<LeagueService> logger)
        {
            _httpClient = httpClientFactory?.CreateClient("resilient") ?? throw new ArgumentNullException(nameof(httpClientFactory));
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
                var leagueTable = await FetchAndParseDivision(divionId);

                _memoryCache.Set(cacheKey, leagueTable, DateTimeOffset.Now.Add(_cacheDuration));
                return leagueTable;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching league {divionId}", divionId);
                throw;
            }
        }

        public async Task<List<LeagueTable>> GetTableSnapshot(string divisionId, string teamName)
        {
            if (string.IsNullOrWhiteSpace(divisionId))
                throw new ArgumentException("Division ID cannot be empty", nameof(divisionId));

            if (string.IsNullOrWhiteSpace(teamName))
                throw new ArgumentException("Team name cannot be empty", nameof(teamName));

            string cacheKey = $"TableSnap-{divisionId}-{teamName}";

            if (_memoryCache.TryGetValue(cacheKey, out List<LeagueTable> cachedList) && cachedList?.Any() == true)
            {
                _logger.LogInformation("Retrieved table snapshot from cache for division {DivisionId} and team {TeamName}", divisionId, teamName);
                return cachedList;
            }

            try
            {
                var fullTable = await GetLeagueStandings(divisionId);
                int teamIndex = fullTable.FindIndex(t => t.TeamName.Contains(teamName, StringComparison.OrdinalIgnoreCase));

                if (teamIndex == -1)
                {
                    _logger.LogWarning("Team {TeamName} not found in division {DivisionId}", teamName, divisionId);
                    return new List<LeagueTable>();
                }

                var selectedIndices = new List<int>();

                if (teamIndex == 0)
                {
                    selectedIndices = new List<int> { 0, 1, 2 };
                }
                else if (teamIndex == (fullTable.Count - 1))
                {
                    selectedIndices = new List<int> { fullTable.Count - 1, fullTable.Count - 2, fullTable.Count - 3 };
                }
                else
                {
                    selectedIndices = new List<int> { teamIndex - 1, teamIndex, teamIndex + 1 };
                }

                var selectedItems = fullTable
                    .Where((item, index) => selectedIndices.Contains(index))
                    .ToList();

                _memoryCache.Set(cacheKey, selectedItems, DateTimeOffset.Now.Add(_cacheDuration));
                return selectedItems;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching table snapshot for division {DivisionId} and team {TeamName}", divisionId, teamName);
                throw;
            }
        }

        private async Task<List<LeagueTable>> FetchAndParseDivision(string divisonId)
        {
            var url = $"{BaseUrl}?selectedDivision={divisonId}&itemsPerPage={MaxItemsPerPage}";
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
                _logger.LogWarning("No result nodes found for division");
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
