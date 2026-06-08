using FullTimeAPI.Models;

namespace FullTimeAPI.Services.Interfaces
{
    public interface ILeagueService
    {
        Task<List<LeagueTable>> GetLeagueStandings(string divisionId);
        Task<List<LeagueTable>> GetTableSnapshot(string divisionId, string teamName);
    }
}
