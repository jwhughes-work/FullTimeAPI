using FullTimeAPI.Framework;

namespace FullTimeAPI.Services.Interfaces
{
    public interface IResultsService
    {
        Task<List<Result>> GetResultsByLeague(string divisionId, string specificTeamName = "");
    }
}
