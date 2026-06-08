using FullTimeAPI.Models;

namespace FullTimeAPI.Services.Interfaces
{
    public interface IFixturesService
    {
        Task<List<Fixture>> GetFixturesByDivision(string divionId, string specificTeamName = "");
    }
}
