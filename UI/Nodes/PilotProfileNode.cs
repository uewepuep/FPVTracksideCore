using Composition;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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

        public bool HasProfileImage { get; private set; }

        public AnimatedRelativeNode ProfileImageContainer;

        public AlphaAnimatedNode Background { get; private set; }

        public PilotProfileNode(Color channelColour, float pilotAlpha)
        {
            Alpha = 0;
            HasProfileImage = false;

            ProfileImageContainer = new AnimatedRelativeNode();
            AddChild(ProfileImageContainer);

            Background = new AlphaAnimatedNode();
            ProfileImageContainer.AddChild(Background);

            TextNode = new TextNode("", Theme.Current.PilotViewTheme.PilotOverlayText.XNA);
            TextNode.Style.Italic = true;
            TextNode.Style.Border = true;
            AddChild(TextNode);

            PilotPhoto = new ImageNode();
                        
            PilotPhoto.Alignment = RectangleAlignment.Center;
            PilotPhoto.KeepAspectRatio = false;
            PilotPhoto.CropToFit = true;
            PilotPhoto.Scale(0.95f);

            Background.AddChild(PilotPhoto);

            TextNode.Scale(0.9f);

            SetAnimationTime(TimeSpan.FromSeconds(1));
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

                string justFilename = System.Text.RegularExpressions.Regex.Replace(filename, @"[^\w\-. \\:]", "");
                if (File.Exists(justFilename))
                {
                    PilotPhoto.SetFilename(justFilename);
                    HasProfileImage = true;
                }
                else
                {
                    HasProfileImage = false;
                }

                if (string.IsNullOrEmpty(TextNode.Text))
                {
                    PilotPhoto.RelativeBounds = new RectangleF(0, 0, 1, 1);
                }
                else
                {
                    TextNode.RelativeBounds = new RectangleF(0, 0.9f, 1, 0.1f);
                    PilotPhoto.RelativeBounds = new RectangleF(0, 0, 1, TextNode.RelativeBounds.Y);
                }
            }
            else
            {
                PilotPhoto.SetFilename(null);
                TextNode.Text = "";
            }

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


        public override void Snap()
        {
            ProfileImageContainer.Snap();   
            base.Snap();
        }
    }
}
