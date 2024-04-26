using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Text
{
    public class TextRenderBMP : ITextRenderer
    {
        public Tools.Size TextSize { get; set; }

        public float FontPoint { get; set; }

        public bool CanCreateTextures { get; set; }

        private List<Rectangle> sourceBounds;
        private List<Rectangle> destBounds;

        private BitmapFont font;
        private string text;
        private Style style;

        public TextRenderBMP()
        {
            sourceBounds = new List<Rectangle>();
            destBounds = new List<Rectangle>();
            text = "";
            CanCreateTextures = true;
        }

        public void Dispose()
        {
        }

        public void Reset()
        {
        }

        public void CreateGeometry(int width, int height, string text, Style style)
        {
            this.text = text;
            this.style = style;

            int lines = Maths.CountLines(text);

            int lineHeight = height / lines;

            font = BitmapFontLibrary.GetFont(lineHeight, style);

            if (font == null)
                return;

            int x = 0, y = 0;

            int measuredWidth = 0;

            //Measure width..
            foreach (char c in text)
            {
                if (c == '\n')
                {
                    y += lineHeight;
                    x = 0;
                }

                Rectangle bounds = font.GetBounds(c);
                if (bounds != Rectangle.Empty)
                {
                    Rectangle dest = new Rectangle(x, y, bounds.Width, bounds.Height);
                    x += bounds.Width;

                    measuredWidth = Math.Max(dest.Right, measuredWidth);
                }
            }

            if (measuredWidth > width && width > 0)
            {
                float factor = width / (float)measuredWidth;
                lineHeight = (int)(lineHeight * factor);

                font = BitmapFontLibrary.GetFont(lineHeight, style);
            }

            if (font == null)
                return;

            lock (sourceBounds)
            {

                sourceBounds.Clear();
                destBounds.Clear();

                if (font == null)
                    return;

                measuredWidth = x = y = 0;
                int actualHeight = 0;
                foreach (char c in text)
                {
                    if (c == '\n')
                    {
                        y += lineHeight;
                        x = 0;
                    }

                    Rectangle bounds = font.GetBounds(c);
                    if (bounds != Rectangle.Empty)
                    {
                        sourceBounds.Add(bounds);

                        Rectangle dest = new Rectangle(x, y, bounds.Width, bounds.Height);
                        destBounds.Add(dest);

                        x += bounds.Width;
                        actualHeight = Math.Max(actualHeight, bounds.Height + y);
                        measuredWidth = Math.Max(dest.Right, measuredWidth);
                    }
                }

                TextSize = new Size(measuredWidth, actualHeight);
            }
        }

        public void CreateTextures(Drawer graphicsDevice)
        {
        }

        public bool Render()
        {
            return true;
        }

        public void Draw(Drawer id, RectangleF target, RectangleAlignment alignment, Scale scale, Color tint, float alpha)
        {
            Rectangle rectangle = new Rectangle((int)target.X, (int)target.Y, (int)target.Width, (int)target.Height);
            Draw(id, rectangle, alignment, scale, tint, alpha);
        }

        public void Draw(Drawer id, Rectangle target, RectangleAlignment alignment, Scale scale, Color tint, float alpha)
        {
            if (font != null)
            {
                lock (sourceBounds)
                {
                    CreateGeometry(target.Width, target.Height, text, style);

                    float scaleFactor = 1;

                    Rectangle textBounds = new Rectangle(0, 0, TextSize.Width, TextSize.Height);

                    scaleFactor = Maths.ScaleFactor(target, textBounds, FitType.FitBoth);

                    Rectangle bounds = Maths.FitBoxMaintainAspectRatio(target, textBounds, scaleFactor, alignment);

                    for (int i = 0; i < Math.Min(sourceBounds.Count, destBounds.Count); i++)
                    {
                        Rectangle dest = destBounds[i];
                        dest.X = (int)(scaleFactor * dest.X) + bounds.X;
                        dest.Y = (int)(scaleFactor * dest.Y) + bounds.Y;
                        dest.Width = (int)(scaleFactor * dest.Width);
                        dest.Height = (int)(scaleFactor * dest.Height);

                        id.Draw(font.GetTexture(id.GraphicsDevice), sourceBounds[i], dest, tint, alpha);
                    }
                }
            }
        }

        public int HitCharacterIndex(Point point)
        {
            int index = 0;
            foreach (Rectangle rectangle in destBounds)
            {
                if (rectangle.Contains(point))
                {
                    return index;
                }
                index++;
            }
            return index;
        }

        public Point CharacterPosition(int i)
        {
            if (!destBounds.Any())
                return Point.Zero;

            if (i < destBounds.Count)
            {
                Rectangle bounds = destBounds[i];

                return new Point(bounds.Left, bounds.Top);
            }

            Rectangle last = destBounds.Last();
            return new Point(last.Right, last.Top);
        }

        public Texture2D TextToTexture(GraphicsDevice graphicsDevice, Color textColor, string font, float point, string text)
        {
            text = "H";

            TextRenderBMP textRenderBMP = new TextRenderBMP();
            textRenderBMP.CreateGeometry(0, TextSize.Height, text, style);

            RenderTarget2D renderTarget = new RenderTarget2D(graphicsDevice, Math.Min(4096, textRenderBMP.TextSize.Width), Math.Min(4096, textRenderBMP.TextSize.Height));
            using (Drawer drawer = new Drawer(renderTarget.GraphicsDevice))
            {
                drawer.Begin();
                textRenderBMP.Draw(drawer, new Rectangle(0, 0, renderTarget.Width, renderTarget.Height), RectangleAlignment.Center, Scale.Disallowed, Color.White, 1);
                drawer.End();
            }

            renderTarget.SaveAs("test.png");

            return renderTarget;
        }

        public void SavePNG(string filename)
        {
        }
    }
}
