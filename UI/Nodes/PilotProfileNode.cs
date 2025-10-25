﻿using Composition;
using Composition.Input;
using Composition.Nodes;
using ImageServer;
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

        public void SetPilot(Pilot pilot, string filename = "", bool showText = true)
        {
            Pilot = pilot;

            insideOutBorderRelativeNode.ClearDisposeChildren();

            if (pilot != null)
            {
                if (showText)
                    TextNode.Text = PickAThing(pilot);

                if (string.IsNullOrEmpty(filename) && !string.IsNullOrEmpty(pilot.PhotoPath))
                {
                    filename = pilot.PhotoPath;
                }

                if (string.IsNullOrEmpty(filename))
                {
                    HasProfileImage = false;
                    return;
                }

                HasProfileImage = LoadFile(filename);
            }
            else
            {
                HasProfileImage = false;
                PilotPhoto?.Dispose();
                PilotPhoto = null;
                TextNode.Text = "";
            }

            RequestLayout();
        }

        private bool LoadFile(string filename)
        {
            try
            {
                PilotPhoto?.Dispose();
                PilotPhoto = null;

                string repaired = System.Text.RegularExpressions.Regex.Replace(filename, @"[^\w\-. \/\\:]", "");

                // On macOS: if path is relative, make it absolute using the base directory
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                {
                    if (!Path.IsPathRooted(repaired))
                    {
                        repaired = Path.Combine(IOTools.GetBaseDirectory().FullName, repaired);
                    }
                }

                FileInfo fileInfo = new FileInfo(repaired);
                if (fileInfo.Exists)
                {

                    string[] videoFileTypes = new[] { ".mp4", ".wmv", ".mkv" };
                    string[] imageFileTypes = new[] { ".png", ".jpg", ".jpeg" };

                    if (videoFileTypes.Contains(fileInfo.Extension))
                    {
                        CachedTextureFrameSource source = null;

                        FileFrameNode videoPlayer;
                        if (ApplicationProfileSettings.Instance.PilotProfileChromaKey)
                        {
                            source = new ChromaKeyCachedTextureFrameSource(CompositorLayer.GraphicsDevice, VideoFrameWorks.GetFramework(FrameWork.MediaFoundation), filename, ApplicationProfileSettings.Instance.PilotProfileChromaKeyColor, ApplicationProfileSettings.Instance.PilotProfileChromaKeyLimit);
                        }
                        else
                        {
                            source = new CachedTextureFrameSource(CompositorLayer.GraphicsDevice, VideoFrameWorks.GetFramework(FrameWork.MediaFoundation), filename);
                        }

                        source.BounceRepeat = ApplicationProfileSettings.Instance.PilotProfileBoomerangRepeat;

                        videoPlayer = new FileFrameNode(source);

                        videoPlayer.Repeat = true;
                        videoPlayer.Play();

                        PilotPhoto = videoPlayer;
                        insideOutBorderRelativeNode.AddChild(PilotPhoto, 0);
                    }
                    else if (imageFileTypes.Contains(fileInfo.Extension))
                    {
                        PilotPhoto = new ImageNode(fileInfo.FullName);
                        insideOutBorderRelativeNode.AddChild(PilotPhoto, 0);
                    }

                    if (PilotPhoto != null)
                    {
                        PilotPhoto.Alignment = RectangleAlignment.Center;
                        PilotPhoto.KeepAspectRatio = false;
                        PilotPhoto.CropToFit = true;
                        PilotPhoto.Scale(0.95f);
                        PilotPhoto.ReloadFromFile = true;

                        if (string.IsNullOrEmpty(TextNode.Text))
                        {
                            PilotPhoto.RelativeBounds = new RectangleF(0, 0, 1, 1);
                        }
                        else
                        {
                            TextNode.RelativeBounds = new RectangleF(0, 0.9f, 1, 0.1f);
                            PilotPhoto.RelativeBounds = new RectangleF(0, 0, 1, TextNode.RelativeBounds.Y);
                        }
                        return true;
                    }
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex) 
            {
                PilotPhoto?.Dispose();
                PilotPhoto = null;

                Logger.VideoLog.LogException(this, ex);
            }
            return false;
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

        internal void Reload()
        {
            if (PilotPhoto != null)
            {
                PilotPhoto.ReloadFromFile = true;
            }
        }

        public void Seek(TimeSpan time)
        {
            FileFrameNode videoPlayer = PilotPhoto as FileFrameNode;
            if (videoPlayer != null)
            {
                videoPlayer.Seek(time);
            }
        }

        public override bool Contains(Point point)
        {
            if (!Visible) 
                return false;

            if (PilotPhoto == null) 
                return false;

            return PilotPhoto.Contains(point);
        }
    }
}
