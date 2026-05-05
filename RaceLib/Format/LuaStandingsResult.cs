namespace RaceLib.Format
{
    public class LuaStandingsResult
    {
        public string[] Headings { get; set; }
        public LuaStandingsRow[] Rows { get; set; }
    }

    public class LuaStandingsRow
    {
        public string Name { get; set; }
        public string[] Values { get; set; }
    }
}
