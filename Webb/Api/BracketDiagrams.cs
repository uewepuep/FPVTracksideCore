using System;
using System.Collections.Generic;
using System.Linq;
using Engine = global::BracketEngine;

namespace Webb.Api
{
    // The bracket diagram as data: the engine's public-diagram projection
    // (the same one the AUFPV platform draws — live results where they exist,
    // seeds-always-win placeholders where they don't) turned into nodes, seats
    // and edges. The page draws it; nothing here knows about pixels.
    public static class BracketDiagrams
    {
        public static List<DiagramDto> Build(Engine.IRaceFormat f, Func<string, string> name)
        {
            List<DiagramDto> diagrams = new List<DiagramDto>();

            Engine.EliminationFormat elim = f as Engine.EliminationFormat;
            if (elim != null)
            {
                diagrams.Add(FromProjection(null, "", Engine.BracketDiagram.Elimination(elim.Bracket()), name));
                return diagrams;
            }

            Engine.QualifierFormat qual = f as Engine.QualifierFormat;
            if (qual != null)
            {
                diagrams.Add(FromProjection(null, "", Engine.BracketDiagram.Qualifier(qual.Qual()), name));
                return diagrams;
            }

            Engine.MainsProgressionFormat mains = f as Engine.MainsProgressionFormat;
            if (mains != null)
            {
                foreach (var main in Engine.BracketDiagram.MainsProgression(mains.Mains()))
                {
                    diagrams.Add(FromProjection(main.Label + " Main", main.Label + ":", main.P, name));
                }
                return diagrams;
            }

            // Ladder and points mains are tables, not diagrams.
            return diagrams;
        }

        // prefix: heat ids in the projection are a main's inner ids ("W1-1");
        // the bracket's heats are addressed "A:W1-1".
        private static DiagramDto FromProjection(string title, string prefix, Engine.BracketProjection p, Func<string, string> name)
        {
            List<Engine.Race> races = p.Projected.State().Races
                .Where(r => r.Bracket != "filler")
                .ToList();

            Dictionary<string, Dictionary<string, Engine.PilotSlot>> predicted = p.PredictedSlots ?? new Dictionary<string, Dictionary<string, Engine.PilotSlot>>();
            Func<string, string> id = raw => raw.StartsWith(prefix) ? raw : prefix + raw;

            // A chase-the-ace series is a run of re-races, not a bracket round:
            // lay its heats across the page (as the platform does) instead of
            // stacking identical boxes down one column.
            Dictionary<string, int> column = new Dictionary<string, int>();
            int ctaIndex = 0;
            foreach (Engine.Race r in races.OrderBy(r => r.Round).ThenBy(r => Engine.BracketFlow.RaceSortKey(r.Id)))
            {
                column[r.Id] = r.Bracket == "chase-the-ace" ? r.Round + ctaIndex++ : r.Round;
            }

            Func<Engine.Race, List<string>> finishOrder = r =>
                r.IsComplete ? (r.Winners ?? new List<string>()).Concat(r.Losers ?? new List<string>()).ToList() : new List<string>();

            Dictionary<string, Engine.Race> byId = races.ToDictionary(r => r.Id);

            List<DiagramNodeDto> nodes = new List<DiagramNodeDto>();
            foreach (Engine.Race r in races)
            {
                Dictionary<string, Engine.PilotSlot> slots;
                predicted.TryGetValue(r.Id, out slots);
                List<string> order = finishOrder(r);
                HashSet<string> winners = new HashSet<string>(r.Winners ?? new List<string>());

                List<DiagramSeatDto> seats = new List<DiagramSeatDto>();
                foreach (string pilot in r.Pilots ?? new List<string>())
                {
                    Engine.PilotSlot slot;
                    if (slots != null && slots.TryGetValue(pilot, out slot))
                    {
                        seats.Add(new DiagramSeatDto { FromHeat = id(slot.SourceRaceId), FromRank = slot.SourceRank });
                        continue;
                    }
                    int pos = order.IndexOf(pilot);
                    seats.Add(new DiagramSeatDto
                    {
                        PilotId = pilot,
                        Name = name(pilot),
                        Position = pos >= 0 ? pos + 1 : 0,
                        Advanced = r.IsComplete && winners.Contains(pilot),
                    });
                }

                nodes.Add(new DiagramNodeDto
                {
                    Id = id(r.Id),
                    Column = column[r.Id],
                    Lane = string.IsNullOrEmpty(r.Bracket) ? "winners" : r.Bracket,
                    Label = r.Id,
                    Seats = seats,
                });
            }

            List<DiagramEdgeDto> edges = new List<DiagramEdgeDto>();
            foreach (Engine.BracketConnector c in p.Connectors ?? new List<Engine.BracketConnector>())
            {
                if (!byId.ContainsKey(c.FromId) || !byId.ContainsKey(c.ToId))
                    continue;

                Dictionary<string, Engine.PilotSlot> slots;
                predicted.TryGetValue(c.ToId, out slots);
                List<string> order = finishOrder(byId[c.FromId]);

                SortedSet<int> ranks = new SortedSet<int>();
                foreach (string pilot in c.Pilots ?? new List<string>())
                {
                    Engine.PilotSlot slot;
                    if (slots != null && slots.TryGetValue(pilot, out slot))
                    {
                        if (slot.SourceRank > 0) ranks.Add(slot.SourceRank);
                        continue;
                    }
                    int pos = order.IndexOf(pilot);
                    if (pos >= 0) ranks.Add(pos + 1);
                }

                edges.Add(new DiagramEdgeDto { From = id(c.FromId), To = id(c.ToId), Ranks = ranks.ToList() });
            }

            return new DiagramDto { Title = title, Nodes = nodes, Edges = edges };
        }
    }
}
