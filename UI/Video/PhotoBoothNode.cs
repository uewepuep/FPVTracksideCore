using Composition;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Tools;
using UI.Nodes;

namespace UI.Video
{
    public class PhotoBoothNode : Node
    {
        public Pilot Pilot { get; private set; }

        private VideoManager videoManager;
        private EventManager eventManager;

        private AspectNode cameraAspectNode;
        private CamNode camNode;
        private ChannelPilotNameNode pilotNameNode;


        private Action whatDo;
        private DateTime when;
        private TextNode countDown;

        public TimeSpan Delay { get; set; }

        public PhotoBoothNode(VideoManager videoManager, EventManager eventManager) 
        {
            this.videoManager = videoManager;
            this.eventManager = eventManager;
            Delay = TimeSpan.FromSeconds(3);
        }

        private void Init()
        {
            if (cameraAspectNode != null)
            {
                return;
            }
                        
            ClearDisposeChildren();


            Node cameraContainer = new Node();
            cameraContainer.RelativeBounds = new RectangleF(0, 0, 1, 0.95f);
            AddChild(cameraContainer);

            Node buttonContainer = new Node();
            buttonContainer.RelativeBounds = new RectangleF(0.3f, cameraContainer.RelativeBounds.Bottom, 0.4f, 1 - cameraContainer.RelativeBounds.Bottom);
            AddChild(buttonContainer);

            TextButtonNode takePhoto = new TextButtonNode("Take Photo", Theme.Current.Editor.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA);
            takePhoto.OnClick += TakePhoto_OnClick;
            buttonContainer.AddChild(takePhoto);

            TextButtonNode recordClip = new TextButtonNode("Record Clip", Theme.Current.Editor.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA);
            recordClip.OnClick += RecordClip_OnClick;
            buttonContainer.AddChild(recordClip);

            AlignHorizontally(0.1f, buttonContainer.Children);

            foreach (Node node in buttonContainer.Children)
            {
                node.Scale(0.9f);
            }

            cameraAspectNode = new AspectNode(4 / 3.0f);
            cameraContainer.AddChild(cameraAspectNode);


            camNode = CreateCamNode();
            if (camNode == null)
            {
                TextNode textNode = new TextNode("Please add a video source and assign to camera PhotoBooth in Video Settings", Theme.Current.TextMain.XNA);
                textNode.RelativeBounds = new RectangleF(0, 0.45f, 1, 0.03f);
                cameraAspectNode.AddChild(textNode);
            }
            else
            {
                countDown = new TextNode("", Color.White);
                countDown.Scale(0.3f);
                camNode.AddChild(countDown);

                cameraAspectNode.AddChild(camNode);
            }

            float pilotAlpha = Theme.Current.PilotViewTheme.PilotTitleAlpha / 255.0f;

            pilotNameNode = new ChannelPilotNameNode(null, Color.Transparent, pilotAlpha);
            cameraAspectNode.AddChild(pilotNameNode);
            pilotNameNode.RelativeBounds = new RectangleF(0, 0.03f, 0.4f, 0.125f);
            RequestLayout();
        }

        private void RecordClip_OnClick(Composition.Input.MouseInputEvent mie)
        {
            whatDo = RecordClip;
            when = DateTime.Now + Delay;
        }

        private void TakePhoto_OnClick(Composition.Input.MouseInputEvent mie)
        {
            whatDo = TakePhoto;
            when = DateTime.Now + Delay;
        }

        private void TakePhoto()
        {
            whatDo = null;

            string path = Pilot.PhotoPath;
            if (string.IsNullOrEmpty(path))
            {
                path = "pilots/" + Pilot.Name + ".jpg";
            }

            camNode.FrameNode.SaveImage(path);
            Pilot.PhotoPath = path;

            using (IDatabase db = DatabaseFactory.Open(eventManager.EventId))
            {
                db.Update(Pilot);
            }
        }

        private void RecordClip()
        {
            whatDo = null;
        }

        private CamNode CreateCamNode()
        {
            foreach (FrameSource source in videoManager.GetFrameSources())
            {
                foreach (VideoBounds videoBounds in source.VideoConfig.VideoBounds)
                {
                    if (videoBounds.SourceType == SourceTypes.PhotoBooth)
                    {
                        videoBounds.OverlayAlignment = OverlayAlignment.TopRight;
                        CamNode camNode = new CamNode(source, videoBounds);
                        camNode.FrameNode.CropToFit = true;
                        camNode.FrameNode.KeepAspectRatio = false;
                        return camNode;
                    }
                }
            }
            return null;
        }

        public void SetPilot(Pilot pilot)
        {
            Pilot = pilot;

            Color color = eventManager.GetPilotColor(pilot);

            if (pilotNameNode != null)
            {
                pilotNameNode.Tint = color;
                pilotNameNode.SetPilot(pilot);
            }

            RequestLayout();
        }

        public void Load()
        {
            Init();

            foreach (Pilot p in eventManager.Event.Pilots)
            {
                if (!File.Exists(p.PhotoPath))
                {
                    SetPilot(p);
                    break;
                }
            }

            Pilot pa = eventManager.Event.Pilots.FirstOrDefault();
            SetPilot(pa);
        }

        public void Clean()
        {
            ClearDisposeChildren();

            cameraAspectNode = null;
            camNode = null;
            pilotNameNode = null;
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (countDown != null && whatDo != null)
            {
                TimeSpan remaining = when - DateTime.Now;
                if (remaining > TimeSpan.Zero && remaining < Delay) 
                { 
                    countDown.Text = ((int)Math.Ceiling(remaining.TotalSeconds)).ToString();
                }
                else
                {
                    whatDo();
                    countDown.Text = "";
                }
            }

            base.Draw(id, parentAlpha);
        }
    }
}
