using Composition;
using Composition.Input;
using Composition.Text;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tools;

namespace WindowsPlatform
{
    public class TextRenderWPF : ITextRenderer
    {
        private FormattedText formattedText;
        private DrawingVisual drawingVisual;
        private byte[] rawPixels;
        private int rawWidth;
        private int rawHeight;
        private static CultureInfo cultureInfo;

        private Size newTextSize;
        public Size TextSize { get; set; }

        public int MinPoint { get; set; }
        public int MaxPoint { get; set; }
        public float FontPoint { get; set; }

        public bool CanCreateTextures { get; set; }

        private const float pixelToPnt = 80 / 100.0f;

        private Texture2D texture;

        private float scaleFactor;

        private string text;

        private object locker;

        private float offsetX;
        private float offsetY;

        public TextRenderWPF()
        {

            locker = new object();

            CanCreateTextures = false;
            MinPoint = 8;
            MaxPoint = 360;
            scaleFactor = 1;
            Reset();
        }

        public void Dispose()
        {
            Reset();
        }


        public float LineHeightToPnt(int height)
        {
            float fontPnt = height * pixelToPnt;

            if (fontPnt > MaxPoint)
                fontPnt = MaxPoint;
            if (fontPnt < MinPoint)
                fontPnt = MinPoint;

            return fontPnt;
        }

        public void Reset()
        {
            offsetX = 0;
            offsetY = 0;

            drawingVisual = null;
            cultureInfo = null;

            if (texture != null)
            {
                texture.Dispose();
                texture = null;
            }
        }

        public void CreateGeometry(int width, int height, string text, Style style)
        {
            this.text = text;
            int lines = Maths.CountLines(text);

            FontPoint = LineHeightToPnt(height / lines);
            DrawingVisual tempDrawingVisual = new DrawingVisual();


            System.Windows.FontWeight fw = /*style.Bold ? System.Windows.FontWeights.Bold : */System.Windows.FontWeights.Medium;
            System.Windows.FontStyle fs = /*style.Italic ? System.Windows.FontStyles.Italic : */System.Windows.FontStyles.Normal;

            FontFamily ff = new FontFamily(style.Font);
            Typeface tf = new Typeface(ff, fs, fw, System.Windows.FontStretches.Normal);

            Color color = Color.FromArgb(style.TextColor.A, style.TextColor.R, style.TextColor.G, style.TextColor.B);
            SolidColorBrush brush = new SolidColorBrush(color);
           
            using (DrawingContext drawingContext = tempDrawingVisual.RenderOpen())
            {
                Draw(0,text, FontPoint, width, drawingContext, tf, style, brush, out newTextSize, out formattedText);
            }

            drawingVisual = tempDrawingVisual;

            if (newTextSize.Height > 0 && newTextSize.Width > 0)
            {
                CanCreateTextures = Render();
            }
            else
            {
                CanCreateTextures = false;
            }
        }

        private void Draw(int y, string text, float fontPoint, int width, DrawingContext drawingContext, Typeface tf, Style style, SolidColorBrush brush, out Size newTextSize, out FormattedText formattedText)
        {
            if (cultureInfo == null)
            {
                cultureInfo = CultureInfo.GetCultureInfo("en-us");
            }

#pragma warning disable CS0618 // Type or member is obsolete
            formattedText = new FormattedText(text,
                                                cultureInfo,
                                                System.Windows.FlowDirection.LeftToRight,
                                                tf,
                                                (int)fontPoint,
                                                brush);
#pragma warning restore CS0618 // Type or member is obsolete

            formattedText.Trimming = System.Windows.TextTrimming.WordEllipsis;

            if (width > 0)
            {
                formattedText.MaxTextWidth = width;
            }

            Geometry textGeometry = formattedText.BuildGeometry(new System.Windows.Point(0, y));
            
            if (style.Border)
            {
                SolidColorBrush borderBrush = new SolidColorBrush(Color.FromArgb(64, 0, 0, 0));
                drawingContext.DrawGeometry(borderBrush, new System.Windows.Media.Pen(borderBrush, 3), textGeometry);
            }

            drawingContext.DrawText(formattedText, new System.Windows.Point(0, 0));

            newTextSize = new Size((int)Math.Ceiling(formattedText.Width), (int)Math.Ceiling(formattedText.Height));

            int maxSize = 2000;
            if (newTextSize.Width > maxSize)
            {
                float ratio = maxSize / (float)newTextSize.Width;
                newTextSize = new Size(maxSize, (int)(newTextSize.Height * ratio));
            }

            if (newTextSize.Height > maxSize)
            {
                float ratio = maxSize / (float)newTextSize.Height;
                newTextSize = new Size((int)(newTextSize.Width * ratio), maxSize);
            }
            //SolidColorBrush tempBrush = new SolidColorBrush(Color.FromArgb(64, 0, 128, 0));
            //Pen pen = new Pen(tempBrush, 1);
            //drawingContext.DrawRectangle(tempBrush, pen, new System.Windows.Rect(0, 0, newTextSize.Width, newTextSize.Height));
        }

        public bool Render()
        {
            DrawingVisual tempDrawingVisual = drawingVisual;

            if (tempDrawingVisual == null)
                return false;

            if (newTextSize.Width == 0 || newTextSize.Height == 0)
                return false;

            if (newTextSize.Width > 2000 || newTextSize.Height > 2000)
                return false;

            lock (locker)
            {
                rawWidth = newTextSize.Width;
                rawHeight = newTextSize.Height;

                RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(rawWidth, rawHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                renderTargetBitmap.Render(tempDrawingVisual);

                int stride = rawWidth * 4;
                rawPixels = new byte[rawHeight * stride];

                renderTargetBitmap.CopyPixels(rawPixels, stride, 0);
            }

            return true;
        }

        public void CreateTextures(Drawer drawer)
        {
            if (texture != null)
            {
                drawer.CleanUp(texture);
                texture = null;
            }

            if (rawPixels == null || rawWidth == 0 || rawHeight == 0)
                return;

            lock (locker)
            {
                texture = WinTextureHelper.WPFtoTexture(drawer.GraphicsDevice, rawPixels, rawWidth, rawHeight);
                TextSize = newTextSize;
            }
        }

        public void Draw(Drawer id, Microsoft.Xna.Framework.Rectangle target, RectangleAlignment alignment, Scale scale, Microsoft.Xna.Framework.Color tint, float alpha)
        {
            Texture2D t = texture;
            if (t == null)
                return;

            Microsoft.Xna.Framework.Rectangle sourceBounds = new Microsoft.Xna.Framework.Rectangle(0, 0, t.Width, t.Height);

            scaleFactor = 1;

            bool scaleImage = Math.Abs(target.Height - t.Height) > 10 || target.Width < t.Width || scale == Scale.Force;
            if (scaleImage && scale != Scale.Disallowed)
            {
                scaleFactor = Maths.ScaleFactor(target, sourceBounds, FitType.FitBoth);
            }

            Microsoft.Xna.Framework.Rectangle bounds = Maths.FitBoxMaintainAspectRatio(target, sourceBounds, scaleFactor, alignment);

            offsetX = target.X - bounds.X;
            offsetY = target.Y - bounds.Y;

            //if (text == "Finished in 76.74 - Fastest lap 12.96 - 38km/h")
            //{
            //    id.QuickDraw(bounds);
            //    System.Diagnostics.Debug.WriteLine(target + " " + bounds);
            //}

            id.Draw(t, sourceBounds, bounds, tint, alpha);

            //BitmapFontCreator.Create(id, new System.IO.DirectoryInfo("bitmapfonts"));
        }

        public static BitmapFont QuickCreateBitmapFont(Drawer drawer, int width, int height, Style style)
        {
            Dictionary<char, Microsoft.Xna.Framework.Rectangle> bounds;
            Texture2D texture;
            QuickCreateBitmapFontTexture(drawer, width, height, style, out bounds, out texture);

            BitmapFont bf = new BitmapFont(bounds, texture, style, height);
            return bf;
        }

        public static void QuickCreateBitmapFontTexture(Drawer drawer, int width, int height, Style style, out Dictionary<char, Microsoft.Xna.Framework.Rectangle> bounds, out Texture2D texture)
        {
            List<TextRenderWPF> renderers = new List<TextRenderWPF>();
            bounds = new Dictionary<char, Microsoft.Xna.Framework.Rectangle>();

            for (char c = (char)0; c < 256; c++)
            {
                TextRenderWPF renderer = new TextRenderWPF();

                renderer.CreateGeometry(width, height, "" + c, style);


                if (!renderer.Render())
                {
                    // If it didn't render because its a whitespace, give it a width and it'll be fine.
                    renderer.newTextSize.Width = (int)(renderer.newTextSize.Height * 0.20);
                    renderer.Render();
                }

                if (renderer.rawPixels != null)
                {
                    renderer.CreateTextures(drawer);
                    renderers.Add(renderer);
                }
            }

            Microsoft.Xna.Framework.Rectangle[] rectangles;

            TextureHelper.TextureCombiner(drawer.GraphicsDevice, height, renderers.Select(r => r.texture), out rectangles, out texture);

            for (int i = 0; i < renderers.Count; i++)
            {
                char c = renderers[i].text.First();
                bounds.Add(c, rectangles[i]);
                renderers[i].Dispose();
            }
        }

        public int HitCharacterIndex(Microsoft.Xna.Framework.Point pointXNA)
        {
            System.Windows.Point point = new System.Windows.Point(pointXNA.X, pointXNA.Y);

            // Adjust for the location of the texture in the node (alignment)
            point.X -= offsetX;
            point.Y -= offsetY;

            float oneOver = 1 / scaleFactor;
            point = new System.Windows.Point(point.X * oneOver, point.Y * oneOver);

            if (scaleFactor != 0 && formattedText != null)
            {
                int i;
                for (i = 0; i < formattedText.Text.Length; i++)
                {
                    Geometry characterHighlightGeometry = formattedText.BuildHighlightGeometry(new System.Windows.Point(), i, 1);
                    if (characterHighlightGeometry.Bounds.Contains(point))
                    {
                        System.Windows.Point andHalfWidth = new System.Windows.Point(point.X + (characterHighlightGeometry.Bounds.Width / 2), point.Y);
                        if (!characterHighlightGeometry.Bounds.Contains(andHalfWidth))
                        {
                            return i + 1;
                        }

                        return i;
                    }
                }
                return i;
            }
            return -1;
        }

        public Microsoft.Xna.Framework.Point CharacterPosition(int i)
        {
            if (formattedText != null)
            {
                // special case for right character
                if (i >= formattedText.Text.Length)
                {
                    Geometry characterHighlightGeometry = formattedText.BuildHighlightGeometry(new System.Windows.Point(), formattedText.Text.Length - 1, 1);
                    return new Microsoft.Xna.Framework.Point(
                        (int)((characterHighlightGeometry.Bounds.Right * scaleFactor) - offsetX),
                        (int)((characterHighlightGeometry.Bounds.Top * scaleFactor) - offsetY));
                }

                if (i >= 0 && i < formattedText.Text.Length)
                {
                    Geometry characterHighlightGeometry = formattedText.BuildHighlightGeometry(new System.Windows.Point(), i, 1);
                    return new Microsoft.Xna.Framework.Point(
                        (int)((characterHighlightGeometry.Bounds.Left * scaleFactor) - offsetX),
                        (int)((characterHighlightGeometry.Bounds.Top * scaleFactor) - offsetY));
                }
            }
            return Microsoft.Xna.Framework.Point.Zero;
        }
        public Texture2D TextToTexture(GraphicsDevice graphicsDevice, Microsoft.Xna.Framework.Color textColor, string font, float point, string text)
        {
            return WinTextureHelper.TextToTexture(graphicsDevice, textColor, font, point, text);    
        }

        public void SavePNG(string filename)
        {
            if (texture != null)
            {
                texture.SaveAsPng(filename + ".png");
            }
        }
    }
}
