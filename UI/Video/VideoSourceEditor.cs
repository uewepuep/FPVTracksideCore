﻿using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RaceLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Tools;
using UI.Nodes;
using UI.Nodes.Rounds;

namespace UI.Video
{
    public class VideoSourceEditor : ObjectEditorNode<VideoConfig>
    {
        public VideoManager VideoManager { get; private set; }
        public EventManager EventManager { get; private set; }

        protected Node preview;

        public override bool GroupItems { get { return true; } }

        private ChannelVideoMapperNode mapperNode;
        private object locker;

        private Node physicalLayoutContainer;
        private Node physicalLayout;

        public Profile Profile { get; private set; }
        
        private bool wasEmpty = false;    

        public static VideoSourceEditor GetVideoSourceEditor(EventManager em, Profile profile)
        {
            VideoManager videoManager = new VideoManager(ApplicationProfileSettings.Instance.EventStorageLocation, profile);

            videoManager.LoadDevices();
            videoManager.MaintainConnections = true;
            videoManager.AutoPause = false;

            return new VideoSourceEditor(videoManager, em, profile);
        }

        private VideoSourceEditor(VideoManager videoManager, EventManager em, Profile profile)
        {
            locker = new object();
            Profile = profile;

            VideoManager = videoManager;
            VideoManager.OnStart += VideoManager_OnStart;
            EventManager = em;

            heading.Text = "Video Input Settings";
            cancelButton.Visible = true;
            trackChanges = true;
            CanReOrder = false;

            SetObjects(videoManager.VideoConfigs, true);

            InitMapperNode(Selected);

            RelativeBounds = new RectangleF(0, 0, 1, 0.97f);
            Scale(0.6f, 1.0f);
        }

        private void VideoManager_OnStart(FrameSource obj)
        {
            if (Selected == obj.VideoConfig)
            {
                InitMapperNode(obj.VideoConfig);
            }
        }

        public override void Dispose()
        {
            if (VideoManager != null)
            {
                VideoManager.Dispose();
                VideoManager = null;
            }

            base.Dispose();
        }

        protected override void AddNew(VideoConfig videoConfig)
        {
            // When adding a new camera, always detect and set optimal video mode
            // This ensures we use supported resolutions/framerates instead of defaults
            Tools.Logger.VideoLog.LogCall(this, $"Auto-detecting optimal mode for newly added camera: '{videoConfig.DeviceName}' (current: {videoConfig.VideoMode.Width}x{videoConfig.VideoMode.Height}@{videoConfig.VideoMode.FrameRate}fps)");
            
            var optimalMode = VideoManager.DetectOptimalMode(videoConfig);
            if (optimalMode != null)
            {
                videoConfig.VideoMode = optimalMode;
                Tools.Logger.VideoLog.LogCall(this, $"✓ Set optimal mode for '{videoConfig.DeviceName}': {optimalMode.Width}x{optimalMode.Height}@{optimalMode.FrameRate}fps");
            }
            else
            {
                // Fallback to safe defaults (but still better than 25fps default)
                videoConfig.VideoMode.Width = 640;
                videoConfig.VideoMode.Height = 480;
                videoConfig.VideoMode.FrameRate = 30;
                videoConfig.VideoMode.Format = "";
                videoConfig.VideoMode.FrameWork = videoConfig.FrameWork;
                videoConfig.VideoMode.Index = 0; // Set a valid index
                Tools.Logger.VideoLog.LogCall(this, $"⚠ Using fallback mode for '{videoConfig.DeviceName}': 640x480@30fps");
            }
            
            // Add to the VideoManager's collection first to ensure persistence
            if (VideoManager != null && !VideoManager.VideoConfigs.Contains(videoConfig))
            {
                Tools.Logger.VideoLog.LogCall(this, $"Adding camera to VideoManager.VideoConfigs: '{videoConfig.DeviceName}'");
                VideoManager.VideoConfigs.Add(videoConfig);
            }
            
            base.AddNew(videoConfig);
            
            if (wasEmpty)
            {
                // Force property editor rebuild since ClearSelected corrupted it
                DoSetSelected(videoConfig);
                wasEmpty = false;
            }
            RepairVideoPreview();
        }

        protected override void AddOnClick(MouseInputEvent mie)
        {
            MouseMenu mouseMenu = new MouseMenu(this);
            mouseMenu.TopToBottom = false;

            VideoConfig[] vcs = VideoManager.GetAvailableVideoSources().OrderBy(vc => vc.DeviceName).ToArray();
            foreach (VideoConfig source in vcs)
            {
                if (!Objects.Any(r => r.Equals(source)))
                {
                    string sourceAsString = source.ToString();
                    if (!string.IsNullOrWhiteSpace(sourceAsString))
                    {
                        if (VideoManager.ValidDevice(source))
                        {
                            mouseMenu.AddItem(sourceAsString, () => { AddNew(source); });
                        }
                        else
                        {
                            mouseMenu.AddDisabledItem(sourceAsString);
                        }
                    }
                }
            }

            mouseMenu.AddItem("File", AddVideoFile);
            //mouseMenu.AddItem("RTSP URL", AddURL);
            mouseMenu.Show(addButton);
        }

        private void AddVideoFile()
        {
            string filename = PlatformTools.OpenFileDialog("Open Video / Image", "Video or Image files|*.wmv;*.mp4;*.mkv;*.jpg");
            if (!string.IsNullOrWhiteSpace(filename))
            {
                VideoConfig vs = new VideoConfig();

                System.IO.FileInfo fi = new System.IO.FileInfo(filename);
                vs.DeviceName = fi.Name;
                vs.FilePath = fi.FullName;
                AddNew(vs);
            }
        }

        private void AddURL()
        {
            TextPopupNode tn = new TextPopupNode("RTSP Stream", "URL", "");
            tn.OnOK += (url) =>
            {
                Uri uri = new Uri(url);
                VideoConfig vs = new VideoConfig();
                vs.DeviceName = uri.Host;
                vs.URL = uri.AbsoluteUri;
                AddNew(vs);
            };
            GetLayer<PopupLayer>().Popup(tn);
        }

        protected override PropertyNode<VideoConfig> CreatePropertyNode(VideoConfig obj, PropertyInfo pi)
        {
            if (pi.Name == "VideoMode")
            {
                return new ModePropertyNode(this, obj, pi, ButtonBackground, TextColor, ButtonHover);
            }

            if (pi.Name == "AnyUSBPort")
            {
                if (obj.DeviceName == "OBS-Camera")
                {
                    return null;
                }
            }

            if (pi.Name == "AudioDevice")
            {
                return null;
            }

            if (pi.Name == "Channels")
            {
                return new VideoChannelAssigner(obj, pi, TextColor);
            }

            if (pi.Name == "RecordResolution")
            {
                int[] resolutions = new int[] { 240, 360, 480, 720, 1080, 2160 };
                ListPropertyNode<VideoConfig> listPropertyNode = new ListPropertyNode<VideoConfig>(obj, pi, ButtonBackground, TextColor, ButtonHover, resolutions);
                return listPropertyNode;
            }

            if (pi.Name == "RecordFrameRate")
            {
                int[] framerates = new int[] { 15, 25, 30, 50, 60, 120, 160 };
                ListPropertyNode<VideoConfig> listPropertyNode = new ListPropertyNode<VideoConfig>(obj, pi, ButtonBackground, TextColor, ButtonHover, framerates);
                return listPropertyNode;
            }

            if (pi.Name == "NeedsGMFBridge")
            {
                if (obj.NeedsGMFBridge)
                {
                    GMFBridgePropertyNode buttonPropertyNode = new GMFBridgePropertyNode(PlatformTools, obj, pi, ButtonBackground, TextColor, ButtonHover);
                    return buttonPropertyNode;
                }
                return null;
            }

            if (pi.Name == "Splits")
            {
                return new SplitsPropertyNode(obj, pi, ButtonBackground, TextColor, ButtonHover);
            }

            // Only show Hardware Decode Acceleration for compressed video formats
            if (pi.Name == "HardwareDecodeAcceleration")
            {
                if (!obj.IsCompressedVideoFormat)
                {
                    return null; // Don't show for uncompressed formats
                }
            }

            PropertyNode<VideoConfig> propertyNode = base.CreatePropertyNode(obj, pi);
            CheckVisible(propertyNode, obj);

            return propertyNode;
        }

        public override void SetObjects(IEnumerable<VideoConfig> toEdit, bool addRemove = false, bool cancelButton = true)
        {
            if (preview == null)
            {
                preview = new ColorNode(Theme.Current.Editor.Foreground.XNA);
                right.AddChild(preview);
            }

            if (physicalLayoutContainer == null)
            {
                physicalLayoutContainer = new AspectNode(16 / 9.0f);
                physicalLayoutContainer.Visible = false;
                ColorNode background = new ColorNode(Theme.Current.Editor.Foreground.XNA);
                physicalLayoutContainer.AddChild(background);

                right.AddChild(physicalLayoutContainer);

                physicalLayout = new ColorNode(Theme.Current.Editor.Text.XNA);
                physicalLayout.Scale(0.3f);
                physicalLayoutContainer.AddChild(physicalLayout);
            }


            base.SetObjects(toEdit, addRemove, cancelButton);

            // Set preview at the top of the right panel
            preview.RelativeBounds = new RectangleF(0, 0, 1, 0.46f);

            // Move objectProperties down to make room for preview
            objectProperties.RelativeBounds = new RectangleF(0, preview.RelativeBounds.Bottom, 1, buttonContainer.RelativeBounds.Y - preview.RelativeBounds.Bottom);

            // Position physicalLayoutContainer to the right of the preview
            physicalLayoutContainer.RelativeBounds = new RectangleF(1.1f, preview.RelativeBounds.Y, 0.3f, preview.RelativeBounds.Height);
        }

        protected override void DoSetSelected(VideoConfig obj)
        {
            base.DoSetSelected(obj);

            if (obj != null)
            {
                preview.Visible = true;
            }
            RepairVideoPreview();
        }

        public override void ClearSelected()
        {
            base.ClearSelected();
            InitMapperNode(null);
            preview.Visible = false;
        }

        private void InitMapperNode(VideoConfig videoConfig)
        {
            lock (locker)
            {
                // Ensure complete cleanup before creating new mapper node
                Tools.Logger.VideoLog.LogCall(this, $"InitMapperNode: Cleaning up for '{videoConfig?.DeviceName ?? "null"}'");
                
                // Dispose existing mapper node
                if (mapperNode != null)
                {
                    mapperNode.OnChange -= MapperNode_OnChange;
                    mapperNode.Dispose();
                    mapperNode = null;
                }
                
                // Clear preview children to prevent node disposal issues
                if (preview != null)
                {
                    preview.ClearDisposeChildren();
                }

                if (videoConfig == null)
                {
                    Tools.Logger.VideoLog.LogCall(this, "InitMapperNode: videoConfig is null, cleanup complete");
                    return;
                }

                if (VideoManager != null)
                {
                    try
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"InitMapperNode: Creating new mapper for '{videoConfig.DeviceName}'");
                        mapperNode = new ChannelVideoMapperNode(Profile, VideoManager, EventManager, videoConfig, Objects);
                        mapperNode.OnChange += MapperNode_OnChange;
                        preview.AddChild(mapperNode);

                        RequestLayout();
                        Tools.Logger.VideoLog.LogCall(this, $"InitMapperNode: Successfully created mapper for '{videoConfig.DeviceName}'");
                    }
                    catch (Exception ex)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"InitMapperNode: Error creating mapper for '{videoConfig.DeviceName}': {ex.Message}");
                        mapperNode?.Dispose();
                        mapperNode = null;
                    }
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, "InitMapperNode: VideoManager is null");
                }
            }
        }

        private void MapperNode_OnChange()
        {
            SplitsPropertyNode spn = PropertyNodes.OfType<SplitsPropertyNode>().FirstOrDefault();
            if (spn != null)
            {
                spn.UpdateFromObject();
            }
        }

        private void CheckVisible(PropertyNode<VideoConfig> propertyNode, VideoConfig obj)
        {
            if (propertyNode == null)
                return;

            string name = propertyNode.PropertyInfo.Name;
            if (name.Contains("Splits") && name != "Splits" && obj.Splits != Splits.Custom)
            {
                propertyNode.Visible = false;
            }
        }

        private void RepairVideoPreview()
        {
            if (VideoManager != null && Selected != null)
            {
                Tools.Logger.VideoLog.LogCall(this, $"RepairVideoPreview called for '{Selected.DeviceName}' - creating frame source and ensuring it starts");
                
                try
                {
                    VideoManager.CreateFrameSource(new VideoConfig[] { Selected }, (fs) =>
                    {
                        try
                        {
                            if (mapperNode != null)
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"RepairVideoPreview: Calling MakeTable for '{Selected.DeviceName}'");
                                mapperNode.MakeTable();
                            }
                            else
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"RepairVideoPreview: mapperNode is null for '{Selected.DeviceName}'");
                            }
                        }
                        catch (Exception ex)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"RepairVideoPreview: Error in MakeTable for '{Selected.DeviceName}': {ex.Message}");
                        }
                    });

                    InitMapperNode(Selected);
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"RepairVideoPreview: Error creating frame source for '{Selected.DeviceName}': {ex.Message}");
                }
                
                // Give the frame source a moment to be created, then force another initialization
                // This helps ensure the camera actually starts when re-added
                System.Threading.Timer initTimer = null;
                initTimer = new System.Threading.Timer((state) =>
                {
                    try
                    {
                        var videoConfig = state as VideoConfig;
                        if (videoConfig != null)
                        {
                            var frameSource = VideoManager.GetFrameSource(videoConfig);
                            if (frameSource != null && !frameSource.Connected)
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"Follow-up initialization for '{videoConfig.DeviceName}' - camera not connected yet");
                                VideoManager.Initialize(frameSource);
                            }
                            else if (frameSource != null)
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"Camera '{videoConfig.DeviceName}' already connected - no follow-up needed");
                            }
                            else
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"No frame source found for '{videoConfig.DeviceName}' during follow-up");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Tools.Logger.VideoLog.LogException(this, ex);
                    }
                    finally
                    {
                        initTimer?.Dispose();
                    }
                }, Selected, TimeSpan.FromMilliseconds(500), System.Threading.Timeout.InfiniteTimeSpan);
            }
        }

        protected override void Remove(MouseInputEvent mie)
        {
            VideoConfig videoConfig = Selected;
            if (videoConfig != null)
            {
                GetLayer<PopupLayer>()?.PopupConfirmation("Remove camera '" + videoConfig.DeviceName + "'?", () =>
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Removing camera '{videoConfig.DeviceName}' and rebuilding UI");
                    
                    // Remove from Objects list (replaces base.Remove call)
                    Objects.Remove(videoConfig);

                    // Remove from VideoManager's frame sources
                    VideoManager.RemoveFrameSource(videoConfig);
                    
                    // Remove from VideoManager's VideoConfigs list
                    VideoManager.VideoConfigs.Remove(videoConfig);
                    
                    // Persist the change to disk
                    VideoManager.WriteCurrentDeviceConfig();
                    
                    Tools.Logger.VideoLog.LogCall(this, $"Camera '{videoConfig.DeviceName}' removed from VideoConfigs and changes persisted");
                    
                    // Refresh the UI list (same as base.Remove does)
                    RefreshList();
                    
                    // Handle selection like base.Remove does
                    if (Objects.Any())
                    {
                        SetSelected(Objects.FirstOrDefault());
                    }
                    else
                    {
                        // INVESTIGATION: Add debugging around ClearSelected to see what it does
                        Tools.Logger.VideoLog.LogCall(this, "No cameras remaining - investigating ClearSelected behavior");
                        Tools.Logger.VideoLog.LogCall(this, $"Before ClearSelected - objectProperties children: {objectProperties?.Children?.Count() ?? 0}");
                        
                        ClearSelected();
                        
                        Tools.Logger.VideoLog.LogCall(this, $"After ClearSelected - objectProperties children: {objectProperties?.Children?.Count() ?? 0}");
                        Tools.Logger.VideoLog.LogCall(this, "No cameras remaining - clearing preview");
                        preview.Visible = false;
                        wasEmpty = true; // Track that we became empty
                    }
                });
            }
        }

        protected override void ChildValueChanged(Change newChange)
        {
            foreach (var propertyNode in PropertyNodes)
            {
                CheckVisible(propertyNode, Selected);
            }

            if (newChange.PropertyInfo.Name == "VideoMode" || newChange.PropertyInfo.Name == "Flipped" || newChange.PropertyInfo.Name == "RecordVideoForReplays" || newChange.PropertyInfo.Name == "DeviceName")
            {
                RepairVideoPreview();
                
                // If DeviceName changed, also refresh available video modes for the new camera
                if (newChange.PropertyInfo.Name == "DeviceName")
                {
                    RefreshVideoModes();
                }
            }

            if (newChange.PropertyInfo.Name == "Splits" && Selected != null)
            {
                Selected.VideoBounds = mapperNode.CreateChannelBounds(Selected).ToArray();
            }

            InitMapperNode(Selected);

            base.ChildValueChanged(newChange);
        }

        private void RefreshVideoModes()
        {
            if (Selected != null)
            {
                // Find the ModePropertyNode in the property editor and refresh its modes
                var modePropertyNode = FindPropertyNode<ModePropertyNode>("VideoMode");
                if (modePropertyNode != null)
                {
                    modePropertyNode.RefreshModes();
                }
            }
        }

        private T FindPropertyNode<T>(string propertyName) where T : class
        {
            // Search through the property editor nodes to find the specific property node
            return objectProperties.Children.OfType<T>().FirstOrDefault();
        }

        public void RefreshPropertyEditor()
        {
            if (Selected != null)
            {
                // Force rebuild of property editor by re-setting the selected object
                var currentSelected = Selected;
                DoSetSelected(currentSelected);
            }
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            bool physicalVisible = false;

            if (preview.Contains(mouseInputEvent.Position) && mapperNode != null)
            {
                if (mapperNode.ChannelVideoInfos.Count() > 9)
                {
                    ChannelVideoMapNode match = mapperNode.ChannelVideoMapNodes.FirstOrDefault(r => r.Contains(mouseInputEvent.Position));
                    if (match != null)
                    {
                        physicalLayout.RelativeBounds = match.ChannelVideoInfo.ScaledRelativeSourceBounds;
                        physicalLayout.RequestLayout();
                        physicalVisible = true;
                    }
                }
            }

            physicalLayoutContainer.Visible = physicalVisible;


            return base.OnMouseInput(mouseInputEvent);
        }

        private class AudioDevicePropertyNode : ListPropertyNode<VideoConfig>
        {
            private VideoManager vm;

            public AudioDevicePropertyNode(VideoManager vm, VideoConfig obj, PropertyInfo pi, Color textBackground, Color textColor, Color hover) 
                : base(obj, pi, textBackground, textColor, hover, null)
            {
                this.vm = vm;
            }

            public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
            {
                if (mouseInputEvent.Button == MouseButtons.Left && mouseInputEvent.ButtonState == ButtonStates.Released)
                {
                    List<string> audioDevices = new List<string>();
                    audioDevices.Add("None");
                    audioDevices.AddRange(vm.GetAvailableAudioSources());
                    SetOptions(audioDevices);
                }

                return base.OnMouseInput(mouseInputEvent);
            }
        }

        private class ModePropertyNode : ListPropertyNode<VideoConfig>
        {
            private VideoSourceEditor vse;

            private Mode[] modes;

            private bool rebootRequired;

            public ModePropertyNode(VideoSourceEditor vse, VideoConfig obj, PropertyInfo pi, Color background, Color textColor, Color hoverColor)
                : base(obj, pi, background, textColor, hoverColor)
            {
                this.vse = vse;
                modes = new Mode[0];

                rebootRequired = false;
            }

            public void AcceptModes(VideoManager.ModesResult result)
            {
                modes = TrimModes(result.Modes).ToArray();
                if (result.RebootRequired)
                {
                    GetLayer<PopupLayer>().PopupMessage("Please reboot capture device: " + Object.DeviceName);
                    rebootRequired = true;
                }
                else
                {
                    rebootRequired = false;
                }

                SetOptions(modes);
                ShowMouseMenu();
            }

            public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
            {
                if (mouseInputEvent.Button == MouseButtons.Left && mouseInputEvent.ButtonState == ButtonStates.Released)
                {
                    bool forceAllModes = Keyboard.GetState().IsKeyDown(Keys.LeftControl) || Keyboard.GetState().IsKeyDown(Keys.RightControl);
                    if (forceAllModes)
                    {
                        // Get ALL the modes form the device
                        vse.VideoManager.GetModes(Object, true, AcceptModes);
                    }
                    else if (rebootRequired || !modes.Any() || true) // TEMP: Force refresh for new format parsing
                    {
                        // Get the normal modes form the device
                        vse.VideoManager.GetModes(Object, false, AcceptModes);
                    }
                    else
                    {
                        // just use the cached ones..
                        SetOptions(modes);
                        return base.OnMouseInput(mouseInputEvent);
                    }

                    return true;
                }
                return base.OnMouseInput(mouseInputEvent);
            }

            private void SetOptions(Mode[] ms)
            {
                // Group by format first, then sort within each group by resolution and framerate
                IEnumerable<Mode> ordered = ms.OrderByDescending(m => m.FrameWork)
                                                     .ThenBy(m => GetFormatPriority(m.Format))  // Group by format priority
                                                     .ThenByDescending(m => m.Width * m.Height) // Then by resolution
                                                     .ThenByDescending(m => m.FrameRate)        // Then by framerate
                                                     .ThenBy(m => m.Format);                    // Finally by format name for consistency

                if (ms.Any())
                {
                    Options = ordered.OfType<object>().ToList();
                }
            }

            private int GetFormatPriority(string format)
            {
                // Lower numbers = higher priority (shown first)
                switch (format?.ToLower())
                {
                    case "h264": return 0;      // Highest priority
                    case "mjpeg": return 1;     // High priority  
                    case "uyvy422": return 2;   // Medium priority
                    case "yuyv422": return 3;   // Lower priority
                    default: return 9;          // Lowest priority for unknown formats
                }
            }

            public void RefreshModes()
            {
                // Force refresh the video modes for the current camera
                vse.VideoManager.GetModes(Object, false, AcceptModes);
                Tools.Logger.VideoLog.LogCall(this, $"Refreshing video modes for camera: '{Object.DeviceName}'");
            }

            private IEnumerable<Mode> TrimModes(IEnumerable<Mode> modes)
            {
                bool allItems = Keyboard.GetState().IsKeyDown(Keys.LeftControl);
                if (!allItems)
                {
                    // Filter out very low framerates, but be more permissive for different formats
                    // Keep modes that are >= 5fps or have different formats (mjpeg, h264, etc.)
                    modes = modes.Where(m => m.FrameRate >= 5 || m.Format == "mjpeg" || m.Format == "h264");
                }

                var grouped = modes.GroupBy(m => new Tuple<FrameWork, int, int, float, string>(m.FrameWork, m.Width, m.Height, m.FrameRate, m.Format)).OrderByDescending(t => t.Key.Item1);

                foreach (var group in grouped)
                {
                    if (allItems)
                    {
                        foreach (var g in group)
                        {
                            if (g != null)
                            {
                                yield return g;
                            }
                        }
                    }
                    else
                    {
                        var mode = vse.VideoManager.PickMode(Object, group);
                        if (mode != null)
                        {
                            yield return mode;
                        }
                    }
                }
            }

            

            protected override void SetValue(object value)
            {
                bool wasCompressed = Object.IsCompressedVideoFormat;
                base.SetValue(value);
                bool isCompressed = Object.IsCompressedVideoFormat;
                
                // If compressed format status changed, refresh the property editor to show/hide hardware acceleration option
                if (wasCompressed != isCompressed)
                {
                    vse.RefreshPropertyEditor();
                }
            }

            public override string ValueToString(object value)
            {
                Mode mode = value as Mode;
                if (mode == null) return "";

                return mode.ToString();
            }
        }

        class VideoChannelAssigner : NamedPropertyNode<VideoConfig>
        {
            public VideoChannelAssigner(VideoConfig obj, PropertyInfo pi, Color textColor) : base(obj, pi, textColor)
            {
                ColorNode cn = new ColorNode(Color.Gray);
                AddChild(cn);
            }

            public override void Layout(RectangleF parentBounds)
            {
                parentBounds.Height *= 4;
                base.Layout(parentBounds);
            }
        }
    }

    public class ChannelVideoMapperNode : Node
    {
        private Node main;

        private TableNode table;

        private VideoManager videoManager;

        public ChannelVideoInfo[] ChannelVideoInfos { get; private set; }

        public List<ChannelVideoMapNode> ChannelVideoMapNodes { get; private set; }

        private VideoConfig videoConfig;
        private VideoConfig[] others;

        private Channel[] eventChannels;

        public event Action OnChange;

        public ChannelVideoMapperNode(Profile profile, VideoManager videoManager, EventManager eventManager, VideoConfig videoConfig, IEnumerable<VideoConfig> others)
        {
            ChannelVideoMapNodes = new List<ChannelVideoMapNode>();

            this.others = others.ToArray();
            this.videoManager = videoManager;
            this.videoConfig = videoConfig;

            if (eventManager != null && eventManager.Channels != null)
            {
                eventChannels = eventManager.Channels;
            }
            else
            {
                eventChannels = Channel.Read(profile);
            }

            // Always order by frequency.
            eventChannels = eventChannels.OrderBy(r => r.Frequency).ToArray();

            main = new Node();
            AddChild(main);

            table = new TableNode(1, 1);
            table.RelativeBounds = new RectangleF(0, 0, 1, 0.96f);
            main.AddChild(table);

            TextNode channelInstructions = new TextNode("Click on the video feed to change channel / camera assignments / edit settings", Theme.Current.Editor.Text.XNA);
            channelInstructions.RelativeBounds = new RectangleF(0, table.RelativeBounds.Bottom, 1, 1 - table.RelativeBounds.Bottom);
            main.AddChild(channelInstructions);

            CreateChannelVideoInfos();
            MakeTable();
        }

        private void CreateChannelVideoInfos()
        {
            var allChannelVideoInfos = videoManager.CreateChannelVideoInfos(others);
            Tools.Logger.VideoLog.LogCall(this, $"VideoManager returned {allChannelVideoInfos?.Count() ?? 0} ChannelVideoInfos");
            
            ChannelVideoInfos = allChannelVideoInfos.Where(cc => cc.FrameSource.VideoConfig == videoConfig).ToArray();
            Tools.Logger.VideoLog.LogCall(this, $"After filtering by VideoConfig ({videoConfig.DeviceName}), got {ChannelVideoInfos.Length} ChannelVideoInfos");

            foreach (ChannelVideoInfo cvi in ChannelVideoInfos)
            {
                Channel channel = cvi.Channel;

                if ((channel == null || channel == Channel.None) && cvi.VideoBounds.SourceType == SourceTypes.FPVFeed)
                {
                    IEnumerable<Channel> otherDevicesInUse = others.SelectMany(vs => vs.VideoBounds).Select(vb => vb.GetChannel()).Where(ca => ca != null);
                    IEnumerable<Channel> thisDeviceInUse = ChannelVideoInfos.Select(c => c.Channel).Where(ca => ca != null);

                    IEnumerable<Channel> inUse = otherDevicesInUse.Concat(thisDeviceInUse).Distinct();
                    Channel next = eventChannels.Where(d => !inUse.Contains(d)).FirstOrDefault();
                    if (next != null)
                    {
                        channel = next;
                    }
                    else
                    {
                        channel = Channel.None;
                    }
                    cvi.Channel = channel;
                    cvi.VideoBounds.Channel = channel.ToStringShort();
                }
            }
        }

        public void MakeTable()
        {
            Tools.Logger.VideoLog.LogCall(this, $"MakeTable called with {ChannelVideoInfos?.Length ?? 0} ChannelVideoInfos");
            
            lock (table)
            {
                if (table != null)
                {
                    table.ClearDisposeChildren();
                }
                lock (ChannelVideoMapNodes)
                {
                    ChannelVideoMapNodes.Clear();
                }

                int columns = (int)Math.Ceiling(Math.Sqrt(ChannelVideoInfos.Length));
                int rows = (int)Math.Ceiling(ChannelVideoInfos.Length / (float)columns);
                
                Tools.Logger.VideoLog.LogCall(this, $"Table layout: {rows}x{columns} for {ChannelVideoInfos.Length} items");
                table.SetSize(rows, columns);

                Color transparentForeground = new Color(Theme.Current.Editor.Foreground.XNA, 0.5f);

                int count = Math.Min(ChannelVideoInfos.Length, table.CellCount);
                for (int i = 0; i < count; i++)
                {
                    ChannelVideoInfo channelVideoInfo = ChannelVideoInfos[i];
                    Node cell = table.GetCell(i);

                    Tools.Logger.VideoLog.LogCall(this, $"Creating video nodes: cell={cell != null}, frameSource={channelVideoInfo?.FrameSource?.GetType()?.Name ?? "null"} (Instance: {channelVideoInfo?.FrameSource?.GetHashCode()})");
                    
                    if (cell != null && channelVideoInfo.FrameSource != null)
                    {
                        ChannelVideoMapNode cvmn = new ChannelVideoMapNode(channelVideoInfo);
                        cvmn.OnClick += (m, c) => { ShowChangeMenu(m, c.ChannelVideoInfo); };
                        cell.AddChild(cvmn);

                        lock (ChannelVideoMapNodes)
                        {
                            ChannelVideoMapNodes.Add(cvmn);
                        }
                    }
                }
                RequestLayout();
            }

            OnChange?.Invoke();
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            base.Draw(id, parentAlpha);

            lock (ChannelVideoMapNodes)
            {
                foreach (ChannelVideoMapNode fn in ChannelVideoMapNodes)
                {
                    fn.FrameNode.NeedsAspectRatioUpdate = true;
                }
            }
        }

        private void ShowChangeMenu(MouseInputEvent mie, ChannelVideoInfo channelVideoInfo)
        {
            MouseMenu mouseMenu = new MouseMenu(this);
            mouseMenu.TopToBottom = true;

            if (channelVideoInfo.VideoBounds.SourceType != SourceTypes.FPVFeed)
            {
                mouseMenu.AddItem("Edit Cam Settings", () =>
                {
                    VideoBoundsEditor editor = new VideoBoundsEditor(channelVideoInfo.VideoBounds);
                    GetLayer<PopupLayer>().Popup(editor);
                });
                mouseMenu.AddBlank();
            }

            MouseMenu channelMenu = mouseMenu.AddSubmenu("Channel Assigment");

            channelMenu.AddItem("No Channel", () => { AssignChannel(channelVideoInfo, Channel.None); });


            if (eventChannels != null)
            {
                channelMenu.AddItem("Auto-Assign", () => { LinearChannelAssignment(eventChannels, false); });
                channelMenu.AddItem("Auto-Assign (Share frequencies)", () => { LinearChannelAssignment(eventChannels, true); });
                channelMenu.AddSubmenu("Event Channels", (c) => { AssignChannel(channelVideoInfo, c); }, eventChannels);
            }

            foreach (Band band in Channel.GetBands())
            {
                IEnumerable<Channel> cs = Channel.AllChannels.Where(c => c.Band == band).OrderBy(c => c.Number);
                channelMenu.AddSubmenu(band.ToString(), (c) => { AssignChannel(channelVideoInfo, c); }, cs.ToArray());
            }

            MouseMenu cameraMenu = mouseMenu.AddSubmenu("Camera Assignment");

            foreach (SourceTypes sourceType in Enum.GetValues(typeof(SourceTypes)))
            {
                if (sourceType == SourceTypes.FPVFeed)
                    continue;

                cameraMenu.AddItem(sourceType.ToString() +  " Camera", () =>
                {
                    SetSourceType(channelVideoInfo, sourceType);
                });
            }
            
            mouseMenu.AddBlank();

            MouseMenu splitMenu = mouseMenu.AddSubmenu("Split");
            foreach (Splits split in Enum.GetValues(typeof(Splits)))
            {
                if (split == Splits.Custom || split == Splits.SingleChannel)
                    continue;

                splitMenu.AddItem("Split " + split.ToHumanString(), () =>
                {
                    Split(channelVideoInfo, split);
                });
            }

            MouseMenu cropMenu = mouseMenu.AddSubmenu("Crop");
            cropMenu.AddItem("Crop 4:3", () => { Crop(channelVideoInfo, 4, 3); });
            cropMenu.AddItem("Crop 16:9", () => { Crop(channelVideoInfo, 16, 9); });


            mouseMenu.AddItem("Duplicate", () => { Duplicate(channelVideoInfo, Channel.None); });
            mouseMenu.AddItem("Duplicate Source", () => { DuplicateSource(); });

            if (eventChannels != null)
            {
                Channel c = channelVideoInfo.Channel;

                Channel channel = eventChannels.GetOthersInChannelGroup(c).FirstOrDefault();
                if (channel != null)
                {
                    mouseMenu.AddItem("Duplicate to " + channel.UIDisplayName, () => { Duplicate(channelVideoInfo, channel); });
                }
            }

            mouseMenu.AddBlank();
            mouseMenu.AddItem("Remove", () => { RemoveView(channelVideoInfo); });
            mouseMenu.AddItem("Reset All", Reset);

            mouseMenu.Show(mie);

        }

        public void LinearChannelAssignment(IEnumerable<Channel> channels, bool shareChannelGroups)
        {
            if (channels != null)
            {
                if (shareChannelGroups)
                {
                    for (int j = 0; j < ChannelVideoInfos.Length; j++)
                    {
                        ChannelVideoInfos[j].Channel = Channel.None;
                    }


                    int i = 0;
                    foreach(Channel[] grouped in channels.GetChannelGroups())
                    {
                        if (i >= ChannelVideoInfos.Length)
                            break;

                        ChannelVideoInfo cvi = ChannelVideoInfos[i];
                        Channel first = grouped.FirstOrDefault();
                        AssignChannel(cvi, grouped.FirstOrDefault());

                        foreach (Channel channel in grouped.Where(r => r != first))
                        {
                            Duplicate(cvi, channel);
                        }
                        i++;
                    }

                    MakeTable();
                }
                else
                {
                    Channel[] ordered = channels.OrderBy(c => c.Band.GetBandType()).ThenBy(c => c.Frequency).ToArray();

                    int max = Math.Min(ChannelVideoInfos.Length, ordered.Length);
                    for (int i = 0; i < max; i++)
                    {
                        AssignChannel(ChannelVideoInfos[i], ordered[i]);
                    }
                }
            }
        }

        private void Crop(ChannelVideoInfo channelVideoInfo, int width, int height)
        {
            float scale = height / (float)width;

            RectangleF f = channelVideoInfo.VideoBounds.RelativeSourceBounds;
            channelVideoInfo.VideoBounds.RelativeSourceBounds = f.Scale(scale, 1);

            CreateChannelVideoInfos();
            MakeTable();
        }

        private void RemoveView(ChannelVideoInfo channelVideoInfo)
        {
            videoConfig.Splits = Splits.Custom;

            List<VideoBounds> videoBoundsList = videoConfig.VideoBounds.ToList();

            videoConfig.VideoBounds = videoBoundsList.Where(r => r != channelVideoInfo.VideoBounds).ToArray();

            CreateChannelVideoInfos();
            MakeTable();
        }

        private void Duplicate(ChannelVideoInfo channelVideoInfo, Channel channel)
        {
            videoConfig.Splits = Splits.Custom;

            VideoBounds clone = channelVideoInfo.VideoBounds.Clone();
            clone.Channel = channel.ToStringShort();

            List<VideoBounds> videoBoundsList = videoConfig.VideoBounds.ToList();

            int index = videoBoundsList.IndexOf(channelVideoInfo.VideoBounds);
            if (index >= 0)
            {
                videoBoundsList.Insert(index + 1, clone);
            }

            videoConfig.VideoBounds = videoBoundsList.ToArray();

            CreateChannelVideoInfos();
            MakeTable();
        }

        private void DuplicateSource()
        {
            videoConfig.Splits = Splits.Custom;

            List<VideoBounds> videoBoundsList = videoConfig.VideoBounds.ToList();

            VideoBounds source = new VideoBounds();
            source.RelativeSourceBounds = new RectangleF(0, 0, 1, 1);
            videoBoundsList.Add(source);

            videoConfig.VideoBounds = videoBoundsList.ToArray();

            CreateChannelVideoInfos();
            MakeTable();
        }

        private void Reset()
        {
            videoConfig.VideoBounds = CreateChannelBounds(Splits.SingleChannel, new RectangleF(0, 0, 1, 1)).ToArray();
            CreateChannelVideoInfos();
            MakeTable();
        }

        private void Split(ChannelVideoInfo channelVideoInfo, Splits split)
        {
            bool firstSplit = channelVideoInfo.VideoBounds.RelativeSourceBounds.Width == 1 && channelVideoInfo.VideoBounds.RelativeSourceBounds.Height == 1;

            videoConfig.Splits = Splits.Custom;

            List<VideoBounds> cbs = new List<VideoBounds>();

            foreach (VideoBounds vb in videoConfig.VideoBounds)
            {
                if (vb == channelVideoInfo.VideoBounds)
                {
                    var newBoudns = CreateChannelBounds(split, channelVideoInfo.VideoBounds.RelativeSourceBounds);
                    cbs.AddRange(newBoudns);
                }
                else
                {
                    cbs.Add(vb);
                }
            }

            videoConfig.VideoBounds = cbs.ToArray();

            CreateChannelVideoInfos();

            if (firstSplit)
            {
                LinearChannelAssignment(eventChannels, false);
            }

            MakeTable();
        }

        private void SetSourceType(ChannelVideoInfo channelVideoInfo, SourceTypes sourceType)
        {
            channelVideoInfo.VideoBounds.SourceType = sourceType;

            if (string.IsNullOrEmpty(channelVideoInfo.VideoBounds.OverlayText))
            {
                channelVideoInfo.VideoBounds.OverlayText = sourceType.ToString();
            }

            if (sourceType != SourceTypes.FPVFeed)
            {
                channelVideoInfo.Channel = Channel.None;
                channelVideoInfo.VideoBounds.Channel = channelVideoInfo.Channel.ToStringShort();

                VideoBoundsEditor editor = new VideoBoundsEditor(channelVideoInfo.VideoBounds);
                GetLayer<PopupLayer>().Popup(editor);
            }

            RemoveDuplicateChannels();
            MakeTable();
        }

        private void RemoveChannel(Channel c)
        {
            if (c == Channel.None)
                return;

            foreach (VideoBounds vb in videoConfig.VideoBounds)
            {
                if (vb.Channel == c.ToStringShort())
                {
                    vb.Channel = Channel.None.ToStringShort();
                }
            }
        }

        private void AssignChannel(ChannelVideoInfo channelVideoInfo, Channel c)
        {
            SetSourceType(channelVideoInfo, SourceTypes.FPVFeed);

            RemoveChannel(c);

            channelVideoInfo.Channel = c;
            channelVideoInfo.VideoBounds.Channel = c.ToStringShort();

            MakeTable();
        }

        private void RemoveDuplicateChannels()
        {
            List<string> channelIds = new List<string>();
            foreach (VideoBounds vb in videoConfig.VideoBounds)
            {
                if (vb.Channel == Channel.None.ToStringShort())
                    continue;

                if (channelIds.Contains(vb.Channel))
                {
                    vb.Channel = Channel.None.ToStringShort();
                }
                else
                {
                    channelIds.Add(vb.Channel);
                }
            }
        }

        public IEnumerable<VideoBounds> CreateChannelBounds(VideoConfig videoConfig)
        {
            return CreateChannelBounds(videoConfig.Splits, RectangleF.Centered(1, 1));
        }

        public IEnumerable<VideoBounds> CreateChannelBounds(Splits splits, RectangleF bounds)
        {
            int horz, vertz;
            splits.GetSplits(out horz, out vertz);
            return CreateChannelBounds(horz, vertz, bounds);
        }

        public IEnumerable<VideoBounds> CreateChannelBounds(int horizontalSplits, int verticalSplits, RectangleF bounds)
        {
            float widthF = horizontalSplits;
            float heightF = verticalSplits;

            for (int y = 0; y < verticalSplits; y++)
            {
                for (int x = 0; x < horizontalSplits; x++)
                {
                    RectangleF relativeBounds = new RectangleF(x / widthF, y / heightF, 1 / widthF, 1 / heightF);

                    RectangleF insideParent = new RectangleF();
                    insideParent.X = bounds.X + bounds.Width * relativeBounds.X;
                    insideParent.Y = bounds.Y + bounds.Height * relativeBounds.Y;
                    insideParent.Width = bounds.Width * relativeBounds.Width;
                    insideParent.Height = bounds.Height * relativeBounds.Height;

                    VideoBounds cvi = new VideoBounds() { Channel = Channel.None.ToStringShort(), RelativeSourceBounds = insideParent };
                    yield return cvi;
                }
            }
        }
    }

    public class ChannelVideoMapNode : Node
    {
        public event Action<MouseInputEvent, ChannelVideoMapNode> OnClick;

        public FrameNode FrameNode { get; private set; }
        public ChannelVideoInfo ChannelVideoInfo { get; private set; }

        public HoverNode HoverNode { get; private set; }

        public ChannelVideoMapNode(ChannelVideoInfo channelVideoInfo)
        {
            ChannelVideoInfo = channelVideoInfo;
            
            Tools.Logger.VideoLog.LogCall(this, $"ChannelVideoMapNode constructor called with FrameSource: {channelVideoInfo?.FrameSource?.GetType()?.Name ?? "null"} (Instance: {channelVideoInfo?.FrameSource?.GetHashCode()})");

            SetData();
        }

        private void SetData()
        {
            Tools.Logger.VideoLog.LogCall(this, $"SetData called for ChannelVideoMapNode");
            ClearDisposeChildren();

            if (ChannelVideoInfo?.FrameSource == null)
            {
                Tools.Logger.VideoLog.LogCall(this, $"SetData: ChannelVideoInfo.FrameSource is null, skipping FrameNode creation");
                return;
            }

            VideoConfig videoConfig = ChannelVideoInfo.FrameSource.VideoConfig;

            Tools.Logger.VideoLog.LogCall(this, $"VideoSourceEditor creating FrameNode with source: {ChannelVideoInfo.FrameSource.GetType().Name} (Instance: {ChannelVideoInfo.FrameSource.GetHashCode()})");
            FrameNode = new FrameNode(ChannelVideoInfo.FrameSource);
            FrameNode.RelativeSourceBounds = ChannelVideoInfo.ScaledRelativeSourceBounds;
            FrameNode.KeepAspectRatio = true;
            AddChild(FrameNode);

            HoverNode = new HoverNode(Theme.Current.Hover.XNA);
            FrameNode.AddChild(HoverNode);

            ButtonNode change = new ButtonNode();
            AddChild(change);
            change.OnClick += (m) =>
            {
                OnClick(m, this);
            };

            AbsoluteHeightNode absoluteHeightNode = new AbsoluteHeightNode(30);
            absoluteHeightNode.Alignment = RectangleAlignment.Center;
            FrameNode.AddChild(absoluteHeightNode);

            string text;
            switch (ChannelVideoInfo.VideoBounds.SourceType)
            {
                default:
                    text = ChannelVideoInfo.VideoBounds.SourceType.ToString().CamelCaseToHuman();
                    break;

                case SourceTypes.FPVFeed:
                    text = ChannelVideoInfo.Channel.ToStringShort();
                    break;
            }

            TextNode textNode = new TextNode(text, Theme.Current.Editor.Text.XNA);
            textNode.Style.Border = true;
            absoluteHeightNode.AddChild(textNode);
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.Button == MouseButtons.Left && mouseInputEvent.ButtonState == ButtonStates.Pressed)
            {
                GetLayer<DragLayer>()?.RegisterDrag(this, mouseInputEvent);
            }

            return base.OnMouseInput(mouseInputEvent);
        }

        public override Rectangle? CanDrop(MouseInputEvent finalInputEvent, Node node)
        {
            ChannelVideoMapNode other = node as ChannelVideoMapNode;
            if (other != null)
                return Bounds;

            return base.CanDrop(finalInputEvent, node);
        }

        public override bool OnDrop(MouseInputEvent finalInputEvent, Node node)
        {
            ChannelVideoMapNode other = node as ChannelVideoMapNode;
            if (other != null)
            {
                // Cache existing settings..
                Channel thisChannel = ChannelVideoInfo.Channel;
                string thisVBChannel = ChannelVideoInfo.VideoBounds.Channel;
                SourceTypes thisSourceType = ChannelVideoInfo.VideoBounds.SourceType;
                string thisOverlayText = ChannelVideoInfo.VideoBounds.OverlayText;
                OverlayAlignment thisOverlayAlignment = ChannelVideoInfo.VideoBounds.OverlayAlignment;
                bool thisShowInGrid = ChannelVideoInfo.VideoBounds.ShowInGrid;

                // Set the new ones
                ChannelVideoInfo.Channel = other.ChannelVideoInfo.Channel;
                ChannelVideoInfo.VideoBounds.Channel = other.ChannelVideoInfo.VideoBounds.Channel;
                ChannelVideoInfo.VideoBounds.SourceType = other.ChannelVideoInfo.VideoBounds.SourceType;
                ChannelVideoInfo.VideoBounds.OverlayText = other.ChannelVideoInfo.VideoBounds.OverlayText;
                ChannelVideoInfo.VideoBounds.OverlayAlignment = other.ChannelVideoInfo.VideoBounds.OverlayAlignment;
                ChannelVideoInfo.VideoBounds.ShowInGrid = other.ChannelVideoInfo.VideoBounds.ShowInGrid;

                // set the old ones..
                other.ChannelVideoInfo.Channel = thisChannel;
                other.ChannelVideoInfo.VideoBounds.Channel = thisVBChannel;
                other.ChannelVideoInfo.VideoBounds.SourceType = thisSourceType;
                other.ChannelVideoInfo.VideoBounds.OverlayText = thisOverlayText;
                other.ChannelVideoInfo.VideoBounds.OverlayAlignment = thisOverlayAlignment;
                other.ChannelVideoInfo.VideoBounds.ShowInGrid = thisShowInGrid;

                SetData();
                other.SetData();

                return true;
            }

            return base.OnDrop(finalInputEvent, node);
        }

    }

    public class VideoBoundsEditor : ObjectEditorNode<VideoBounds>
    {
        public VideoBoundsEditor(VideoBounds toEdit)
            : base(toEdit, false, true, false)
        {
            heading.Text = "Camera Display Editor";
            Scale(0.5f, 0.5f);
            SetButtonsHeight(0.1f);
        }
    }

    public class GMFBridgePropertyNode : ButtonPropertyNode<VideoConfig>
    {
        private PlatformTools platformTools1;


        public GMFBridgePropertyNode(PlatformTools platformTools, VideoConfig obj, PropertyInfo pi, Color backgroundColor, Color textColor, Color hoverColor)
            : base(obj, pi, backgroundColor, textColor, hoverColor, "Please click here, then run GMFBridge Installer & restart FPVTrackside", StartInstaller)
        {
            platformTools1 = platformTools;
            Visible = false;
            UpdateFromObject();
        }

        public static void StartInstaller()
        {
            try
            {
                string filename = Path.Combine(Directory.GetCurrentDirectory(), "GMFBridge.msi");
                string argument = "/i \"" + filename + "\"";
                System.Diagnostics.Process.Start("msiexec.exe", argument);
            }
            catch
            {

            }
        }

        public override void UpdateFromObject()
        {
            if (platformTools1 != null)
            {
                bool installed = platformTools1.Check("GMFBridge");

                bool vis = !installed;

                if (vis != Visible)
                {
                    Visible = vis;
                    RequestLayout();
                    RequestRedraw();
                }
            }

            base.UpdateFromObject();
        }
    }

    public class SplitsPropertyNode : EnumPropertyNode<VideoConfig>
    {
        public SplitsPropertyNode(VideoConfig obj, PropertyInfo pi, Color background, Color textColor, Color hover)
            : base(obj, pi, background, textColor, hover)
        {
        }

        public override string ValueToString(object value)
        {
            if (value is Splits)
            {
                Splits split = (Splits)value;
                return split.ToHumanString();
            }
            return base.ValueToString(value);
        }
    }
}
