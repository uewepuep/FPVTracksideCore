using Composition;
using Composition.Nodes;
using RaceLib;
using RaceLib.Format;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class NextRoundNode : Node
    {
        private ListNode<TextButtonNode> nextItems;

        private EventManager eventManager;

        public Round Round { get; private set; }

        public NextRoundNode(EventManager eventManager)
        {
            this.eventManager = eventManager;

            nextItems = new ListNode<TextButtonNode>(Theme.Current.ScrollBar.XNA);
            nextItems.Scale(0.9f);
            AddChild(nextItems);
        }

        public void GenerateOptions(Round round)
        {
            Round = round;

            nextItems.ClearDisposeChildren();

            int nextRound = round.RoundNumber + 1;

            TextButtonNode change = new TextButtonNode("Randomise (Random channels)", Theme.Current.Rounds.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            change.OnClick += Change_OnClick;
            nextItems.AddChild(change);


            TextButtonNode keep = new TextButtonNode("Randomise (Keep Channels)", Theme.Current.Rounds.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            keep.OnClick += Keep_OnClick;
            nextItems.AddChild(keep);

          
            TextButtonNode clone = new TextButtonNode("Clone Last Round", Theme.Current.Rounds.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            clone.OnClick += Clone_OnClick;
            nextItems.AddChild(clone);

            TextButtonNode lastHeatAgain = new TextButtonNode("Clone Last Race", Theme.Current.Rounds.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            lastHeatAgain.OnClick += LastAgain_OnClick;
            nextItems.AddChild(lastHeatAgain);

            TextButtonNode paste = new TextButtonNode("Paste Pilots", Theme.Current.Rounds.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            paste.OnClick += Paste_OnClick;
            nextItems.AddChild(paste);

            if (eventManager.ExternalRaceProviders != null)
            {
                foreach (var external in eventManager.ExternalRaceProviders)
                {
                    var t = external;
                    TextButtonNode externalButton = new TextButtonNode("Next " + external.Name, Theme.Current.Rounds.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
                    externalButton.OnClick += (m) => { t.TriggerCreateRaces(Round); };
                    nextItems.AddChild(externalButton);
                }
            }

            nextItems.RequestLayout();
        }

        private void Paste_OnClick(Composition.Input.MouseInputEvent mie)
        {
            var lines = PlatformTools.Clipboard.GetLines();
            eventManager.AddPilotsFromLines(lines);
        }

        private void Change_OnClick(Composition.Input.MouseInputEvent mie)
        {
            RoundPlan roundPlan = new RoundPlan(eventManager, Round);
            roundPlan.ChannelChange = RoundPlan.ChannelChangeEnum.Change;
            eventManager.RoundManager.GenerateRound(roundPlan);
        }

        private void Keep_OnClick(Composition.Input.MouseInputEvent mie)
        {
            RoundPlan roundPlan = new RoundPlan(eventManager, Round);
            roundPlan.ChannelChange = RoundPlan.ChannelChangeEnum.KeepFromPreviousRound;
            eventManager.RoundManager.GenerateRound(roundPlan);
        }

        private void Clone_OnClick(Composition.Input.MouseInputEvent mie)
        {
            eventManager.RoundManager.CloneRound(Round);
        }

        private void LastAgain_OnClick(Composition.Input.MouseInputEvent mie)
        {
            eventManager.RoundManager.CloneLastHeat(Round);
        }
    }
}
