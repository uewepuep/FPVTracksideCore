using Composition;
using Composition.Layers;
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

        private CameraCountdownNode countDown;

        public PhotoBoothNode(VideoManager videoManager, EventManager eventManager) 
        {
            this.videoManager = videoManager;
            this.eventManager = eventManager;
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

            //TextButtonNode recordClip = new TextButtonNode("Record Clip", Theme.Current.Editor.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA);
            //recordClip.OnClick += RecordClip_OnClick;
            //buttonContainer.AddChild(recordClip);

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
                countDown = new CameraCountdownNode();
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
            countDown.Take(RecordClip);
        }

        private void TakePhoto_OnClick(Composition.Input.MouseInputEvent mie)
        {
            countDown.Take(TakePhoto);
        }

        private void TakePhoto()
        {
            if (string.IsNullOrEmpty(Pilot.PhotoPath))
            {
                Pilot.PhotoPath = "pilots/" + Pilot.Name + ".jpg";

                using (IDatabase db = DatabaseFactory.Open(eventManager.EventId))
                {
                    db.Update(Pilot);
                }
            }

            string newPath = "pilots/" + Pilot.Name + "_temp.jpg";
            camNode.FrameNode.SaveImage(newPath);

            ConfirmPictureNode confirmPictureNode = new ConfirmPictureNode(Pilot.PhotoPath, newPath);
            GetLayer<PopupLayer>().Popup(confirmPictureNode);
        }

        private void RecordClip()
        {
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
            

            base.Draw(id, parentAlpha);
        }
    }

    public class CameraCountdownNode : Node, IUpdateableNode
    {
        private Action action;
        private DateTime when;
        private TextNode text;
        public TimeSpan Delay { get; set; }

        private AlphaAnimatedNode flash;

        public CameraCountdownNode() 
        {
            Delay = TimeSpan.FromSeconds(3);

            text = new TextNode("", Color.White);
            text.Scale(0.3f);

            AddChild(text);

            flash = new AlphaAnimatedNode();
            flash.Alpha = 0;
            AddChild(flash);

            flash.AddChild(new ColorNode(Color.White));
        }

        public void Update(GameTime gameTime)
        {
            if (action != null)
            {
                TimeSpan remaining = when - DateTime.Now;
                if (remaining > TimeSpan.Zero && remaining < Delay)
                {
                    text.Text = ((int)Math.Ceiling(remaining.TotalSeconds)).ToString();
                }
                else
                {
                    flash.Alpha = 1;
                    flash.SetAnimatedAlpha(0);
                    action();
                    text.Text = "";
                    action = null;
                }
            }
        }

        public void Take(Action action)
        {
            when = DateTime.Now + Delay;
            this.action = action;
        }
    }

    public class ConfirmPictureNode : BorderPanelNode
    {
        private FileInfo existingPhoto;
        private FileInfo newPhoto;

        public ConfirmPictureNode(string existingFilename, string newFilename)
        {
            existingPhoto = new FileInfo(existingFilename);
            newPhoto = new FileInfo(newFilename);

            Scale(0.8f, 0.6f);
            Node photoContainer = new Node();
            photoContainer.Scale(0.9f);
            AddChild(photoContainer);   
            if (existingPhoto.Exists)
            {
                photoContainer.AddChild(CreateChoice(existingPhoto, "Old Photo", "Keep old photo", KeepOld));
            }

            if (newPhoto.Exists)
            {
                photoContainer.AddChild(CreateChoice(newPhoto, "New Photo", "Use new photo", UseNew));
            }

            AlignHorizontally(0.1f, photoContainer.Children);
        }

        private void KeepOld()
        {
            Dispose();
        }

        private void UseNew()
        {
            try
            {
                existingPhoto.Delete();
                newPhoto.MoveTo(existingPhoto.FullName);
            }
            catch (Exception ex) 
            { 
                GetLayer<PopupLayer>().PopupMessage(ex.Message);
            }
            
            Dispose();
        }

        private Node CreateChoice(FileInfo file, string name, string question, Action action)
        {
            Node container = new Node();

            ImageNode imageNode = new ImageNode(file.FullName);
            imageNode.ReloadFromFile = true;
            imageNode.RelativeBounds = new RectangleF(0, 0, 1, 0.9f);
            container.AddChild(imageNode);

            TextNode textNode = new TextNode(name, Color.White);
            textNode.RelativeBounds = new RectangleF(0, 0, 1, 0.1f);
            imageNode.AddChild(textNode);

            TextButtonNode textButtonNode = new TextButtonNode(question, Theme.Current.Editor.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA);
            textButtonNode.RelativeBounds = new RectangleF(0, imageNode.RelativeBounds.Bottom, 1, 1 - imageNode.RelativeBounds.Bottom);
            container.AddChild(textButtonNode);
            textButtonNode.OnClick += m => { action(); };

            return container;
        }
    }
}
