using FullTimeAPI.Framework;

namespace FullTimeAPI.Services
{
    public interface IResultsService
    {
        Task<List<Result>> GetResultsByLeague(string leagueId, string specificTeamName = "");
    }
}
