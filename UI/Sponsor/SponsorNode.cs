using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sound;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using UI.Nodes;

namespace UI.Sponsor
{
    public class SponsorNode : Node
    {
        public DateTime End { get; private set; }

        public SponsorMedia SponsorMedia { get; private set; }

        private TextButtonNode button;


        public SponsorNode(SoundManager soundManager, SponsorMedia media)
        {
            TimeSpan duration = TimeSpan.FromSeconds(media.DurationSeconds);

            End = DateTime.Now + duration;

            SponsorMedia = media;

            RelativeBounds = new RectangleF(0.05f, 0.05f, 0.9f, 0.9f);
            
            button = new TextButtonNode("", Theme.Current.Button.XNA, Theme.Current.Hover.XNA, Theme.Current.TextMain.XNA);
            button.RelativeBounds = new RectangleF(0.89f, 0.94f, 0.1f, 0.05f);
            button.OnClick += OnClick;

            AddChild(button);

            switch (media.AdType)
            {
                case AdType.Video:
                    break;

                case AdType.Image:
                    ImageNode imageNode = new ImageNode(media.Filename);
                    AddChild(imageNode);
                    break;

                case AdType.Patreon:

                    PatreonNode patreonNode = new PatreonNode();
                    patreonNode.SetPatreon(media.Name, media.Since, media.Filename);
                    patreonNode.Scale(0.6f, 1);

                    BorderPanelNode borderPanelNode = new BorderPanelNode();
                    borderPanelNode.Scale(1, 0.4f);
                    borderPanelNode.AddChild(patreonNode);

                    AddChild(borderPanelNode);
                    break;
            }

            if (!string.IsNullOrEmpty(media.Text))
            {
                soundManager.SponsorRead(media.Text, duration);
            }
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            DateTime now = DateTime.Now;

            int remaining = (int)Math.Ceiling((End - now).TotalSeconds);

            button.Text = "Skip (" + remaining + ")"; 

            if (now > End)
            {
                Close();
            }

            base.Draw(id, parentAlpha);
        }

        public void Close()
        {
            CompositorLayer.Visible = false;
            this.Dispose();
        }

        private void OnClick(Composition.Input.MouseInputEvent mie)
        {
            Close();
        }
    }
}
