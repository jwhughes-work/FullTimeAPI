using FullTimeAPI.Models;

namespace FullTimeAPI.Services.Interfaces
{
    public interface IPlayerService
    {
        Task<Player> GetPlayerStats(string faPlayerId);
    }
}