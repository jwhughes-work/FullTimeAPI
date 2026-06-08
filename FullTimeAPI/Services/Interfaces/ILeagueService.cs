using FullTimeAPI.Models;

namespace FullTimeAPI.Services.Interfaces
{
    public interface ILeagueService
    {
        Task<LeagueStandings> GetLeagueStandings(string divisionId);
        Task<LeagueStandings> GetTableSnapshot(string divisionId, string teamName);
    }
}
