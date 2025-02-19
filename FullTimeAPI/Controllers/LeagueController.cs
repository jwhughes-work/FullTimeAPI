using FullTimeAPI.Services;
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

        // GET api/league/{leagueId}?teamName=someTeam
        [HttpGet("{leagueId}")]
        public async Task<IActionResult> GetLeague(string leagueId)
        {
            try
            {
                var results = await _leagueService.GetLeagueStandings(leagueId);
                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }
    }
}
