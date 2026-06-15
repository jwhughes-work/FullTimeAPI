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
        private const int MaxItemsPerPage = 500;

        public LeagueService(IHttpClientFactory httpClientFactory, IMemoryCache memoryCache, ILogger<LeagueService> logger)
        {
            _httpClient = httpClientFactory?.CreateClient("resilient") ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<LeagueStandings> GetLeagueStandings(string divionId)
        {
            if (string.IsNullOrWhiteSpace(divionId))
                throw new ArgumentException("League ID cannot be empty", nameof(divionId));

            string cacheKey = $"League-{divionId}";

            if (_memoryCache.TryGetValue(cacheKey, out LeagueStandings cachedStandings) && cachedStandings?.Table?.Any() == true)
            {
                _logger.LogInformation("Retrieved league {LeagueId}", divionId);
                return cachedStandings;
            }

            try
            {
                var standings = await FetchAndParseDivision(divionId);

                if (standings.Table.Any())
                    _memoryCache.Set(cacheKey, standings, DateTimeOffset.Now.Add(_cacheDuration));

                return standings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching league {divionId}", divionId);
                throw;
            }
        }

        public async Task<LeagueStandings> GetTableSnapshot(string divisionId, string teamName)
        {
            if (string.IsNullOrWhiteSpace(divisionId))
                throw new ArgumentException("Division ID cannot be empty", nameof(divisionId));

            if (string.IsNullOrWhiteSpace(teamName))
                throw new ArgumentException("Team name cannot be empty", nameof(teamName));

            string cacheKey = $"TableSnap-{divisionId}-{teamName}";

            if (_memoryCache.TryGetValue(cacheKey, out LeagueStandings cachedSnapshot) && cachedSnapshot?.Table?.Any() == true)
            {
                _logger.LogInformation("Retrieved table snapshot from cache for division {DivisionId} and team {TeamName}", divisionId, teamName);
                return cachedSnapshot;
            }

            try
            {
                var fullTable = await GetLeagueStandings(divisionId);
                int teamIndex = fullTable.Table.FindIndex(t => t.TeamName.Contains(teamName, StringComparison.OrdinalIgnoreCase));

                if (teamIndex == -1)
                {
                    _logger.LogWarning("Team {TeamName} not found in division {DivisionId}", teamName, divisionId);
                    return new LeagueStandings { DivisionName = fullTable.DivisionName };
                }

                var selectedIndices = new List<int>();

                if (teamIndex == 0)
                {
                    selectedIndices = new List<int> { 0, 1, 2 };
                }
                else if (teamIndex == (fullTable.Table.Count - 1))
                {
                    selectedIndices = new List<int> { fullTable.Table.Count - 1, fullTable.Table.Count - 2, fullTable.Table.Count - 3 };
                }
                else
                {
                    selectedIndices = new List<int> { teamIndex - 1, teamIndex, teamIndex + 1 };
                }

                var selectedItems = fullTable.Table
                    .Where((item, index) => selectedIndices.Contains(index))
                    .ToList();

                var snapshot = new LeagueStandings
                {
                    DivisionName = fullTable.DivisionName,
                    Table = selectedItems
                };

                if (selectedItems.Any())
                    _memoryCache.Set(cacheKey, snapshot, DateTimeOffset.Now.Add(_cacheDuration));

                return snapshot;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching table snapshot for division {DivisionId} and team {TeamName}", divisionId, teamName);
                throw;
            }
        }

        private async Task<LeagueStandings> FetchAndParseDivision(string divisonId)
        {
            var url = $"{BaseUrl}?selectedDivision={Uri.EscapeDataString(divisonId)}&itemsPerPage={MaxItemsPerPage}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var document = new HtmlDocument();
            document.LoadHtml(content);

            var resultsNode = document.GetElementbyId("league-table");
            if (resultsNode == null)
            {
                LogMissingNode("league-table", response, content);
                return new LeagueStandings();
            }

            var divisionName = Helpers.NormalizeText(resultsNode.SelectSingleNode("div/div[1]/div[1]/h2")?.InnerText ?? string.Empty);

            var resultNodes = resultsNode.SelectNodes("//div[@class='table-scroll']/table/tbody/tr");
            if (resultNodes == null)
            {
                LogMissingNode("league-table rows", response, content);
                return new LeagueStandings { DivisionName = divisionName };
            }

            return new LeagueStandings
            {
                DivisionName = divisionName,
                Table = resultNodes.Select(ParseLeagueRow).Where(result => result != null).ToList()
            };
        }

        // When an expected node is missing we can't tell a genuinely empty division from a
        // redirect/block page (both yield blank). Log enough about the actual response to tell
        // them apart from production logs: final URL surfaces redirects, the snippet surfaces
        // block/consent pages.
        private void LogMissingNode(string nodeName, HttpResponseMessage response, string content)
        {
            var snippet = content.Length > 500 ? content.Substring(0, 500) : content;
            _logger.LogWarning(
                "Missing {NodeName}. status={Status} finalUrl={FinalUrl} contentLength={Length} snippet={Snippet}",
                nodeName, (int)response.StatusCode, response.RequestMessage?.RequestUri, content.Length, snippet);
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
