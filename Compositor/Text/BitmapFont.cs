
using Composition;
using Composition.Nodes;
using Composition.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using Tools;

namespace Composition.Text
{
    public class BitmapFont : IDisposable
    {
        private Dictionary<char, Rectangle> charBounds;

        public int FontHeight { get; set; }

        public Style Style { get; private set; }

        public IEnumerable<KeyValuePair<char, Rectangle>> CharBounds
        {
            get
            {
                return charBounds;
            }
        }

        private string filename;
        private Texture2D texture;

        public BitmapFont(Dictionary<char, Rectangle> bounds, string filename, Style style, int fontHeight)
        {
            FontHeight = fontHeight;
            charBounds = bounds;
            Style = style;
            texture = null;
            this.filename = filename;
        }

        public BitmapFont(Dictionary<char, Rectangle> bounds,  Texture2D texture, Style style, int fontHeight)
            :this(bounds, "", style, fontHeight)
        {
            this.texture = texture;
        }

        public void Dispose()
        {
            texture?.Dispose();
            texture = null;
        }

        public override string ToString()
        {
            return Style.ToString() + " " + FontHeight.ToString() + "px";
        }

        public string Save(DirectoryInfo directory)
        {
            string filename = directory.Name + "/" + ToString() + ".png";

            texture.SaveAs(filename);

            return filename;
        }

        public Rectangle GetBounds(char c)
        {
            Rectangle sourceBounds;
            if (charBounds.TryGetValue(c, out sourceBounds))
            {
                return sourceBounds;
            }
            return Rectangle.Empty;
        }

        public Texture2D GetTexture(GraphicsDevice graphicsDevice)
        {
            if (texture == null && !string.IsNullOrEmpty(filename))
            {
                texture = TextureHelper.LoadTexture(graphicsDevice, filename, true);
            }

            return texture;
        }

        public void DrawText(Drawer ig, string text, Rectangle destination)
        {
            if (!string.IsNullOrEmpty(text))
            {
                Texture2D texture = GetTexture(ig.GraphicsDevice);

                if (texture != null)
                {
                    int offset = 0;
                    foreach (char c in text)
                    {
                        Rectangle sourceBounds = GetBounds(c);
                        if (sourceBounds != Rectangle.Empty)
                        {
                            Rectangle charBounds = new Rectangle(sourceBounds.X + offset, sourceBounds.Y, sourceBounds.Width, sourceBounds.Height);
                            offset = charBounds.Right - 4;
                            ig.Draw(texture, destination, charBounds, Color.White, 1);
                        }
                        else
                        {
                            offset += 6;
                        }
                    }
                }
            }
        }
    }

}
