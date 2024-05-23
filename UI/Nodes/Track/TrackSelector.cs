using Composition.Nodes;
using ExternalData;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Tools;

namespace UI.Nodes.Track
{
    public class TrackSelector : BorderPanelNode
    {
        private ListNode<TrackNameNode> tracksNode;

        private TextEditNode searcherNode;

        private HeadingNode titleNode;

        public event Action<RaceLib.Track> OnTrackSelected;

        private ITrackProvider trackProvider;

        public TrackSelector(ITrackProvider ttrackProvider) 
        {
            trackProvider = ttrackProvider;
            Scale(0.3f, 0.9f);

            titleNode = new HeadingNode(Theme.Current.InfoPanel, "Track Selector");
            titleNode.RelativeBounds = new RectangleF(0, 0, 1, 0.05f);
            AddChild(titleNode);

            Node container = new Node();
            container.RelativeBounds = new RectangleF(0.01f, titleNode.RelativeBounds.Bottom, 0.98f, 1 - titleNode.RelativeBounds.Bottom);
            AddChild(container);

            Node searchContainer = new Node();
            searchContainer.RelativeBounds = new RectangleF(0.1f, 0.0f, 0.8f, 0.04f);
            container.AddChild(searchContainer);

            TextNode search = new TextNode("Search: ", Theme.Current.InfoPanel.Text.XNA);
            search.Alignment = RectangleAlignment.CenterRight;
            search.RelativeBounds = new RectangleF(0, 0.1f, 0.29f, 0.8f);
            searchContainer.AddChild(search);

            ColorNode ss = new ColorNode(Theme.Current.InfoPanel.Foreground.XNA);
            ss.RelativeBounds = new RectangleF(0.30f, 0, 0.7f, 1);
            searchContainer.AddChild(ss);

            searcherNode = new TextEditNode("", Theme.Current.InfoPanel.Text.XNA);
            searcherNode.TextChanged += SearcherNode_TextChanged;
            searcherNode.HasFocus = true;
            ss.AddChild(searcherNode);

            tracksNode = new ListNode<TrackNameNode>(Theme.Current.ScrollBar.XNA);
            tracksNode.RelativeBounds = new RectangleF(0, searchContainer.RelativeBounds.Bottom, 1, 0.94f - searchContainer.RelativeBounds.Bottom);
            tracksNode.LayoutInvisibleItems = false;
            tracksNode.ItemHeight = 30;
            container.AddChild(tracksNode);

            TextButtonNode cancel = new TextButtonNode("Cancel", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
            cancel.RelativeBounds = new RectangleF(0, 0.94f, 0.2f, 0.05f);
            cancel.OnClick += (m) => { Remove(); };
            container.AddChild(cancel);

            if (trackProvider != null)
            {
                TextButtonNode download = new TextButtonNode("Download more", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
                download.RelativeBounds = new RectangleF(0.25f, 0.94f, 0.3f, 0.05f);
                download.OnClick += Download_OnClick;
                container.AddChild(download);
            }

            Load();
        }

        private void Download_OnClick(Composition.Input.MouseInputEvent mie)
        {
            LoadingLayer ll = CompositorLayer.LayerStack.GetLayer<LoadingLayer>();
            ll.WorkQueue.Enqueue("Downloading Tracks from FPVTrackside.com", () =>
            {
                RaceLib.Track[] onlineTracks = trackProvider.GetTracks().ToArray();
                RaceLib.Track[] offlineTracks;

                using (RaceLib.IDatabase db = RaceLib.DatabaseFactory.Open())
                {
                    offlineTracks = db.All<RaceLib.Track>().ToArray();
                }

                var merged = offlineTracks.Union(onlineTracks).Distinct();
                MakeItems(merged);
            });
        }

        private void SearcherNode_TextChanged(string search)
        {
            search = search.ToLower();

            foreach (TrackNameNode trackNode in tracksNode.ChildrenOfType)
            {
                trackNode.Visible = trackNode.Text.ToLower().Contains(search);
            }
            RequestLayout();
        }

        private void Load()
        {
            using (RaceLib.IDatabase db = RaceLib.DatabaseFactory.Open())
            {
                MakeItems(db.All<RaceLib.Track>());
            }
        }

        private void MakeItems(IEnumerable<RaceLib.Track> tracks)
        {
            tracksNode.ClearDisposeChildren();

            foreach (RaceLib.Track track in tracks.OrderBy(r => r.Name))
            {
                TrackNameNode ttn = new TrackNameNode(track);
                ttn.OnClick += (mie) => { SelectTrack(ttn.Track); };
                tracksNode.AddChild(ttn);
            }
            RequestLayout();
        }

        public void SelectTrack(RaceLib.Track track)
        {
            OnTrackSelected?.Invoke(track);
            Remove();
        }
    }

    public class TrackNameNode : TextButtonNode
    {
        public RaceLib.Track Track { get; private set; }
        public TrackNameNode(RaceLib.Track track) 
            : base(track.Name, Theme.Current.InfoPanel.Background.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA)
        {
            Track = track;  
            if (string.IsNullOrEmpty(Track.Name))
            {
                Text = "Track " + Track.ID.ToString();
            }
        }
    }
}
