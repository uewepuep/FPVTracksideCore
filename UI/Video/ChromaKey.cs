using Composition;
using ImageServer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Video
{
    public class ChromaKeyFileFrameNode : FileFrameNode
    {
        private Texture2D replacementTexture;

        private Color[] data;

        public bool Enabled { get; set; }

        private ChromaKeyColor chromaKeyColor;
        private byte chromaKeyLimit;

        public ChromaKeyFileFrameNode(string filename, ChromaKeyColor chromaKeyColor, byte chromaKeyLimit)
            : base(filename)
        {
            this.chromaKeyColor = chromaKeyColor;
            this.chromaKeyLimit = chromaKeyLimit;
            Enabled = true;
        }

        public override void PreProcess(Drawer id)
        {
            base.PreProcess(id);

            if (!Enabled)
                return;

            if (texture == replacementTexture)
                return;

            TextureHelper.ChromaKey(texture, ref data, ref replacementTexture, chromaKeyColor, chromaKeyLimit);

            texture = replacementTexture;
        }
    }

    public class ChromaKeyFrameNode : FrameNode
    {
        private Texture2D replacementTexture;

        private Color[] data;

        public bool Enabled { get; set; }

        private ChromaKeyColor chromaKeyColor;
        private byte chromaKeyLimit;

        public ChromaKeyFrameNode(FrameSource s, ChromaKeyColor chromaKeyColor, byte chromaKeyLimit)
            :base(s)
        {
            this.chromaKeyColor = chromaKeyColor;
            this.chromaKeyLimit = chromaKeyLimit;
            Enabled = true;
        }

        public override void PreProcess(Drawer id)
        {
            base.PreProcess(id);

            if (!Enabled)
                return;

            if (texture == replacementTexture)
                return;

            TextureHelper.ChromaKey(texture, ref data, ref replacementTexture, chromaKeyColor, chromaKeyLimit);

            texture = replacementTexture;
        }
    }

    public class ChromaKeyCamNode : CamNode
    {
        public ChromaKeyCamNode(FrameSource s, VideoBounds videoBounds, ChromaKeyColor chromaKeyColor, byte chromaKeyLimit) 
            : base(new ChromaKeyFrameNode(s, chromaKeyColor, chromaKeyLimit), videoBounds)
        {
        }
    }
}
