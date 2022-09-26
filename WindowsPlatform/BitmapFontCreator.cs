using Composition;
using Composition.Text;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsPlatform
{
    public class BitmapFontCreator
    {
        public static List<BitmapFont> Create(Drawer d, DirectoryInfo fontsDir)
        {
            List<BitmapFont> fonts = new List<BitmapFont>();

            if (!fontsDir.Exists)
                fontsDir.Create();

            List<BitmapFontDef> list = new List<BitmapFontDef>();

            bool[] trueFalse = new bool[] { true, false };

            int incrementer = 2;

            foreach (bool border in trueFalse)
            {
                for (int height = 10; height < 160; height += incrementer)
                {
                    Style style = new Style();

                    BitmapFont bf = TextRenderWPF.QuickCreateBitmapFont(d, -1, height, style);
                    fonts.Add(bf);

                    string filename = bf.Save(fontsDir);

                    BitmapFontDef def = new BitmapFontDef();
                    def.CharBounds = bf.CharBounds.Select(cb => new CharBounds() { Char = (short)cb.Key, Bounds = cb.Value }).ToArray();
                    def.FontHeight = bf.FontHeight;
                    def.Style = bf.Style;
                    def.TextureFilename = filename;
                    list.Add(def);

                    if (height > 100)
                    {
                        incrementer = 20;
                    }
                    else if (height > 50)
                    {
                        incrementer = 5;
                    }
                }
            }


            BitmapFontDef.Write(list.ToArray());
            return fonts;
        }
    }
}
