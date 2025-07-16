using Composition;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.Video;

namespace UI.Nodes
{
    public class RaceTimeRemainingNode : RaceTimeNode
    {
        public RaceTimeRemainingNode(RaceManager raceManager, ReplayNode replayNode, Color textColor) 
            : base(raceManager, replayNode, textColor)
        {
            Prepend = Translator.Get("Label.Remaining", "Remaining") + " ";
            AddChild(new HoverNode(Theme.Current.Hover.XNA));
        }

        public override void Update(GameTime gameTime)
        {
            if (RaceManager.EventManager.Event != null)
            {
                if (RaceManager.EventManager.Event.RaceLength == TimeSpan.Zero)
                {
                    Text = "";
                    return;
                }
            }

            if (RaceManager.RaceType == EventTypes.CasualPractice)
            {
                Text = "";
                return;
            }

            if (ReplayNode != null && ReplayNode.Active)
            {
                SetTime(ReplayNode.RemainingTime);
            }
            else
            {
                SetTime(RaceManager.RemainingTime);
            }
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.ButtonState == ButtonStates.Released && !EventManager.Event.RulesLocked)
            {
                MouseMenu mouseMenu = new MouseMenu(this);

                int inc = 60;
                for (int i = inc; i <= 600; i += inc)
                {
                    int t = i;
                    mouseMenu.AddItem("Set Race Length " + i + " seconds", () => { EventManager.SetRaceLength(t); });
                }

                mouseMenu.Show(Bounds.X, Bounds.Bottom);
            }

            return base.OnMouseInput(mouseInputEvent);
        }
    }
}
