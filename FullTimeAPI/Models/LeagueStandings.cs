namespace FullTimeAPI.Models
{
    public class LeagueStandings
    {
        public string DivisionName { get; set; }
        public List<LeagueTable> Table { get; set; } = new List<LeagueTable>();
    }
}
