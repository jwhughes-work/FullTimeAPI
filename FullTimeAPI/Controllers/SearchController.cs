﻿using FullTimeAPI.Services;
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

        /// <summary>
        /// Searches for clubs by name.
        /// </summary>
        /// <param name="clubName">The club name to search for</param>
        /// <returns>List of matching clubs</returns>
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
        /// Gets all teams for a specific club by ID.
        /// </summary>
        /// <param name="clubId">The club ID</param>
        /// <returns>List of teams within the club</returns>
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
    }
}
