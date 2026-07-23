using RaceLib;
using RaceLib.Format;

namespace UI.Nodes.Rounds
{
    // Standings table sourced from a Lua round format's standings() function.
    public class EventLuaStandingsNode : EventStandingsResultNode
    {
        public EventLuaStandingsNode(RoundsNode roundsNode, EventManager ev, Round round)
            : base(roundsNode, ev, round)
        {
        }

        protected override StandingsResult GetStandingsResult(Pilot[] stagePilots)
        {
            LuaRoundFormat format = EventManager.RoundManager.GetRoundFormat(Stage) as LuaRoundFormat;
            return format?.GetStandings(stagePilots);
        }
    }
}
