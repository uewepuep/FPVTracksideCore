using Composition;
using Composition.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;

namespace FPVMacsideCore
{
    public class TextRenderSkia : ITextRenderer
    {
        private byte[] rawPixels;
        private int rawWidth;
        private int rawHeight;

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
        private Style style;

        private object locker;

        private float offsetX;
        private float offsetY;

        private List<SKRect> characterBounds;

        public TextRenderSkia()
        {
            locker = new object();
            characterBounds = new List<SKRect>();

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

            lock (locker)
            {
                if (texture != null)
                {
                    texture.Dispose();
                    texture = null;
                }
                rawPixels = null;
            }
        }

        public void CreateGeometry(int width, int height, string text, Style style)
        {
            this.text = text ?? "";
            this.style = style;

            if (string.IsNullOrEmpty(this.text))
            {
                CanCreateTextures = false;
                return;
            }

            int lines = Maths.CountLines(text);
            FontPoint = LineHeightToPnt(height / lines);

            lock (locker)
            {
                characterBounds.Clear();

                using (var paint = CreatePaint(style, FontPoint))
                {
                    // Measure the text
                    SKRect bounds = new SKRect();
                    paint.MeasureText(text, ref bounds);

                    // Handle multi-line text
                    string[] textLines = text.Split('\n');
                    float totalHeight = textLines.Length * FontPoint * 1.2f;
                    float maxWidth = 0;

                    foreach (string line in textLines)
                    {
                        SKRect lineBounds = new SKRect();
                        paint.MeasureText(line, ref lineBounds);
                        maxWidth = Math.Max(maxWidth, lineBounds.Width);
                    }

                    // Check if we need to scale down to fit width
                    if (width > 0 && maxWidth > width)
                    {
                        float ratio = width / maxWidth;
                        FontPoint *= ratio;
                    }

                    // Re-measure with potentially adjusted font size
                    using (var adjustedPaint = CreatePaint(style, FontPoint))
                    {
                        maxWidth = 0;
                        totalHeight = 0;
                        float y = 0;

                        foreach (string line in textLines)
                        {
                            SKRect lineBounds = new SKRect();
                            adjustedPaint.MeasureText(line, ref lineBounds);
                            maxWidth = Math.Max(maxWidth, lineBounds.Width);

                            // Calculate character bounds for hit testing
                            float x = 0;
                            foreach (char c in line)
                            {
                                float charWidth = adjustedPaint.MeasureText(c.ToString());
                                characterBounds.Add(new SKRect(x, y, x + charWidth, y + FontPoint));
                                x += charWidth;
                            }
                            // Add newline character bounds
                            characterBounds.Add(new SKRect(x, y, x, y + FontPoint));

                            y += FontPoint * 1.2f;
                        }
                        totalHeight = y;

                        newTextSize = new Size(
                            Math.Max(1, (int)Math.Ceiling(maxWidth)),
                            Math.Max(1, (int)Math.Ceiling(totalHeight))
                        );
                    }
                }

                // Limit size
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

                if (newTextSize.Height > 0 && newTextSize.Width > 0)
                {
                    CanCreateTextures = Render();
                }
                else
                {
                    CanCreateTextures = false;
                }
            }
        }

        private SKTypeface cachedTypeface;
        private string cachedTypefaceText;

        private SKPaint CreatePaint(Style style, float fontSize)
        {
            var paint = new SKPaint
            {
                TextSize = fontSize,
                IsAntialias = true,
                Color = new SKColor(style.TextColor.R, style.TextColor.G, style.TextColor.B, style.TextColor.A),
                IsStroke = false,
                TextAlign = SKTextAlign.Left
            };

            // Find a typeface that can render the text
            SKTypeface typeface = FindTypefaceForText(this.text ?? "");

            if (typeface != null)
            {
                paint.Typeface = typeface;
            }

            return paint;
        }

        private SKTypeface FindTypefaceForText(string text)
        {
            // Use cached typeface if text hasn't changed
            if (cachedTypeface != null && cachedTypefaceText == text)
            {
                return cachedTypeface;
            }

            var fontManager = SKFontManager.Default;

            // Find a character that needs CJK support
            char testChar = 'A';
            foreach (char c in text)
            {
                if (c > 0x2E80) // CJK character range starts around here
                {
                    testChar = c;
                    break;
                }
            }

            // Use MatchCharacter to find a font that can render this character
            SKTypeface typeface = null;

            if (testChar > 0x2E80)
            {
                // Try to match a CJK character
                typeface = fontManager.MatchCharacter(null, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright, null, testChar);
            }

            // If that didn't work, try explicit font names
            if (typeface == null || !CanRenderText(typeface, text))
            {
                string[] fontFamilies = new string[]
                {
                    "Hiragino Kaku Gothic ProN",  // Japanese
                    "Hiragino Sans",              // Japanese
                    "PingFang SC",                // Chinese Simplified
                    "PingFang TC",                // Chinese Traditional
                    "Apple SD Gothic Neo",        // Korean
                    "Noto Sans CJK JP",           // Japanese
                    "Noto Sans CJK SC",           // Chinese
                    "Yu Gothic",                  // Japanese
                    "MS Gothic",                  // Japanese
                    "Arial Unicode MS",
                    "Helvetica Neue",
                    "Roboto"
                };

                foreach (string fontFamily in fontFamilies)
                {
                    var testTypeface = SKTypeface.FromFamilyName(fontFamily);
                    if (testTypeface != null && CanRenderText(testTypeface, text))
                    {
                        typeface = testTypeface;
                        break;
                    }
                }
            }

            // Final fallback - use system default
            if (typeface == null)
            {
                typeface = SKTypeface.FromFamilyName(null);
            }

            cachedTypeface = typeface;
            cachedTypefaceText = text;

            return typeface;
        }

        private bool CanRenderText(SKTypeface typeface, string text)
        {
            if (typeface == null || string.IsNullOrEmpty(text))
                return false;

            // Check if the typeface has glyphs for all CJK characters in the text
            foreach (char c in text)
            {
                if (c > 0x2E80) // CJK range
                {
                    ushort glyphId = typeface.GetGlyph(c);
                    if (glyphId == 0)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private bool Render()
        {
            if (string.IsNullOrEmpty(text))
                return false;

            if (newTextSize.Width <= 0 || newTextSize.Height <= 0)
                return false;

            lock (locker)
            {
                rawWidth = newTextSize.Width;
                rawHeight = newTextSize.Height;

                using (var surface = SKSurface.Create(new SKImageInfo(rawWidth, rawHeight, SKColorType.Rgba8888, SKAlphaType.Premul)))
                {
                    var canvas = surface.Canvas;
                    canvas.Clear(SKColors.Transparent);

                    using (var paint = CreatePaint(style, FontPoint))
                    {
                        // Draw border/shadow if enabled
                        if (style.Border)
                        {
                            using (var borderPaint = CreatePaint(style, FontPoint))
                            {
                                borderPaint.Color = new SKColor(0, 0, 0, 64);
                                borderPaint.IsStroke = true;
                                borderPaint.StrokeWidth = 3;

                                string[] lines = text.Split('\n');
                                float y = FontPoint;
                                foreach (string line in lines)
                                {
                                    canvas.DrawText(line, 0, y, borderPaint);
                                    y += FontPoint * 1.2f;
                                }
                            }
                        }

                        // Draw text
                        string[] textLines = text.Split('\n');
                        float yPos = FontPoint;
                        foreach (string line in textLines)
                        {
                            canvas.DrawText(line, 0, yPos, paint);
                            yPos += FontPoint * 1.2f;
                        }
                    }

                    // Get pixels
                    using (var image = surface.Snapshot())
                    using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                    {
                        // Get raw pixel data directly
                        var pixmap = surface.PeekPixels();
                        if (pixmap != null)
                        {
                            rawPixels = new byte[rawWidth * rawHeight * 4];
                            System.Runtime.InteropServices.Marshal.Copy(pixmap.GetPixels(), rawPixels, 0, rawPixels.Length);
                        }
                    }
                }
            }

            return rawPixels != null;
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
                texture = new Texture2D(drawer.GraphicsDevice, rawWidth, rawHeight, false, SurfaceFormat.Color);
                texture.SetData(rawPixels);
                TextSize = newTextSize;
            }
        }

        public void Draw(Drawer id, RectangleF target, RectangleAlignment alignment, Scale scale, Color tint, float alpha)
        {
            lock (locker)
            {
                if (texture == null)
                    return;

                Rectangle sourceBounds = new Rectangle(0, 0, texture.Width, texture.Height);
                RectangleF sourceBoundsF = new RectangleF(0, 0, texture.Width, texture.Height);

                scaleFactor = 1;

                if (scale != Scale.Disallowed)
                {
                    scaleFactor = Maths.ScaleFactor(target, sourceBoundsF, FitType.FitBoth);
                }

                RectangleF bounds = Maths.FitBoxMaintainAspectRatio(target, sourceBoundsF, scaleFactor, alignment);
                offsetX = target.X - bounds.X;
                offsetY = target.Y - bounds.Y;
                id.Draw(texture, sourceBounds, bounds, tint, alpha);
            }
        }

        public void Draw(Drawer id, Rectangle target, RectangleAlignment alignment, Scale scale, Color tint, float alpha)
        {
            lock (locker)
            {
                if (texture == null)
                    return;

                Rectangle sourceBounds = new Rectangle(0, 0, texture.Width, texture.Height);

                scaleFactor = 1;

                if (scale != Scale.Disallowed)
                {
                    scaleFactor = Maths.ScaleFactor(target, sourceBounds, FitType.FitBoth);
                }

                Rectangle bounds = Maths.FitBoxMaintainAspectRatio(target, sourceBounds, scaleFactor, alignment);
                offsetX = target.X - bounds.X;
                offsetY = target.Y - bounds.Y;
                id.Draw(texture, sourceBounds, bounds, tint, alpha);
            }
        }

        public void Draw(Drawer id, Rectangle target, RectangleAlignment alignment, Scale scale, Color tint, Vector2 origin, float rotation)
        {
            lock (locker)
            {
                if (texture == null)
                    return;

                Rectangle sourceBounds = new Rectangle(0, 0, texture.Width, texture.Height);
                Rectangle sourceRotatedBounds = new Rectangle(0, 0, texture.Height, texture.Width);

                scaleFactor = 1;

                Rectangle boundsRotated = Maths.FitBoxMaintainAspectRatio(target, sourceRotatedBounds, scaleFactor, alignment);
                Rectangle bounds = new Rectangle(boundsRotated.X, boundsRotated.Y, boundsRotated.Height, boundsRotated.Width);

                offsetX = target.X - bounds.X;
                offsetY = target.Y - bounds.Y;
                id.Draw(texture, sourceBounds, bounds, tint, rotation, origin);
            }
        }

        public int HitCharacterIndex(Point point)
        {
            lock (locker)
            {
                float adjustedX = (point.X + offsetX) / scaleFactor;
                float adjustedY = (point.Y + offsetY) / scaleFactor;

                for (int i = 0; i < characterBounds.Count; i++)
                {
                    SKRect rect = characterBounds[i];
                    if (adjustedX >= rect.Left && adjustedX <= rect.Right &&
                        adjustedY >= rect.Top && adjustedY <= rect.Bottom)
                    {
                        // Check if we're past the midpoint of the character
                        if (adjustedX > (rect.Left + rect.Right) / 2)
                        {
                            return i + 1;
                        }
                        return i;
                    }
                }
                return characterBounds.Count;
            }
        }

        public Point CharacterPosition(int i)
        {
            lock (locker)
            {
                if (characterBounds.Count == 0)
                    return Point.Zero;

                if (i >= characterBounds.Count)
                {
                    SKRect last = characterBounds[characterBounds.Count - 1];
                    return new Point(
                        (int)((last.Right * scaleFactor) - offsetX),
                        (int)((last.Top * scaleFactor) - offsetY));
                }

                if (i >= 0 && i < characterBounds.Count)
                {
                    SKRect rect = characterBounds[i];
                    return new Point(
                        (int)((rect.Left * scaleFactor) - offsetX),
                        (int)((rect.Top * scaleFactor) - offsetY));
                }

                return Point.Zero;
            }
        }

        public Texture2D TextToTexture(GraphicsDevice graphicsDevice, Color textColor, string font, float point, string text)
        {
            Style tempStyle = new Style
            {
                TextColor = textColor,
                Font = font
            };

            TextRenderSkia renderer = new TextRenderSkia();
            renderer.CreateGeometry(0, (int)(point / pixelToPnt), text, tempStyle);

            if (renderer.rawPixels != null && renderer.rawWidth > 0 && renderer.rawHeight > 0)
            {
                Texture2D tex = new Texture2D(graphicsDevice, renderer.rawWidth, renderer.rawHeight, false, SurfaceFormat.Color);
                tex.SetData(renderer.rawPixels);
                return tex;
            }

            return null;
        }

        public void SavePNG(string filename)
        {
            if (texture != null)
            {
                using (var stream = System.IO.File.Create(filename + ".png"))
                {
                    texture.SaveAsPng(stream, texture.Width, texture.Height);
                }
            }
        }
    }
}
