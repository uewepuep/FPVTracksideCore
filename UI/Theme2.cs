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

        public bool TopPanelTextBorder { get; set; }

        public byte PilotTitleAlpha { get; set; }

        public TextureColor TextMain { get; set; }
        public TextureColor TextAlt { get; set; }

        public TextureRegion TopPanel { get; set; }
        public TextureColor TopPanelText { get; set; }

        public TextureRegion Background { get; set; }

        public TextureRegion TabsBackground { get; set; }
        public TextureRegion TabForeground { get; set; }
        public TextureColor TabText { get; set; }
        public TextureColor ScrollBar { get; set; }


        public TextureRegion PanelBackground { get; set; }
        public TextureRegion PanelForeground { get; set; }
        public TextureColor PanelText { get; set; }
        public TextureColor PanelBorder { get; set; }
        public TextureRegion PanelHeadingBackground { get; set; }
        public TextureColor PanelHeadingText { get; set; }


        public TextureRegion LeftBackground { get; set; }
        public TextureRegion LeftPilotBackground { get; set; }
        public TextureColor LeftPilotText { get; set; }

        public TextureRegion RightBackground { get; set; }
        public TextureRegion RightButtonBackground { get; set; }
        public TextureColor RightText { get; set; }


        public TextureColor OverallBestTime { get; set; }
        public TextureColor NewPersonalBest { get; set; }
        public TextureColor BehindTime { get; set; }
        public TextureColor AheadTime { get; set; }

        public TextureRegion ChannelLapBackground { get; set; }
        public TextureColor ChannelText { get; set; }
        public TextureRegion ChannelOverlayPanel { get; set; }
        public TextureRegion ChannelPilotNameBackground { get; set; }
        public TextureRegion ChannelPBBackground { get; set; }

        public TextureRegion MenuBackground { get; set; }
        public TextureColor MenuText { get; set; }
        public TextureColor MenuInactiveText { get; set; }


        public TextureRegion NoVideoBackground { get; set; }
        public TextureRegion CrashedOut { get; set; }

        public TextureColor[] ChannelColors { get; set; }


        public Theme2()
        {
            FontFamily = "Roboto";
            TopPanelTextBorder = true;
            PilotTitleAlpha = 160;

            ScrollBar = new TextureColor() { Filename = "theme.png", X = 2005, Y = 22 };
            TextMain = new TextureColor() { Filename = "theme.png", X = 2005, Y = 35 };
            TextAlt = new TextureColor() { Filename = "theme.png", X = 2010, Y = 35 };

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

            PanelHeadingBackground = new TextureRegion() { Filename = "theme.png", X = 377, Y = 174, W = 1170, H = 32 };
            PanelHeadingText = new TextureColor() { Filename = "theme.png", X = 504, Y = 164 };

            LeftBackground = new TextureRegion() { Filename = "theme.png", X = 0, Y = 131, W = 267, H = 870 };
            LeftPilotBackground = new TextureRegion() { Filename = "theme.png", X = 1, Y = 1003, W = 263, H = 30 };
            LeftPilotText = new TextureColor() { Filename = "theme.png", X = 79, Y = 1041 };

            RightBackground = new TextureRegion() { Filename = "theme.png", X = 1836, Y = 131, W = 64, H = 870 };
            RightButtonBackground = new TextureRegion() { Filename = "theme.png", X = 1836, Y = 1002, W = 64, H = 64 };
            RightText = new TextureColor() { Filename = "theme.png", X = 1979, Y = 1052 };

            ChannelPilotNameBackground = new TextureRegion() { Filename = "theme.png", X = 305, Y = 223, W = 211, H = 48 };
            ChannelPBBackground = new TextureRegion() { Filename = "theme.png", X = 305, Y = 271, W = 138, H = 26 };
            ChannelLapBackground = new TextureRegion() { Filename = "theme.png", X = 305, Y = 606, W = 526, H = 31 };
            ChannelOverlayPanel = new TextureRegion() { Filename = "theme.png", X = 305, Y = 330, W = 526, H = 158 };
            ChannelText = new TextureColor() { Filename = "theme.png", X = 640, Y = 233 };

            MenuBackground = new TextureRegion() { Filename = "theme.png", X = 916, Y = 269, W = 207, H = 173 };
            MenuText = new TextureColor() { Filename = "theme.png", X = 968, Y = 235 };
            MenuInactiveText = new TextureColor() { Filename = "theme.png", X = 968, Y = 254 };

            NoVideoBackground = new TextureRegion() { Filename = "theme.png", X = 304, Y = 687, W = 282, H = 238 };
            CrashedOut = new TextureRegion() { Filename = "theme.png", X = 615, Y = 688, W = 282, H = 237 };

            ChannelColors = new TextureColor[]
            {
                new TextureColor() { Filename = "theme.png", X = 1673, Y = 209 },
                new TextureColor() { Filename = "theme.png", X = 1673, Y = 228 },

                new TextureColor() { Filename = "theme.png", X = 1673, Y = 247 },
                new TextureColor() { Filename = "theme.png", X = 1673, Y = 266 },
                
                new TextureColor() { Filename = "theme.png", X = 1673, Y = 285 },
                new TextureColor() { Filename = "theme.png", X = 1673, Y = 304 },
                
                new TextureColor() { Filename = "theme.png", X = 1673, Y = 324 },
                new TextureColor() { Filename = "theme.png", X = 1673, Y = 343 },
            };
        }

        public Theme ToTheme(GraphicsDevice graphicsDevice, Theme baseTheme)
        {
            Theme theme = new Theme();
            Tools.ReflectionTools.Copy(baseTheme, theme);

            using (RawTextureCache rawTextureCache = new RawTextureCache(graphicsDevice, Directory))
            {
                theme.Directory = Directory;
                theme.TextMain = TextMain.ToToolColor(rawTextureCache);
                theme.Background = Background.ToToolTexture(rawTextureCache);

                theme.TopPanel = TopPanel.ToToolTexture(rawTextureCache);
                theme.TopPanelText = TopPanelText.ToToolColor(rawTextureCache);
                theme.EventSelectorTop = PanelHeadingBackground.ToToolTexture(rawTextureCache);

                theme.Tabs.Background = TabsBackground.ToToolTexture(rawTextureCache);
                theme.Tabs.Foreground = TabForeground.ToToolTexture(rawTextureCache);
                theme.Tabs.Text = TabText.ToToolColor(rawTextureCache);

                theme.Panel = PanelBackground.ToToolTexture(rawTextureCache);
                theme.PanelAlt = PanelForeground.ToToolTexture(rawTextureCache);

                theme.Editor.Background = PanelBackground.ToToolTexture(rawTextureCache);
                theme.Editor.Foreground = PanelForeground.ToToolTexture(rawTextureCache);
                theme.Editor.Text = PanelText.ToToolColor(rawTextureCache);
                theme.Editor.Border = PanelBorder.ToToolColor(rawTextureCache);

                theme.InfoPanel.Background = PanelBackground.ToToolTexture(rawTextureCache);
                theme.InfoPanel.Foreground = PanelForeground.ToToolTexture(rawTextureCache);
                theme.InfoPanel.Text = PanelText.ToToolColor(rawTextureCache);
                theme.InfoPanel.Border = PanelBorder.ToToolColor(rawTextureCache);
                theme.InfoPanel.HeadingText = PanelHeadingText.ToToolColor(rawTextureCache);
                theme.InfoPanel.Heading = PanelHeadingBackground.ToToolTexture(rawTextureCache);

                theme.LeftPilotList.Background = LeftBackground.ToToolTexture(rawTextureCache);
                theme.LeftPilotList.Foreground = LeftPilotBackground.ToToolTexture(rawTextureCache);
                theme.LeftPilotList.Text = LeftPilotText.ToToolColor(rawTextureCache);

                theme.RightControls.Background = RightBackground.ToToolTexture(rawTextureCache);
                theme.RightControls.Foreground = RightButtonBackground.ToToolTexture(rawTextureCache);
                theme.RightControls.Text = RightText.ToToolColor(rawTextureCache);

                theme.Rounds.Background = PanelBackground.ToToolTexture(rawTextureCache);
                theme.Rounds.Foreground = PanelForeground.ToToolTexture(rawTextureCache);
                theme.Rounds.Text = PanelText.ToToolColor(rawTextureCache);
                theme.Rounds.Border = PanelBorder.ToToolColor(rawTextureCache);
                theme.Rounds.RaceTitle = PanelForeground.ToToolTexture(rawTextureCache);
                theme.Rounds.Heading = PanelHeadingBackground.ToToolTexture(rawTextureCache);

                theme.PilotViewTheme.PBBackground = ChannelPBBackground.ToToolTexture(rawTextureCache);
                theme.PilotViewTheme.PilotNameBackground = ChannelPilotNameBackground.ToToolTexture(rawTextureCache);
                theme.PilotViewTheme.LapBackground = ChannelLapBackground.ToToolTexture(rawTextureCache);
                theme.PilotViewTheme.PilotOverlayPanel = ChannelOverlayPanel.ToToolTexture(rawTextureCache);
                theme.PilotViewTheme.PilotTitleAlpha = PilotTitleAlpha;
                theme.PilotViewTheme.CrashedOut = CrashedOut.ToToolTexture(rawTextureCache);
                theme.PilotViewTheme.NoVideoBackground = NoVideoBackground.ToToolTexture(rawTextureCache);


                theme.MenuBackground = MenuBackground.ToToolTexture(rawTextureCache);
                theme.MenuTextInactive = MenuInactiveText.ToToolColor(rawTextureCache);
                theme.MenuText = MenuText.ToToolColor(rawTextureCache);

                theme.Button = PanelForeground.ToToolTexture(rawTextureCache);
                theme.TopPanelTextBorder = TopPanelTextBorder;

                theme.ChannelColors = ChannelColors.Select(c => c.ToToolColor(rawTextureCache)).ToArray();
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

        public TextureRegion()
        {
        }

        public ToolTexture ToToolTexture(RawTextureCache textureCache)
        {
            Rectangle r = new Rectangle(X, Y, W, H);

            Color c = textureCache.GetColor(Filename, r.Center.X, r.Center.Y);

            return new ToolTexture(Filename, c.R, c.G, c.B, c.A, r);
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
