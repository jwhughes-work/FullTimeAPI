namespace FullTimeAPI.Models
{
    public class ClubSearch
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public int NumberOfTeams { get; set; }
    }

    public class TeamSearch
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string LeagueName { get; set; }
        public string LeagueId { get; set; }
    }
}
