using FullTimeAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace FullTimeAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ResultsController : ControllerBase
    {
        private readonly IResultsService _resultsService;

        public ResultsController(IResultsService resultsService)
        {
            _resultsService = resultsService;
        }

        // GET api/results/{leagueId}?teamName=someTeam
        [HttpGet("{leagueId}")]
        public async Task<IActionResult> GetResults(string leagueId, [FromQuery] string teamName = "")
        {
            try
            {
                var results = await _resultsService.GetResultsByLeague(leagueId, teamName);
                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }
    }
}
