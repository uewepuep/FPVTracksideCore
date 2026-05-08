namespace RaceLib.Format
{
    public class StandingsResult
    {
        public string[] Headings { get; set; }
        public StandingsRow[] Rows { get; set; }
    }

    public class StandingsRow
    {
        public string Name { get; set; }
        public string[] Values { get; set; }
    }
}
