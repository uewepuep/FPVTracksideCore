using Composition;
using Composition.Nodes;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class PilotProfileNode : AlphaAnimatedNode
    {
        public ImageNode PilotPhoto { get; set; }

        public Pilot Pilot { get; set; }

        public TextNode TextNode { get; set; }

        public PilotProfileNode()
        {
            AnimationTime = TimeSpan.FromSeconds(1);

            TextNode = new TextNode("", Theme.Current.PilotViewTheme.PilotOverlayText.XNA);
            TextNode.RelativeBounds = new RectangleF(0, 0.9f, 1, 0.1f);
            TextNode.Style.Italic = true;
            TextNode.Style.Border = true;
            AddChild(TextNode);

            PilotPhoto = new ImageNode();
            PilotPhoto.RelativeBounds = new RectangleF(0, 0, 1, TextNode.RelativeBounds.Y);

            PilotPhoto.Alignment = RectangleAlignment.Center;
            PilotPhoto.KeepAspectRatio = true;
            AddChild(PilotPhoto);

            TextNode.Scale(0.9f);
        }

        public void SetPilot(Pilot pilot)
        {
            Pilot = pilot;

            string filename = "";

            if (pilot != null)
            {
                TextNode.Text = PickAThing(pilot);

                if (pilot.PhotoPath != null)
                {
                    filename = pilot.PhotoPath;
                }
            }

            string justFilename = System.Text.RegularExpressions.Regex.Replace(filename, @"[^\w\-. \\:]", "");

            PilotPhoto.SetFilename(justFilename);
            RequestLayout();
        }

        private string PickAThing(Pilot pilot)
        {
            List<string> things = new List<string>();
            if (!string.IsNullOrEmpty(pilot.Aircraft)) things.Add("Aircraft: " + pilot.Aircraft);
            if (!string.IsNullOrEmpty(pilot.CatchPhrase)) things.Add("Catch phrase: " + pilot.CatchPhrase);
            if (!string.IsNullOrEmpty(pilot.BestResult)) things.Add("Best result: " + pilot.BestResult);

            if (things.Any())
            {
                return things.Random();
            }

            return "";
        }
    }
}
