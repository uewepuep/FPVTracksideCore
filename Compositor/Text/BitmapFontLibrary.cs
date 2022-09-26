using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Text
{
    public static class BitmapFontLibrary
    {
        private static List<BitmapFont> fonts = null;

        private static DirectoryInfo fontsDir;

#if DEBUG
        private static List<int> requested;
#endif

        public static void Init(DirectoryInfo workingDirectory)
        {
            fontsDir = new DirectoryInfo(Path.Combine(workingDirectory.FullName, "bitmapfonts"));
            if (fonts == null)
            {
                fonts = new List<BitmapFont>();

                lock (fonts)
                {
                    if (!fontsDir.Exists)
                        fontsDir.Create();

                    BitmapFontDef[] definitions = BitmapFontDef.Read();
                    foreach (BitmapFontDef def in definitions)
                    {
                        FileInfo texture = new FileInfo(Path.Combine(workingDirectory.FullName, def.TextureFilename));

                        if (texture.Exists)
                        {
                            BitmapFont bitmapFont = new BitmapFont(def.CharBounds.ToDictionary(c => (char)c.Char, c => c.Bounds), texture.FullName, def.Style, def.FontHeight);
                            fonts.Add(bitmapFont);
                        };
                    }
                }
            }
        }

        public static BitmapFont GetFont(int height, Style style)
        {
            if (fonts == null)
            {
                return null;
            }
            lock (fonts)
            {
#if DEBUG
                if (requested == null)
                {
                    requested = new List<int>();
                }
                if (!requested.Contains(height))
                {
                    requested.Add(height);
                }

                IEnumerable<int> ordered = requested.OrderBy(r => r);
#endif


                foreach (BitmapFont font in fonts.Where(r => r.Style.Equals(style)))
                {
                    if (font.FontHeight >= height)
                    {
                        return font;
                    }
                }

                foreach (BitmapFont font in fonts)
                {
                    if (font.FontHeight >= height)
                    {
                        return font;
                    }
                }

                return fonts.LastOrDefault();
            }
        }
    }


    public class BitmapFontDef
    {
        public int FontHeight { get; set; }

        public Style Style { get; set; }

        public string TextureFilename { get; set; }

        public CharBounds[] CharBounds { get; set; }

        private static string filename = "bitmapfonts/fonts.xml";

        public BitmapFontDef()
        {
        }

        public static void Write(BitmapFontDef[] s)
        {
            IOTools.Write(filename, s);
        }

        public static BitmapFontDef[] Read()
        {
            return IOTools.Read<BitmapFontDef>(filename);
        }

    }

    public class CharBounds
    {
        public short Char { get; set; }
        public Rectangle Bounds { get; set; }

        public CharBounds()
        {
        }
    }
}
