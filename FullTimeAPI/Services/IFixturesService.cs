﻿using FullTimeAPI.Framework;

namespace FullTimeAPI.Services
{
    public interface IFixturesService
    {
        Task<List<Fixture>> GetFixturesByDivision(string divionId, string specificTeamName = "");
    }
}
