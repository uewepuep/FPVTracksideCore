using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Tools;

namespace UI
{
    public class Theme2
    {
        public string Filename { get { return Directory + "/theme2.xml"; } }

        [XmlIgnore()]
        [Browsable(false)]
        public string Name { get; set; }

        [XmlIgnore()]
        [Browsable(false)]
        public DirectoryInfo Directory { get; set; }

        [XmlIgnore()]
        [Browsable(false)]
        public DateTime ReadTime { get; private set; }

        public string FontFamily { get; set; }

        public TextureRegion TopPanel { get; set; }
        public TextureColor TopPanelText { get; set; }

        public TextureRegion EventSelectorTop { get; set; }

        public TextureRegion Background { get; set; }

        public TextureRegion TabsBackground { get; set; }
        public TextureRegion TabForeground { get; set; }
        public TextureColor TabText { get; set; }
        public TextureColor ScrollBar { get; set; }


        public TextureRegion PanelBackground { get; set; }
        public TextureRegion PanelForeground { get; set; }
        public TextureColor PanelText { get; set; }
        public TextureColor PanelBorder { get; set; }
        public TextureRegion Heading { get; set; }
        public TextureColor HeadingText { get; set; }


        public TextureRegion LeftBackground { get; set; }
        public TextureRegion LeftPilotBackground { get; set; }
        public TextureColor LeftPilotText { get; set; }

        public TextureRegion RightBackground { get; set; }
        public TextureRegion RightButtonBackground { get; set; }
        public TextureColor RightText { get; set; }

        public Theme2()
        {
            FontFamily = "Roboto";

            ScrollBar = new TextureColor() { Filename = "theme.png", X = 2005, Y = 22 };

            TopPanel = new TextureRegion() { Filename = "theme.png", X = 0, Y = 0, W = 1900, H = 100};
            Background = new TextureRegion() { Filename = "background.png" };
            TopPanelText = new TextureColor() { Filename = "theme.png", X = 2005, Y = 7 };

            TabsBackground = new TextureRegion() { Filename = "theme.png", X = 0, Y = 100, W = 1900, H = 31 };
            TabForeground = new TextureRegion() { Filename = "theme.png", X = 1900, Y = 100, W = 148, H = 31 };
            TabText = new TextureColor() { Filename = "theme.png", X = 1970, Y = 93 };

            PanelBackground = new TextureRegion() { Filename = "theme.png", X = 305, Y = 174, W = 32, H = 32 };
            PanelForeground = new TextureRegion() { Filename = "theme.png", X = 341, Y = 174, W = 32, H = 32 };
            PanelText = new TextureColor() { Filename = "theme.png", X = 330, Y = 167 };
            PanelBorder = new TextureColor() { Filename = "theme.png", X = 379, Y = 167 };

            Heading = new TextureRegion() { Filename = "theme.png", X = 377, Y = 174, W = 1170, H = 32 };
            HeadingText = new TextureColor() { Filename = "theme.png", X = 504, Y = 164 };

            LeftBackground = new TextureRegion() { Filename = "theme.png", X = 0, Y = 131, W = 267, H = 870 };
            LeftPilotBackground = new TextureRegion() { Filename = "theme.png", X = 1, Y = 103, W = 263, H = 30 };
            LeftPilotText = new TextureColor() { Filename = "theme.png", X = 79, Y = 1041 };

            RightBackground = new TextureRegion() { Filename = "theme.png", X = 1836, Y = 131, W = 64, H = 870 };
            RightButtonBackground = new TextureRegion() { Filename = "theme.png", X = 1836, Y = 1002, W = 64, H = 64 };
            RightText = new TextureColor() { Filename = "theme.png", X = 1979, Y = 1052 };

            EventSelectorTop = new TextureRegion() { Filename = "theme.png", X = 377, Y = 174, W = 1170, H = 32 };
        }

        public Theme ToTheme(GraphicsDevice graphicsDevice, Theme baseTheme)
        {
            Theme theme = new Theme();
            Tools.ReflectionTools.Copy(baseTheme, theme);

            using (RawTextureCache rawTextureCache = new RawTextureCache(graphicsDevice, Directory))
            {
                theme.Directory = Directory;

                theme.Background = Background.ToToolTexture();

                theme.TopPanel = TopPanel.ToToolTexture();
                theme.TopPanelText = TopPanelText.ToToolColor(rawTextureCache);
                theme.EventSelectorTop = EventSelectorTop.ToToolTexture();

                theme.Tabs.Background = TabsBackground.ToToolTexture();
                theme.Tabs.Foreground = TabForeground.ToToolTexture();
                theme.Tabs.Text = TabText.ToToolColor(rawTextureCache);

                theme.Panel = PanelBackground.ToToolTexture();
                theme.PanelAlt = PanelForeground.ToToolTexture();
                theme.TextAlt = PanelText.ToToolColor(rawTextureCache);

                theme.Editor.Background = PanelBackground.ToToolTexture();
                theme.Editor.Foreground = PanelForeground.ToToolTexture();
                theme.Editor.Text = PanelText.ToToolColor(rawTextureCache);
                theme.Editor.Border = PanelBorder.ToToolColor(rawTextureCache);

                theme.InfoPanel.Background = PanelBackground.ToToolTexture();
                theme.InfoPanel.Foreground = PanelForeground.ToToolTexture();
                theme.InfoPanel.Text = PanelText.ToToolColor(rawTextureCache);
                theme.InfoPanel.Border = PanelBorder.ToToolColor(rawTextureCache);
                theme.InfoPanel.HeadingText = HeadingText.ToToolColor(rawTextureCache);
                theme.InfoPanel.Heading = Heading.ToToolTexture();

                theme.LeftPilotList.Background = LeftBackground.ToToolTexture();
                theme.LeftPilotList.Foreground = LeftPilotBackground.ToToolTexture();
                theme.LeftPilotList.Text = LeftPilotText.ToToolColor(rawTextureCache);

                theme.RightControls.Background = RightBackground.ToToolTexture();
                theme.RightControls.Foreground = RightButtonBackground.ToToolTexture();
                theme.RightControls.Text = RightText.ToToolColor(rawTextureCache);

            }

            return theme;
        }
    }

    public class TextureColor
    {
        public string Filename { get; set; }

        public int X { get; set; }
        public int Y { get; set; }


        public TextureColor()
        {
        }

        public ToolColor ToToolColor(RawTextureCache textureCache)
        {
            Color color = textureCache.GetColor(Filename, X, Y);
            return new ToolColor(color);
        }
    }

    public class TextureRegion
    {
        public string Filename { get; set; }


        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }


        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public byte A { get; set; }

        public TextureRegion()
        {
            A = 255;
        }

        public ToolTexture ToToolTexture()
        {
            return new ToolTexture(Filename, R, G, B, A, new Rectangle(X, Y, W, H));
        }
    }

    public class RawTextureCache : IDisposable
    {
        private TextureCache textureCache;


        private Dictionary<Texture2D, Color[]> rawData;

        private DirectoryInfo dir;

        public RawTextureCache(GraphicsDevice graphicsDevice, DirectoryInfo dir)
        {
            this.dir = dir;
            textureCache = new TextureCache(graphicsDevice);

            rawData = new Dictionary<Texture2D, Color[]>();
        }

        public void Dispose()
        {
            rawData.Clear();

            textureCache.Dispose();
        }

        public Color GetColor(string filename, int x, int y)
        {
            Texture2D texture2D = textureCache.GetTextureFromFilename(Path.Combine(dir.FullName, filename), false);

            Color[] data;
            if (!rawData.TryGetValue(texture2D, out data))
            {
                data = new Color[texture2D.Width * texture2D.Height];
                texture2D.GetData(data);
                rawData.Add(texture2D, data);
            }

            int i = y * texture2D.Width + x;

            Color output = new Color();

            if (i < data.Length)
            {
                output = data[i];
            }

            return output;
        }

    }

}
