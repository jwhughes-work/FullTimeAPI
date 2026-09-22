using FullTimeAPI.Models;
using FullTimeAPI.Services.Interfaces;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;

namespace FullTimeAPI.Services
{
    public class FixturesService : IFixturesService
    {
        private readonly IPageFetcher _pageFetcher;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<FixturesService> _logger;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);
        private const string BaseUrl = "https://fulltime.thefa.com/fixtures.html";
        private const int MaxItemsPerPage = 500;

        public FixturesService(IPageFetcher pageFetcher, IMemoryCache memoryCache, ILogger<FixturesService> logger)
        {
            _pageFetcher = pageFetcher ?? throw new ArgumentNullException(nameof(pageFetcher));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<Fixture>> GetFixturesByDivision(string divisionId, string specificTeamName = "")
        {
            if (string.IsNullOrWhiteSpace(divisionId))
                throw new ArgumentException("division ID cannot be empty", nameof(divisionId));

            string cacheKey = $"Fixtures-{divisionId}-{specificTeamName}";

            if (_memoryCache.TryGetValue(cacheKey, out List<Fixture> cachedList) && cachedList?.Any() == true)
            {
                _logger.LogInformation("Retrieved fixtures from cache for division {LeagueId}", divisionId);
                return cachedList;
            }

            try
            {
                var fixtures = await FetchAndParseFixtures(divisionId);
                var filteredFixtures = FilterByTeam(fixtures, specificTeamName);

                if (filteredFixtures.Any())
                    _memoryCache.Set(cacheKey, filteredFixtures, DateTimeOffset.Now.Add(_cacheDuration));

                return filteredFixtures;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching fixtures for division {LeagueId}", divisionId);
                throw;
            }
        }

        public async Task<List<Fixture>> GetFixturesByDivision(string divisionId, string selectedSeason, string specificTeamName = "")
        {
            if (string.IsNullOrWhiteSpace(divisionId))
                throw new ArgumentException("division ID cannot be empty", nameof(divisionId));

            string cacheKey = $"Fixtures-{divisionId}-{selectedSeason}-{specificTeamName}";

            if (_memoryCache.TryGetValue(cacheKey, out List<Fixture> cachedList) && cachedList?.Any() == true)
            {
                _logger.LogInformation("Retrieved fixtures from cache for division {LeagueId} and season {Season}", divisionId, selectedSeason);
                return cachedList;
            }

            try
            {
                var fixtures = await FetchAndParseFixtures(divisionId, selectedSeason);
                var filteredFixtures = FilterByTeam(fixtures, specificTeamName);

                if (filteredFixtures.Any())
                    _memoryCache.Set(cacheKey, filteredFixtures, DateTimeOffset.Now.Add(_cacheDuration));

                return filteredFixtures;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching fixtures for division {LeagueId} and season {Season}", divisionId, selectedSeason);
                throw;
            }
        }

        private async Task<List<Fixture>> FetchAndParseFixtures(string divisionId)
        {
            var url = $"{BaseUrl}?selectedDivision={Uri.EscapeDataString(divisionId)}&selectedRelatedFixtureOption=1&itemsPerPage={MaxItemsPerPage}";
            var result = await _pageFetcher.GetHtmlAsync(url);
            EnsureSuccessOrLog(result, $"fixtures (division {divisionId})");

            var content = result.Content;
            var document = new HtmlDocument();
            document.LoadHtml(content);

            var results = document.DocumentNode.SelectNodes("//div[@class='fixtures-table table-scroll']/table/tbody/tr");
            if (results == null)
            {
                LogMissingNode($"fixtures rows (division {divisionId})", result);
                return new List<Fixture>();
            }

            return results.Select(ParseFixtureRow).Where(fixture => fixture != null).ToList();
        }

        private async Task<List<Fixture>> FetchAndParseFixtures(string divisionId, string selectedSeason)
        {
            var url = $"{BaseUrl}?selectedDivision={Uri.EscapeDataString(divisionId)}&selectedRelatedFixtureOption=1&itemsPerPage={MaxItemsPerPage}&selectedSeason={Uri.EscapeDataString(selectedSeason)}";
            var result = await _pageFetcher.GetHtmlAsync(url);
            EnsureSuccessOrLog(result, $"fixtures (division {divisionId}, season {selectedSeason})");

            var content = result.Content;
            var document = new HtmlDocument();
            document.LoadHtml(content);

            var results = document.DocumentNode.SelectNodes("//div[@class='fixtures-table table-scroll']/table/tbody/tr");
            if (results == null)
            {
                LogMissingNode($"fixtures rows (division {divisionId}, season {selectedSeason})", result);
                return new List<Fixture>();
            }

            return results.Select(ParseFixtureRow).Where(fixture => fixture != null).ToList();
        }

        // After the fetcher's retry policy has given up, a non-success response (403, 5xx, a
        // bounce to an unexpected page) would otherwise throw a bare exception that the
        // middleware turns into an opaque 503. Log what FullTime actually returned first - status,
        // final URL and a body snippet - so the failure is diagnosable, then throw as before.
        private void EnsureSuccessOrLog(PageFetchResult result, string context)
        {
            if (result.IsSuccess)
                return;

            var body = result.Content ?? string.Empty;
            var snippet = body.Length > 500 ? body.Substring(0, 500) : body;

            _logger.LogWarning(
                "Upstream non-success for {Context}. status={Status} finalUrl={FinalUrl} bodyLength={Length} snippet={Snippet}",
                context, result.StatusCode, result.FinalUrl, body.Length, snippet);

            throw new HttpRequestException($"FullTime request failed with status {result.StatusCode} for {context}");
        }

        // When an expected node is missing we can't tell a genuinely empty division from a
        // redirect/block page (both yield blank). Log enough about the actual response to tell
        // them apart from production logs: final URL surfaces redirects, the snippet surfaces
        // block/consent pages.
        private void LogMissingNode(string nodeName, PageFetchResult result)
        {
            var content = result.Content ?? string.Empty;
            var snippet = content.Length > 500 ? content.Substring(0, 500) : content;
            _logger.LogWarning(
                "Missing {NodeName}. status={Status} finalUrl={FinalUrl} contentLength={Length} snippet={Snippet}",
                nodeName, result.StatusCode, result.FinalUrl, content.Length, snippet);
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