using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RaceLib;
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
        public RaceLib.Sponsor Sponsor { get; private set; }

        public SponsorNode(SoundManager soundManager, RaceLib.Sponsor sponsor)
        {
            Sponsor = sponsor;

            RelativeBounds = new RectangleF(0.05f, 0.05f, 0.9f, 0.9f);

            switch (sponsor.AdType)
            {
                case AdType.Video:
                    break;

                case AdType.Image:
                    ImageNode imageNode = new ImageNode(sponsor.Filename);
                    AddChild(imageNode);
                    break;

                case AdType.Patreon:

                    PatreonNode patreonNode = new PatreonNode();
                    patreonNode.SetPatreon(sponsor.Name, sponsor.Since, sponsor.Filename);
                    patreonNode.Scale(0.7f, 1);

                    BorderPanelNode borderPanelNode = new BorderPanelNode();
                    borderPanelNode.Scale(1, 0.4f);
                    borderPanelNode.AddChild(patreonNode);

                    AddChild(borderPanelNode);
                    break;
            }

            if (soundManager!= null && !string.IsNullOrEmpty(sponsor.Text))
            {
                soundManager.SponsorRead(sponsor.Text, TimeSpan.FromSeconds(sponsor.DurationSeconds));
            }
        }
    }
}
