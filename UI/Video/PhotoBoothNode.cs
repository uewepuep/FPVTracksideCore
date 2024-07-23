using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using RaceLib;
using Sound;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading;
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
        private SoundManager soundManager;

        private AspectNode cameraAspectNode;
        private CamNode camNode;
        private ChannelPilotNameNode pilotNameNode;

        private CameraCountdownNode countDown;

        private Node buttonContainer;

        public ICaptureFrameSource CaptureFrameSource { get; private set; }

        public DirectoryInfo PilotsDirectory { get; private set; }

        public event Action<Pilot> OnNewPhoto;

        public TimeSpan Timeout { get; private set; }

        public PhotoBoothNode(VideoManager videoManager, EventManager eventManager, SoundManager soundManager) 
        {
            this.videoManager = videoManager;
            this.eventManager = eventManager;
            this.soundManager = soundManager;

            PilotsDirectory = new DirectoryInfo("pilots/");
            Timeout = TimeSpan.FromSeconds(10);
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

            buttonContainer = new Node();
            buttonContainer.RelativeBounds = new RectangleF(0.3f, cameraContainer.RelativeBounds.Bottom, 0.4f, 1 - cameraContainer.RelativeBounds.Bottom);
            AddChild(buttonContainer);

            TextButtonNode takePhoto = new TextButtonNode("Take Photo", Theme.Current.Editor.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA);
            takePhoto.OnClick += TakePhoto_OnClick;
            buttonContainer.AddChild(takePhoto);


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
                if (camNode.FrameNode.Source is ICaptureFrameSource)
                {
                    CaptureFrameSource = camNode.FrameNode.Source as ICaptureFrameSource;

                    TextButtonNode recordClip = new TextButtonNode("Record Clip", Theme.Current.Editor.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA);
                    recordClip.OnClick += RecordClip_OnClick;
                    buttonContainer.AddChild(recordClip);
                }

                countDown = new CameraCountdownNode();
                camNode.AddChild(countDown);

                cameraAspectNode.AddChild(camNode);
            }

            float pilotAlpha = Theme.Current.PilotViewTheme.PilotTitleAlpha / 255.0f;

            pilotNameNode = new ChannelPilotNameNode(null, Color.Transparent, pilotAlpha);
            cameraAspectNode.AddChild(pilotNameNode);
            pilotNameNode.RelativeBounds = new RectangleF(0, 0.03f, 0.4f, 0.125f);

            AlignHorizontally(0.1f, buttonContainer.Children);
            RequestLayout();
        }

        private void RecordClip_OnClick(Composition.Input.MouseInputEvent mie)
        {
            countDown.StartRecording(StartRecording, StopRecording);
        }

        private void TakePhoto_OnClick(Composition.Input.MouseInputEvent mie)
        {
            if (countDown != null)
            {
                countDown.TakePhoto(TakePhoto);
            }
        }

        private void TakePhoto()
        {
            soundManager.PlaySound(SoundKey.PhotoboothTrigger);

            string newPath = Path.Combine(PilotsDirectory.FullName, Pilot.Name + "_temp.png");
            newPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), newPath);
            camNode.FrameNode.SaveImage(newPath);

            if (File.Exists(newPath))
            {
                ConfirmPictureNode confirmPictureNode = new ConfirmPictureNode(eventManager.EventId, Pilot, Pilot.PhotoPath, newPath);
                confirmPictureNode.OnUseNew += ConfirmPictureNode_OnUseNew;
                GetLayer<PopupLayer>().Popup(confirmPictureNode);
            }
        }

        private void ConfirmPictureNode_OnUseNew(Pilot obj)
        {
            OnNewPhoto?.Invoke(obj);
        }

        private void StartRecording()
        {
            soundManager.PlaySound(SoundKey.PhotoboothTrigger);

            string newPath = Path.Combine(PilotsDirectory.FullName, Pilot.Name + "_temp");
            CaptureFrameSource.ManualRecording = true;
            CaptureFrameSource.StartRecording(newPath);

            if (!Waiter.WaitFor(() => { return CaptureFrameSource.FrameTimes.Any(); }, Timeout))
            {
                return;
            }
        }

        private void StopRecording()
        {
            soundManager.PlaySound(SoundKey.PhotoboothTrigger);

            string filename = CaptureFrameSource.Filename;
            CaptureFrameSource.StopRecording();
            CaptureFrameSource.ManualRecording = false;

            if (!Waiter.WaitFor(() => { return !CaptureFrameSource.Finalising; }, Timeout))
            {
                return;
            }

            if (File.Exists(filename))
            {
                ConfirmPictureNode confirmPictureNode = new ConfirmPictureNode(eventManager.EventId, Pilot, Pilot.PhotoPath, filename);
                confirmPictureNode.OnUseNew += ConfirmPictureNode_OnUseNew;
                GetLayer<PopupLayer>().Popup(confirmPictureNode);
            }
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

                        CamNode camNode;
                        if (ApplicationProfileSettings.Instance.PilotProfileChromaKey)
                        {
                            camNode = new ChromaKeyCamNode(source, videoBounds, ApplicationProfileSettings.Instance.PilotProfileChromaKeyColor, ApplicationProfileSettings.Instance.PilotProfileChromaKeyLimit);
                        }
                        else
                        {
                            camNode = new CamNode(source, videoBounds);
                        }

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
            if (buttonContainer != null)
            {
                buttonContainer.Visible = true;
            }

            RequestLayout();
        }

        public void Load()
        {
            Init();

            if (pilotNameNode != null)
            {
                pilotNameNode.Tint = Color.Red;
                pilotNameNode.SetText("Select a pilot on the left");
            }

            buttonContainer.Visible = false;

            if (Pilot != null)
            {
                SetPilot(Pilot);
            }
        }

        public void Clean()
        {
            ClearDisposeChildren();

            cameraAspectNode = null;
            camNode = null;
            pilotNameNode = null;
        }

        public override bool OnDrop(MouseInputEvent finalInputEvent, Node node)
        {
            IPilot pl = node as IPilot;
            if (pl != null)
            {
                SetPilot(pl.Pilot);
                return true;
            }

            return base.OnDrop(finalInputEvent, node);
        }
    }

    public class CameraCountdownNode : Node, IUpdateableNode
    {
        private Action action;
        private Action stop;
        private DateTime when;
        private TextNode countdownText;
        private TextNode recordingText;
        public TimeSpan Delay { get; set; }
        public TimeSpan RecordingLength { get; set; }

        private AlphaAnimatedNode flash;

        private bool showFlash;

        public CameraCountdownNode() 
        {
            Delay = TimeSpan.FromSeconds(3);
            RecordingLength = TimeSpan.FromSeconds(ApplicationProfileSettings.Instance.PilotProfilePhotoboothVideoLengthSeconds);

            countdownText = new TextNode("", Color.White);
            countdownText.Scale(0.3f);

            AddChild(countdownText);

            recordingText = new TextNode("REC", Color.Red);
            recordingText.RelativeBounds = new RectangleF(0, 0.9f, 1, 0.1f);
            recordingText.Alignment = RectangleAlignment.BottomRight;
            recordingText.Visible = false;
            AddChild(recordingText);

            flash = new AlphaAnimatedNode();
            flash.Alpha = 0;
            AddChild(flash);

            flash.AddChild(new ColorNode(Color.White));
        }

        public void Update(GameTime gameTime)
        {
            if (action != null || stop != null)
            {
                TimeSpan remaining = when - DateTime.Now;
                if (remaining > TimeSpan.Zero && remaining < TimeSpan.FromMinutes(1))
                {
                    countdownText.Text = ((int)Math.Ceiling(remaining.TotalSeconds)).ToString();
                }
                else
                {
                    if (showFlash)
                    {
                        flash.Alpha = 1;
                        flash.SetAnimatedAlpha(0);
                        action();
                        action = null;

                        // When is from the actual start.
                        when = DateTime.Now + Delay;
                    }

                    //Start Recording
                    if (action != null && stop != null)
                    {
                        Action cached = action;
                        action = null;
                        cached();

                        recordingText.Visible = true;
                        when = DateTime.Now + RecordingLength;
                        countdownText.Tint = Color.Red;
                    }
                    //Stop Recording
                    else if (action == null && stop != null)
                    {
                        Action cached = stop;
                        stop = null;

                        cached();
                        recordingText.Visible = false;
                    }

                    countdownText.Text = "";
                }
            }
        }

        public void TakePhoto(Action action)
        {
            countdownText.Tint = Color.White;

            when = DateTime.Now + Delay;
            this.action = action;

            showFlash = true;
        }

        public void StartRecording(Action start, Action stop)
        {
            countdownText.Tint = Color.White;

            when = DateTime.Now + Delay;
            action = start;
            this.stop = stop;

            showFlash = false;
        }
    }

    public class ConfirmPictureNode : BorderPanelNode
    {
        private FileInfo existingPhoto;
        private FileInfo newPhoto;

        private Pilot pilot;
        private Guid eventId;

        public event Action<Pilot> OnUseNew;

        public ConfirmPictureNode(Guid eventId, Pilot pilot, string existingFilename, string newFilename)
        {
            this.eventId = eventId;
            this.pilot = pilot; 

            if (!string.IsNullOrEmpty(existingFilename))
            {
                existingPhoto = new FileInfo(existingFilename);
            }
            newPhoto = new FileInfo(newFilename);

            Scale(0.8f, 0.6f);
            Node photoContainer = new Node();
            photoContainer.Scale(0.9f);
            AddChild(photoContainer);   

            if (newPhoto.Exists)
            {
                photoContainer.AddChild(CreateChoice(existingPhoto, "Old Photo", "Keep old photo", KeepOld));
                photoContainer.AddChild(CreateChoice(newPhoto, "New Photo", "Use new photo", UseNew));

                AlignHorizontally(0.1f, photoContainer.Children);
                RequestLayout();
            }
            else
            {
                Dispose();
            }
        }

        private void KeepOld()
        {
            Dispose();
        }

        private void UseNew()
        {
            try
            {
                existingPhoto?.Delete();

                FileInfo newFileName = new FileInfo(newPhoto.FullName.Replace("_temp", ""));
                if (newFileName.Exists)
                {
                    newFileName.Delete();
                }

                newPhoto.MoveTo(newFileName.FullName);

                pilot.PhotoPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), newFileName.FullName);
                using (IDatabase db = DatabaseFactory.Open(eventId))
                {
                    db.Upsert(pilot);
                }

                OnUseNew?.Invoke(pilot);
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

            PilotProfileNode profileNode = new PilotProfileNode(Color.Transparent, 1);
            profileNode.RelativeBounds = new RectangleF(0, 0, 1, 0.9f);

            if (file != null)
            {
                profileNode.SetPilot(pilot, file.FullName, false);
            }
            container.AddChild(profileNode);

            TextNode textNode = new TextNode(name, Color.White);
            textNode.RelativeBounds = new RectangleF(0, 0, 1, 0.1f);
            profileNode.AddChild(textNode);

            TextButtonNode textButtonNode = new TextButtonNode(question, Theme.Current.Editor.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA);
            textButtonNode.RelativeBounds = new RectangleF(0, profileNode.RelativeBounds.Bottom, 1, 1 - profileNode.RelativeBounds.Bottom);
            container.AddChild(textButtonNode);
            textButtonNode.OnClick += m => { action(); };

            return container;
        }
    }
}
