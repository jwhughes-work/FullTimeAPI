using FullTimeAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace FullTimeAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SearchController : ControllerBase
    {
        private readonly IClubService _clubService;

        public SearchController(IClubService clubService)
        {
            _clubService = clubService;
        }

        // GET api/search/{clubSearch}
        [HttpGet("{clubSearch}")]
        [ActionName("SearchForClub")]
        public async Task<IActionResult> GetClubsSearch(string clubSearch)
        {
            try
            {
                var results = await _clubService.FindClubs(clubSearch);
                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }
    }
}
