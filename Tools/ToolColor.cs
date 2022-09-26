using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Tools
{
    public class ToolColor
    {
        public Microsoft.Xna.Framework.Color XNA
        {
            get
            {
                return new Microsoft.Xna.Framework.Color(R, G, B, A);
            }
        }

        public Microsoft.Xna.Framework.Color XNAPreMultiplied
        {
            get
            {
                return Microsoft.Xna.Framework.Color.FromNonPremultiplied(R, G, B, A);
            }
        }
        [XmlAttribute("R")]
        public byte R { get; set; }
        [XmlAttribute("G")]
        public byte G { get; set; }
        [XmlAttribute("B")]
        public byte B { get; set; }
        [XmlAttribute("A")]
        public byte A { get; set; }

        public ToolColor()
        {
        }

        public ToolColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
            A = 255;
        }

        public ToolColor(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public ToolColor(Microsoft.Xna.Framework.Color xnaColor)
        {
            R = xnaColor.R;
            G = xnaColor.G;
            B = xnaColor.B;
            A = xnaColor.A;
        }

        public ToolColor Invert()
        {
            return new ToolColor(
                (byte)Math.Abs(R - 255),
                (byte)Math.Abs(G - 255),
                (byte)Math.Abs(B - 255),
                A
                );
        }

        public override string ToString()
        {
            string output = "R:" + R + " G:" + G + " B:" + B;

            if (A != 255)
            {
                output += " A:" + A;
            }

            return output;
        }
    }

    public class ToolTexture : ToolColor
    {
        [XmlAttribute("TextureFilename")]
        public string TextureFilename { get; set; }
        public static ToolTexture Transparent
        {
            get
            {
                return new ToolTexture(0, 0, 0, 0);
            }
        }

        public ToolTexture()
        {
        }

        public ToolTexture(string filename, byte r, byte g, byte b) 
            :base(r, g, b)
        {
            TextureFilename = filename;
        }

        public ToolTexture(string filename, byte r, byte g, byte b, byte a)
            :base(r, g, b, a)
        {
            TextureFilename = filename;
        }

        public ToolTexture(byte r, byte g, byte b)
            : base(r, g, b)
        {
        }

        public ToolTexture(byte r, byte g, byte b, byte a)
            : base(r, g, b, a)
        {
        }

        public ToolTexture(Microsoft.Xna.Framework.Color xnaColor)
            :base(xnaColor)
        {
        }

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(TextureFilename))
            {
                return TextureFilename;
            }

            return base.ToString();
        }
    }
}
