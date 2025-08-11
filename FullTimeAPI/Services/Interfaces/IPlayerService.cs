using FullTimeAPI.Framework;

namespace FullTimeAPI.Services.Interfaces
{
    public interface IPlayerService
    {
        Task<Player> GetPlayerStats(string faPlayerId);
    }
}