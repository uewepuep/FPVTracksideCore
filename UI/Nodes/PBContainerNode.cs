using Composition;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class PBContainerNode : Node
    {
        public EventManager EventManager { get; private set; }

        public Pilot Pilot
        {
            set
            {
                PBSpeedNode.Pilot = value;
                PBTimeNode.Pilot = value;
            }
        }

        public PBTimeNode PBTimeNode { get; set; }
        public PBSpeedNode PBSpeedNode { get; set; }

        public bool HasPB
        {
            get
            {
                return PBTimeNode.HasPB || PBSpeedNode.HasPB;
            }
        }

        public float RotatingShowSeconds { get; set; }

        public PBContainerNode(EventManager eventManager, Color textColor, float alphaCorrect)
        {
            PBTimeNode = new PBTimeNode(eventManager, textColor);
            PBTimeNode.Alpha = alphaCorrect;
            AddChild(PBTimeNode);

            PBSpeedNode = new PBSpeedNode(eventManager, textColor);
            PBSpeedNode.Alpha = alphaCorrect;
            AddChild(PBSpeedNode);

            RotatingShowSeconds = 5;
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.ButtonState == ButtonStates.Released)
            {
                MouseMenu mm = new MouseMenu(this);

                PBSpeedNode.MouseMenu(mm);
                PBTimeNode.MouseMenu(mm);

                mm.Show(mouseInputEvent);
                return true;
            }
            return base.OnMouseInput(mouseInputEvent);
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (!HasPB)
                return;

            if (PBTimeNode.HasPB && !PBSpeedNode.HasPB)
            {
                PBTimeNode.Draw(id, parentAlpha);
                return;
            }

            if (PBSpeedNode.HasPB && !PBTimeNode.HasPB)
            {
                PBSpeedNode.Draw(id, parentAlpha);
                return;
            }

            PBNode first;
            PBNode second;

            if (PBTimeNode.DetectionTime >= PBSpeedNode.DetectionTime)
            {
                first = PBTimeNode;
                second = PBSpeedNode;
            }
            else
            {
                first = PBSpeedNode;
                second = PBTimeNode;
            }

            float totalSeconds = (float)(DateTime.Now - first.DetectionTime).TotalSeconds;

            // * 2 because there are 2 options
            float currentProgress = totalSeconds % (RotatingShowSeconds * 2);

            if (currentProgress < RotatingShowSeconds)
            {
                first.Draw(id, parentAlpha);
            }
            else
            {
                second.Draw(id, parentAlpha);
            }
        }
    }
}