using FullTimeAPI.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FullTimeAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PlayerController : ControllerBase
    {
        private readonly IPlayerService _playerService;

        public PlayerController(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        /// <summary>
        /// Retrieves player statistics for a given FA Player ID.
        /// </summary>
        /// <param name="faPlayerId">The FA Player ID to retrieve statistics for.</param>
        /// <returns>Player statistics including appearances, goals, and cards.</returns>
        /// <response code="200">Returns the player statistics</response>
        /// <response code="400">If the player ID is invalid</response>
        /// <response code="500">If an error occurs</response>
        [HttpGet("{faPlayerId}")]
        public async Task<IActionResult> GetPlayerStats(string faPlayerId)
        {
            try
            {
                var player = await _playerService.GetPlayerStats(faPlayerId);
                return Ok(player);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "An error occurred while retrieving player statistics." });
            }
        }
    }
}