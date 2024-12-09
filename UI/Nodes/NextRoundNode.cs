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

        private TextNode instructions;

        private EventManager eventManager;

        public Round Round { get; private set; }

        public NextRoundNode(EventManager eventManager)
        {
            this.eventManager = eventManager;

            instructions = new TextNode("", Theme.Current.Rounds.Text.XNA);
            instructions.RelativeBounds = new RectangleF(0, 0, 1, 0.1f);
            AddChild(instructions);

            nextItems = new ListNode<TextButtonNode>(Theme.Current.ScrollBar.XNA);
            nextItems.RelativeBounds = new RectangleF(0, instructions.RelativeBounds.Bottom, 1, 1 - instructions.RelativeBounds.Bottom);
            nextItems.Scale(0.9f);
            AddChild(nextItems);
        }

        public void GenerateOptions(Round round)
        {
            Round = round;

            nextItems.ClearDisposeChildren();

            int nextRound = round.RoundNumber + 1;
            instructions.Text = "Add round " + nextRound.ToString() + "?";

            TextButtonNode change = new TextButtonNode("Randomise (Random channels)", Theme.Current.Rounds.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            change.OnClick += Change_OnClick;
            nextItems.AddChild(change);


            TextButtonNode keep = new TextButtonNode("Randomise (Keep Channels)", Theme.Current.Rounds.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            keep.OnClick += Keep_OnClick;
            nextItems.AddChild(keep);

          
            TextButtonNode clone = new TextButtonNode("Clone Round", Theme.Current.Rounds.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            clone.OnClick += Clone_OnClick;
            nextItems.AddChild(clone);

            TextButtonNode paste = new TextButtonNode("Paste Pilots", Theme.Current.Rounds.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            paste.OnClick += Paste_OnClick;
            nextItems.AddChild(paste);


            TextButtonNode lastHeatAgain = new TextButtonNode("Clone Last Heat", Theme.Current.Rounds.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            lastHeatAgain.OnClick += LastAgain_OnClick;
            nextItems.AddChild(lastHeatAgain);

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
