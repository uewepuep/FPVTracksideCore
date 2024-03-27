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

            Node cameraContainer = new Node();
            cameraContainer.RelativeBounds = new RectangleF(0, 0, 1, 0.9f);
            AddChild(cameraContainer);

            Node buttonContainer = new Node();
            buttonContainer.RelativeBounds = new RectangleF(0, cameraContainer.RelativeBounds.Bottom, 1, 1 - cameraContainer.RelativeBounds.Bottom);
            AddChild(buttonContainer);

            cameraAspectNode = new AspectNode(4 / 3.0f);
            cameraContainer.AddChild(cameraAspectNode);

            camNode = CreateCamNode();
            cameraAspectNode.AddChild(camNode);

            float pilotAlpha = Theme.Current.PilotViewTheme.PilotTitleAlpha / 255.0f;

            pilotNameNode = new ChannelPilotNameNode(null, Color.Transparent, pilotAlpha);
            cameraAspectNode.AddChild(pilotNameNode);
            pilotNameNode.RelativeBounds = new RectangleF(0, 0.03f, 0.4f, 0.125f);
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

            pilotNameNode.Tint = color;
            pilotNameNode.SetPilot(pilot);

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
    }
}
