namespace FullTimeAPI.Framework
{
    public class LeagueTable
    {
        public int Position { get; set; }
        public string TeamName { get; set; }
        public int GamesPlayed { get; set; }
        public int GamesWon { get; set; }
        public int GamesDrawn { get; set; }
        public int GamesLost { get; set; }
        public int GoalDifference { get; set; }
        public int Points { get; set; }
    }
}
