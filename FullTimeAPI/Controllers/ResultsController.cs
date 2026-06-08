using FullTimeAPI.Services.Interfaces;
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

        /// <summary>
        /// Retrieves results for a given division or a specific team in that division.
        /// </summary>
        /// <param name="divisionId">The ID of the division to retrieve results for.</param>
        /// <param name="teamName">Optional: The team name (or part of) to filter results (default is an empty string).</param>
        /// <returns>A list of results matching the division/team.</returns>
        /// <response code="200">Returns the list of results</response>
        /// <response code="500">If an error occurs</response>
        [HttpGet("{divisionId}")]
        public async Task<IActionResult> GetResults(string divisionId, [FromQuery] string teamName = "")
        {
            var results = await _resultsService.GetResultsByLeague(divisionId, teamName);
            return Ok(results);
        }

        /// <summary>
        /// Retrieves the last 5 match results for a team to show their form guide.
        /// </summary>
        /// <param name="divisionId">The ID of the division.</param>
        /// <param name="teamName">The team name to get form for.</param>
        /// <returns>A list of form results (W, D, L, P) for the last 5 matches.</returns>
        /// <response code="200">Returns the team's form guide</response>
        /// <response code="400">If the division ID or team name is invalid</response>
        /// <response code="500">If an error occurs</response>
        [HttpGet("{divisionId}/form")]
        public async Task<IActionResult> GetTeamForm(string divisionId, [FromQuery] string teamName)
        {
            var form = await _resultsService.GetTeamForm(divisionId, teamName);
            return Ok(form);
        }
    }
}
