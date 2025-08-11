using FullTimeAPI.Framework;
using FullTimeAPI.Models;

namespace FullTimeAPI.Services.Interfaces
{
    public interface IResultsService
    {
        Task<List<Result>> GetResultsByLeague(string divisionId, string specificTeamName = "");
        Task<List<FormResult>> GetTeamForm(string divisionId, string teamName);
    }
}
