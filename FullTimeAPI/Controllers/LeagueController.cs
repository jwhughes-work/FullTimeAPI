using FullTimeAPI.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FullTimeAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LeagueController : ControllerBase
    {
        private readonly ILeagueService _leagueService;

        public LeagueController(ILeagueService leagueService)
        {
            _leagueService = leagueService;
        }

        /// <summary>
        /// Retrieves the league table for a given division
        /// </summary>
        /// <param name="divisionId">The ID of the division to retrieve the table for.</param>
        /// <returns>A list fo the divion table</returns>
        /// <response code="200">Returns the list of results</response>
        /// <response code="500">If an error occurs</response>
        [HttpGet("{divisionId}")]
        public async Task<IActionResult> GetLeague(string divisionId)
        {
            try
            {
                var results = await _leagueService.GetLeagueStandings(divisionId);
                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }
    }
}
