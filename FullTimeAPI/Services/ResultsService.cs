﻿using FullTimeAPI.Framework;
using FullTimeAPI.Models;
using FullTimeAPI.Services.Interfaces;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;

namespace FullTimeAPI.Services
{
    public class ResultsService : IResultsService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<ResultsService> _logger;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);
        private const string BaseUrl = "https://fulltime.thefa.com/results.html";
        private const int MaxItemsPerPage = 10000;

        public ResultsService(HttpClient httpClient, IMemoryCache memoryCache, ILogger<ResultsService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<Result>> GetResultsByLeague(string divisionId, string specificTeamName = "")
        {
            if (string.IsNullOrWhiteSpace(divisionId))
                throw new ArgumentException("League ID cannot be empty", nameof(divisionId));

            string cacheKey = $"Results-{divisionId}-{specificTeamName}";

            if (_memoryCache.TryGetValue(cacheKey, out List<Result> cachedList) && cachedList?.Any() == true)
            {
                _logger.LogInformation("Retrieved results from cache for league {LeagueId}", divisionId);
                return cachedList;
            }

            try
            {
                var results = await FetchAndParseResults(divisionId);
                var filteredFixtures = FilterByTeam(results, specificTeamName);

                _memoryCache.Set(cacheKey, filteredFixtures, DateTimeOffset.Now.Add(_cacheDuration));
                return filteredFixtures;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching fixtures for league {divisionId}", divisionId);
                throw;
            }
        }

        private async Task<List<Result>> FetchAndParseResults(string divisionId)
        {
            var url = $"{BaseUrl}?selectedDivision={divisionId}&itemsPerPage={MaxItemsPerPage}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var document = new HtmlDocument();
            document.LoadHtml(content);

            var resultsNode = document.GetElementbyId("results-list");
            if (resultsNode == null)
            {
                _logger.LogWarning("No results list found for season");
                return new List<Result>();
            }

            var resultNodes = resultsNode.SelectNodes("div/div[3]/div/div[2]/div");
            if (resultNodes == null)
            {
                _logger.LogWarning("No result nodes found for season");
                return new List<Result>();
            }

            return resultNodes.Select(ParseResultRow).Where(result => result != null).ToList();
        }

        private Result ParseResultRow(HtmlNode item)
        {
            try
            {
                var fixtureDateTime = Helpers.NormalizeText(item.SelectSingleNode(".//div[@class='datetime-col']")?.InnerText ?? string.Empty);
                var homeTeam = Helpers.NormalizeText(item.SelectSingleNode(".//div[@class='home-team-col flex middle right']")?.InnerText ?? string.Empty);
                var awayTeam = Helpers.NormalizeText(item.SelectSingleNode(".//div[@class='road-team-col flex middle left']")?.InnerText ?? string.Empty);
                var rawScore = Helpers.NormalizeText(item.SelectSingleNode(".//div[@class='score-col']")?.InnerText ?? string.Empty);
                var division = Helpers.NormalizeText(item.SelectSingleNode(".//div[@class='fg-col']")?.InnerText ?? string.Empty);

                var scoreForSplitting = rawScore;
                var parenIndex = rawScore.IndexOf('(');
                if (parenIndex >= 0)
                {
                    scoreForSplitting = rawScore.Substring(0, parenIndex).Trim();
                }
                var scoreParts = scoreForSplitting.Split('-');
                string homeScore = scoreParts.Length > 0 ? scoreParts[0].Trim() : string.Empty;
                string awayScore = scoreParts.Length > 1 ? scoreParts[1].Trim() : string.Empty;

                return new Result
                {
                    FixtureDateTime = fixtureDateTime,
                    HomeTeam = homeTeam,
                    AwayTeam = awayTeam,
                    Score = rawScore,
                    HomeScore = homeScore,
                    AwayScore = awayScore,
                    Division = division
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing match result row");
                return null;
            }
        }

        private List<Result> FilterByTeam(List<Result> results, string specificTeamName)
        {
            if (string.IsNullOrEmpty(specificTeamName))
                return results;

            return results
                .Where(f => f.AwayTeam.Contains(specificTeamName, StringComparison.OrdinalIgnoreCase) ||
                            f.HomeTeam.Contains(specificTeamName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

    }
}
