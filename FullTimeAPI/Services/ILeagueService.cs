using FullTimeAPI.Framework;

namespace FullTimeAPI.Services
{
    public interface ILeagueService
    {
        Task<List<LeagueTable>> GetLeagueStandings(string divisionId);
    }
}
