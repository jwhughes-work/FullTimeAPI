namespace FullTimeAPI.Models
{
    public class LeagueSearch
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<DivisionSearch> Divisions { get; set; } = new();
    }

    public class DivisionSearch
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }
}
