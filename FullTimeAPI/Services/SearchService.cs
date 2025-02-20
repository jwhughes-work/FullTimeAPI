using FullTimeAPI.Framework;
using FullTimeAPI.Models;
using FullTimeAPI.Services.Interfaces;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;

namespace FullTimeAPI.Services
{
    public class SearchService : ISearchService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<FixturesService> _logger;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);
        
        public SearchService(HttpClient httpClient, IMemoryCache memoryCache, ILogger<FixturesService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region FindClubs
        public async Task<List<ClubSearch>> FindClubs(string searchName)
        {
            if (string.IsNullOrEmpty(searchName))
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
            var url = $"https://fulltime.thefa.com/home/search.html?partLeagueOrClubNameSearchFilter={team}&clubSearchFilter=true";
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
        #endregion

        #region FindTeams
        public async Task<List<TeamSearch>> FindTeamsByClub(string clubId)
        {
            if (string.IsNullOrEmpty(clubId))
                throw new ArgumentException("club id cannot be empty", nameof(clubId));

            string cacheKey = $"TeamSearch-{clubId}";

            if (_memoryCache.TryGetValue(cacheKey, out List<TeamSearch> cachedList) && cachedList?.Any() == true)
            {
                _logger.LogInformation("Retrieved club search");
                return cachedList;
            }

            try
            {
                var teams = await FetchAndParseTeamSearch(clubId);
                _memoryCache.Set(cacheKey, teams, DateTimeOffset.Now.Add(_cacheDuration));
                return teams;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching club search");
                throw;
            }
        }
        
        private async Task<List<TeamSearch>> FetchAndParseTeamSearch(string clubId)
        {
            var url = $"https://fulltime.thefa.com/home/club/{clubId}.html";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var document = new HtmlDocument();
            document.LoadHtml(content);

            var results = document.DocumentNode.SelectNodes("//a[contains(@href, '/DisplayTeam.do?teamID=')]");
            if (results == null)
            {
                _logger.LogWarning("No teams found for club {clubId}", clubId);
                return new List<TeamSearch>();
            }

            return results.Select(ParseTeamRow).Where(fixture => fixture != null).ToList();
        }

        private TeamSearch ParseTeamRow(HtmlNode item)
        {
            try
            {
                var href = item.GetAttributeValue("href", "");

                var queryParams = href.Split('?').Last().Split('&')
                    .Select(p => p.Split('='))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0], p => p[1]);

                if (!queryParams.TryGetValue("teamID", out var teamId) ||
                    !queryParams.TryGetValue("league", out var leagueId))
                {
                    _logger.LogWarning("Team ID or League ID not found in href: {href}", href);
                    return null;
                }

                if (string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(leagueId))
                {
                    _logger.LogWarning("Team ID or League ID not found in href: {href}", href);
                    return null;
                }
                
                var nameNode = item.SelectSingleNode(".//p[@class='bold nomargin']");
                var teamName = nameNode?.InnerText.Trim() ?? "Unknown Team";

               var divNode = item.SelectSingleNode(".//p[@class='smaller']/strong");
                var leagueName = divNode?.InnerText.Trim() ?? "Unknown League";

                return new TeamSearch
                {
                    Id = teamId,
                    Name = teamName,
                    LeagueId = leagueId,
                    LeagueName = leagueName
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing club row");
                return null;
            }
        }
        #endregion

        #region FindLeagues
        public async Task<List<LeagueSearch>> FindLeagues(string searchName)
        {
            if (string.IsNullOrEmpty(searchName))
                throw new ArgumentException("league name cannot be empty", nameof(searchName));

            string cacheKey = $"LeagueSearch-{searchName}";

            if (_memoryCache.TryGetValue(cacheKey, out List<LeagueSearch> cachedList) && cachedList?.Any() == true)
            {
                _logger.LogInformation("Retrieved club search");
                return cachedList;
            }

            try
            {
                var leagues = await FetchAndParseLeagueSearch(searchName);
                _memoryCache.Set(cacheKey, leagues, DateTimeOffset.Now.Add(_cacheDuration));
                return leagues;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching leagues search");
                throw;
            }
        }

        private async Task<List<LeagueSearch>> FetchAndParseLeagueSearch(string team)
        {
            var url = $"https://fulltime.thefa.com/home/search.html?partLeagueOrClubNameSearchFilter={team}&clubSearchFilter=false";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var document = new HtmlDocument();
            document.LoadHtml(content);

            var results = document.DocumentNode.SelectNodes("//a[contains(@href, '/Index.do?league')]");
            if (results == null)
            {
                _logger.LogWarning("No clubs found for search {team}", team);
                return new List<LeagueSearch>();
            }

            return results.Select(ParseLeaugeRow).Where(fixture => fixture != null).ToList();
        }

        private LeagueSearch ParseLeaugeRow(HtmlNode item)
        {
            try
            {
                var href = item.GetAttributeValue("href", "");
                
                var queryParams = href.Split('?').Last().Split('&')
                    .Select(p => p.Split('='))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0], p => p[1]);

                if (!queryParams.TryGetValue("league", out var leagueId))
                {
                    _logger.LogWarning("League ID not found in href: {href}", href);
                    return null;
                }

                var nameNode = item;
                if (nameNode == null)
                {
                    _logger.LogWarning("League name not found for league ID: {leagueId}");
                    return null;
                }
                string leagueName = string.Join("", nameNode.ChildNodes.Select(node => node.InnerText)).Trim();

                return new LeagueSearch
                {
                    Id = leagueId,
                    Name = leagueName
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing club row");
                return null;
            }
        }
        #endregion
    }
}
