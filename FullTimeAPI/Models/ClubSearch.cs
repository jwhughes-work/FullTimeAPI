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
        public string Division { get; set; }
    }
}
