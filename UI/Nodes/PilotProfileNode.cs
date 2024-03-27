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
using UI.Video;

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
        private InsideOutBorderRelativeNode insideOutBorderRelativeNode;

        public bool RepeatVideo
        {
            get
            {
                FileFrameNode fileFrameNode = PilotPhoto as FileFrameNode;
                if (fileFrameNode != null)
                {
                    return fileFrameNode.Repeat;
                }
                return false;
            }
            set
            {
                FileFrameNode fileFrameNode = PilotPhoto as FileFrameNode;
                if (fileFrameNode != null)
                {
                    fileFrameNode.Repeat = value;
                }
            }
        }

        public PilotProfileNode(Color channelColour, float pilotAlpha)
        {
            Alpha = 0;
            HasProfileImage = false;

            ProfileImageContainer = new AnimatedRelativeNode();
            AddChild(ProfileImageContainer);

            Background = new AlphaAnimatedNode();
            ProfileImageContainer.AddChild(Background);

            insideOutBorderRelativeNode = new InsideOutBorderRelativeNode(new Color(channelColour, pilotAlpha));
            Background.AddChild(insideOutBorderRelativeNode);

            TextNode = new TextNode("", Theme.Current.PilotViewTheme.PilotOverlayText.XNA);
            TextNode.Style.Italic = true;
            TextNode.Style.Border = true;
            AddChild(TextNode);

            PilotPhoto = null;

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

                if (string.IsNullOrEmpty(filename))
                {
                    return;
                }

                FileInfo fileInfo = new FileInfo(System.Text.RegularExpressions.Regex.Replace(filename, @"[^\w\-. \\:]", ""));
                if (fileInfo.Exists)
                {
                    string[] videoFileTypes = new[] { ".mp4", ".wmv", ".mkv" };
                    string[] imageFileTypes = new[] { ".png", ".jpg" };

                    if (videoFileTypes.Contains(fileInfo.Extension))
                    {
                        FileFrameNode videoPlayer = new ChromaKeyFileFrameNode(fileInfo.FullName);
                        videoPlayer.Repeat = true;
                        videoPlayer.Play();

                        PilotPhoto = videoPlayer;
                    }

                    if (imageFileTypes.Contains(fileInfo.Extension))
                    {
                        PilotPhoto = new ImageNode(fileInfo.FullName);
                    }

                    PilotPhoto.Alignment = RectangleAlignment.Center;
                    PilotPhoto.KeepAspectRatio = false;
                    PilotPhoto.CropToFit = true;
                    PilotPhoto.Scale(0.95f);
                    insideOutBorderRelativeNode.AddChild(PilotPhoto, 0);

                    if (string.IsNullOrEmpty(TextNode.Text))
                    {
                        PilotPhoto.RelativeBounds = new RectangleF(0, 0, 1, 1);
                    }
                    else
                    {
                        TextNode.RelativeBounds = new RectangleF(0, 0.9f, 1, 0.1f);
                        PilotPhoto.RelativeBounds = new RectangleF(0, 0, 1, TextNode.RelativeBounds.Y);
                    }

                    HasProfileImage = true;
                }
                else
                {
                    HasProfileImage = false;
                }
            }
            else
            {
                PilotPhoto?.Dispose();
                PilotPhoto = null;
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
