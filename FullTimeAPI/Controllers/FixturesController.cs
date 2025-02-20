using FullTimeAPI.Services.Interfaces;
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

        /// <summary>
        /// Retrieves fixtures for a given division or a specific team in that division.
        /// </summary>
        /// <param name="divisionId">The ID of the division to retrieve fixtures for.</param>
        /// <param name="teamName">Optional: The team name (or part of) to filter fixtures (default is an empty string).</param>
        /// <returns>A list of fixtures matching the division/team.</returns>
        /// <response code="200">Returns the list of fixtures</response>
        /// <response code="500">If an error occurs</response>
        [HttpGet("{divisionId}")]
        public async Task<IActionResult> GetFixtures(string divisionId, [FromQuery] string teamName = "")
        {
            try
            {
                var fixtures = await _fixturesService.GetFixturesByDivision(divisionId, teamName);
                return Ok(fixtures);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }
    }
}
