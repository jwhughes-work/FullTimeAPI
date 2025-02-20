using FullTimeAPI.Models;

namespace FullTimeAPI.Services.Interfaces
{
    public interface ISearchService
    {
        Task<List<ClubSearch>> FindClubs(string searchName);
        Task<List<TeamSearch>> FindTeamsByClub(string clubId);
        Task<List<LeagueSearch>> FindLeagues(string searchName);
        Task<List<DivisionSearch>> FindDivisonById(string leagueId);
    }
}
