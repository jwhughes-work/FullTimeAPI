using FullTimeAPI.Framework;
using FullTimeAPI.Models;
using FullTimeAPI.Services.Interfaces;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;

namespace FullTimeAPI.Services
{
    public class PlayerService : IPlayerService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<PlayerService> _logger;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);
        private const string BaseUrl = "https://fulltime.thefa.com/statsForPlayer.html";

        public PlayerService(IHttpClientFactory httpClientFactory, IMemoryCache memoryCache, ILogger<PlayerService> logger)
        {
            _httpClient = httpClientFactory?.CreateClient("resilient") ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Player> GetPlayerStats(string faPlayerId)
        {
            if (string.IsNullOrWhiteSpace(faPlayerId))
                throw new ArgumentException("Player ID cannot be empty", nameof(faPlayerId));

            string cacheKey = $"PlayerStats-{faPlayerId}";

            if (_memoryCache.TryGetValue(cacheKey, out Player cachedPlayer) && cachedPlayer != null)
            {
                _logger.LogInformation("Retrieved player stats from cache for player {PlayerId}", faPlayerId);
                return cachedPlayer;
            }

            try
            {
                var player = await FetchAndParsePlayerStats(faPlayerId);
                _memoryCache.Set(cacheKey, player, DateTimeOffset.Now.Add(_cacheDuration));
                return player;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching player stats for player {PlayerId}", faPlayerId);
                throw;
            }
        }

        private async Task<Player> FetchAndParsePlayerStats(string faPlayerId)
        {
            var url = $"{BaseUrl}?personID={faPlayerId}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var document = new HtmlDocument();
            document.LoadHtml(content);

            var playerNode = document.GetElementbyId("stats-for-player");
            if (playerNode == null)
            {
                _logger.LogWarning("No player stats found for player {PlayerId}", faPlayerId);
                return new Player();
            }

            var player = new Player { FaId = faPlayerId };
            
            var nameNode = playerNode.SelectSingleNode(".//h1[@class='nomargin']");
            if (nameNode != null)
            {
                var fullText = Helpers.NormalizeText(nameNode.InnerText);
                if (fullText.EndsWith(" Player Stats", StringComparison.OrdinalIgnoreCase))
                {
                    player.Name = fullText.Substring(0, fullText.Length - " Player Stats".Length).Trim();
                }
                else
                {
                    player.Name = fullText;
                }
            }
            
            var teamNode = playerNode.SelectSingleNode(".//div[@class='team-name-logo flex left middle']//div[2]");
            if (teamNode != null)
            {
                player.Team = Helpers.NormalizeText(teamNode.InnerText);
            }

            var playerStats = playerNode.SelectNodes(".//div[@class='stats-table fixed-col-table-wrap']//tbody//tr");
            
            if (playerStats != null)
            {
                foreach (var statRow in playerStats)
                {
                    var cells = statRow.SelectNodes("td");

                    if (cells != null && cells.Count >= 5)
                    {
                        player.Appearances += ParseStat(Helpers.NormalizeText(cells[0].InnerText));
                        player.Goals += ParseStat(Helpers.NormalizeText(cells[1].InnerText));
                        player.Yellows += ParseStat(Helpers.NormalizeText(cells[3].InnerText));
                        player.Reds += ParseStat(Helpers.NormalizeText(cells[4].InnerText));
                    }
                }
            }

            return player;
        }

        private static int ParseStat(string statText)
        {
            if (string.IsNullOrWhiteSpace(statText) || statText == "-")
                return 0;

            return int.TryParse(statText, out int result) ? result : 0;
        }
    }
}