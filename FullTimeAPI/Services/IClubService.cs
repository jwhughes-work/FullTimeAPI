using FullTimeAPI.Models;

namespace FullTimeAPI.Services
{
    public interface IClubService
    {
        Task<List<ClubSearch>> FindClubs(string searchName);
    }
}
