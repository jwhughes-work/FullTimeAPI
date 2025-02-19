using FullTimeAPI.Framework;

namespace FullTimeAPI.Services
{
    public interface IFixturesService
    {
        Task<List<Fixture>> GetFixturesByLeague(string leagueId, string specificTeamName = "");
    }
}
