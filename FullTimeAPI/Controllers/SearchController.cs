using FullTimeAPI.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace FullTimeAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SearchController : ControllerBase
    {
        private readonly ISearchService _clubService;

        public SearchController(ISearchService clubService)
        {
            _clubService = clubService;
        }

        /// <summary>
        /// Searches for clubs by entering name (or part of name) Will return ClubId.
        /// </summary>
        /// <param name="clubName">Club name to search for</param>
        /// <returns>List of matching clubs</returns>
        /// <remarks>
        /// Sample Club Name:
        ///
        /// Axbridge
        ///
        /// </remarks>
        /// <response code="200">Returns the list of clubs</response>
        /// <response code="500">If an error occurs</response>
        [HttpGet("clubs/{clubName}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetClubsByName(string clubName)
        {
            try
            {
                var results = await _clubService.FindClubs(clubName);
                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets all teams for a specific club by ClubId.
        /// </summary>
        /// <param name="clubId">The club ID</param>
        /// <returns>List of teams within the club</returns>
        /// <remarks>
        /// Sample Club Id:
        ///
        /// 462688870
        ///
        /// </remarks>
        /// <response code="200">Returns the list of teams</response>
        /// <response code="500">If an error occurs</response>
        [HttpGet("teams/{clubId}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetTeamsByClubId(string clubId)
        {
            try
            {
                var results = await _clubService.FindTeamsByClub(clubId);
                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }

        /// <summary>
        /// Searches for leagues by entering name (or part of name) Will return LeagueId.
        /// </summary>
        /// <param name="leagueName">League name to search for</param>
        /// <returns>List of matching leagues</returns>
        /// /// <remarks>
        /// Sample League Name:
        ///
        /// Weston
        ///
        /// </remarks>
        /// <response code="200">Returns the list of leagues</response>
        /// <response code="500">If an error occurs</response>
        [HttpGet("leagues/{leagueName}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetLeaguesByName(string leagueName)
        {
            try
            {
                var results = await _clubService.FindLeagues(leagueName);
                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }

    }
}
