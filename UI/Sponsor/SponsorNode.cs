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
        public SponsorMedia SponsorMedia { get; private set; }

        public SponsorNode(SoundManager soundManager, SponsorMedia media)
        {
            SponsorMedia = media;

            RelativeBounds = new RectangleF(0.05f, 0.05f, 0.9f, 0.9f);

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
                    patreonNode.Scale(0.7f, 1);

                    BorderPanelNode borderPanelNode = new BorderPanelNode();
                    borderPanelNode.Scale(1, 0.4f);
                    borderPanelNode.AddChild(patreonNode);

                    AddChild(borderPanelNode);
                    break;
            }

            if (!string.IsNullOrEmpty(media.Text))
            {
                soundManager.SponsorRead(media.Text, TimeSpan.FromSeconds(SponsorMedia.DurationSeconds));
            }
        }
    }
}
