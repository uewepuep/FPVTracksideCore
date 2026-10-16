using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
using System;
using System.Collections.Generic;
using Tools;

namespace Composition.Text
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
        private const float lineHeightMultiplier = 1.2f;

        private Texture2D texture;

        private float scaleFactor;

        private string text;
        private Style style;

        private object locker;

        private float offsetX;
        private float offsetY;

        // Worked out on demand, only text editing needs them
        private List<SKRect> characterBounds;

        private SKTypeface typeface;

        public TextRenderSkia()
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
                characterBounds = null;
                typeface = GetTypeface(style, this.text);

                string[] textLines = this.text.Split('\n');

                using (SKPaint paint = CreatePaint(style, FontPoint))
                {
                    float maxWidth = MeasureWidth(paint, textLines);

                    // Scale down to fit the width
                    if (width > 0 && maxWidth > width)
                    {
                        FontPoint *= width / maxWidth;
                        paint.TextSize = FontPoint;
                        maxWidth = MeasureWidth(paint, textLines);
                    }

                    newTextSize = new Size(
                        Math.Max(1, (int)Math.Ceiling(maxWidth)),
                        Math.Max(1, (int)Math.Ceiling(textLines.Length * FontPoint * lineHeightMultiplier))
                    );
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

        private static float MeasureWidth(SKPaint paint, string[] textLines)
        {
            float maxWidth = 0;
            foreach (string line in textLines)
            {
                SKRect lineBounds = new SKRect();
                paint.MeasureText(line, ref lineBounds);
                maxWidth = Math.Max(maxWidth, lineBounds.Right);
            }
            return maxWidth;
        }

        private SKPaint CreatePaint(Style style, float fontSize)
        {
            return new SKPaint
            {
                TextSize = fontSize,
                IsAntialias = true,
                Color = new SKColor(style.TextColor.R, style.TextColor.G, style.TextColor.B, style.TextColor.A),
                IsStroke = false,
                TextAlign = SKTextAlign.Left,
                Typeface = typeface
            };
        }

        // Typefaces by font / bold / italic, shared by every renderer as looking one up is slow
        private static Dictionary<(string, bool, bool), SKTypeface> typefaces = new Dictionary<(string, bool, bool), SKTypeface>();

        // Like the WPF renderer: the style's font (but Normal weight, Medium looks heavy here). If that font can't draw
        // some of the text (eg CJK), use one the system says can.
        private static SKTypeface GetTypeface(Style style, string text)
        {
            SKFontStyleWeight weight = style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
            SKFontStyleSlant slant = style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;

            SKTypeface typeface;
            lock (typefaces)
            {
                var key = (style.Font, style.Bold, style.Italic);
                if (!typefaces.TryGetValue(key, out typeface))
                {
                    typeface = SKTypeface.FromFamilyName(style.Font, weight, SKFontStyleWidth.Normal, slant) ?? SKTypeface.Default;
                    typefaces[key] = typeface;
                }
            }

            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c) || char.IsSurrogate(c))
                    continue;

                if (typeface.GetGlyph(c) == 0)
                {
                    SKTypeface fallback = SKFontManager.Default.MatchCharacter(null, weight, SKFontStyleWidth.Normal, slant, null, c);
                    if (fallback != null)
                    {
                        return fallback;
                    }
                    break;
                }
            }

            return typeface;
        }

        private List<SKRect> GetCharacterBounds()
        {
            if (characterBounds != null)
                return characterBounds;

            List<SKRect> bounds = new List<SKRect>();
            if (!string.IsNullOrEmpty(text) && style != null)
            {
                using (SKPaint paint = CreatePaint(style, FontPoint))
                {
                    float y = 0;
                    foreach (string line in text.Split('\n'))
                    {
                        float x = 0;
                        foreach (char c in line)
                        {
                            float charWidth = paint.MeasureText(c.ToString());
                            bounds.Add(new SKRect(x, y, x + charWidth, y + FontPoint));
                            x += charWidth;
                        }
                        // Add newline character bounds
                        bounds.Add(new SKRect(x, y, x, y + FontPoint));

                        y += FontPoint * lineHeightMultiplier;
                    }
                }
            }
            characterBounds = bounds;
            return bounds;
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

                using (SKSurface surface = SKSurface.Create(new SKImageInfo(rawWidth, rawHeight, SKColorType.Rgba8888, SKAlphaType.Premul)))
                {
                    SKCanvas canvas = surface.Canvas;
                    canvas.Clear(SKColors.Transparent);

                    using (SKPaint paint = CreatePaint(style, FontPoint))
                    {
                        // Draw border/shadow if enabled
                        if (style.Border)
                        {
                            using (SKPaint borderPaint = CreatePaint(style, FontPoint))
                            {
                                borderPaint.Color = new SKColor(0, 0, 0, 64);
                                borderPaint.IsStroke = true;
                                borderPaint.StrokeWidth = 3;

                                string[] lines = text.Split('\n');
                                float y = FontPoint;
                                foreach (string line in lines)
                                {
                                    canvas.DrawText(line, 0, y, borderPaint);
                                    y += FontPoint * lineHeightMultiplier;
                                }
                            }
                        }

                        // Draw text
                        string[] textLines = text.Split('\n');
                        float yPos = FontPoint;
                        foreach (string line in textLines)
                        {
                            canvas.DrawText(line, 0, yPos, paint);
                            yPos += FontPoint * lineHeightMultiplier;
                        }
                    }

                    // Get pixels
                    SKPixmap pixmap = surface.PeekPixels();
                    if (pixmap != null)
                    {
                        rawPixels = new byte[rawWidth * rawHeight * 4];
                        System.Runtime.InteropServices.Marshal.Copy(pixmap.GetPixels(), rawPixels, 0, rawPixels.Length);
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
                List<SKRect> characterBounds = GetCharacterBounds();
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
                            return i;
                        }
                        return i - 1;
                    }
                }
                return characterBounds.Count - 1; // -1 as it's asking for the last char index.
            }
        }

        public Point CharacterPosition(int i)
        {
            lock (locker)
            {
                List<SKRect> characterBounds = GetCharacterBounds();
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
                using (System.IO.Stream stream = System.IO.File.Create(filename + ".png"))
                {
                    texture.SaveAsPng(stream, texture.Width, texture.Height);
                }
            }
        }
    }
}
