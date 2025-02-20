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
            try
            {
                var results = await _resultsService.GetResultsByLeague(divisionId, teamName);
                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }
    }
}
