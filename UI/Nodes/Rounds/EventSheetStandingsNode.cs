using RaceLib;
using RaceLib.Format;

namespace UI.Nodes.Rounds
{
    // Standings table sourced from a spreadsheet round format's "Standing(s) N" block.
    public class EventSheetStandingsNode : EventStandingsResultNode
    {
        public EventSheetStandingsNode(RoundsNode roundsNode, EventManager ev, Round round)
            : base(roundsNode, ev, round)
        {
        }

        protected override StandingsResult GetStandingsResult(Pilot[] stagePilots)
        {
            RoundSheetFormat format = EventManager.RoundManager.SheetFormatManager.GetRoundSheetFormat(Round);
            return format?.GetStandings(stagePilots);
        }
    }
}
