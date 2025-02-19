using FullTimeAPI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FullTimeAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FixturesController : ControllerBase
    {
        private readonly IFixturesService _fixturesService;

        public FixturesController(IFixturesService fixturesService)
        {
            _fixturesService = fixturesService;
        }

        // GET api/fixtures/{leagueId}?teamName=someTeam
        [HttpGet("{leagueId}")]
        public async Task<IActionResult> GetFixtures(string leagueId, [FromQuery] string teamName = "")
        {
            try
            {
                var fixtures = await _fixturesService.GetFixturesByLeague(leagueId, teamName);
                return Ok(fixtures);
            }
            catch (Exception ex)
            {
                // You can return more detailed error info if needed
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }
    }
}
