using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace WindowsPlatform
{
    public static class WinTextureHelper
    {

        public static Texture2D TextToTexture(GraphicsDevice graphics, Color textColor, string fontFamily, float fontPoint, string text, int maxSize = 2000)
        {
            System.Windows.Media.DrawingVisual drawingVisual = new System.Windows.Media.DrawingVisual();
            System.Windows.Media.FontFamily ff = new System.Windows.Media.FontFamily(fontFamily);
            System.Windows.Media.Typeface tf = new System.Windows.Media.Typeface(ff, System.Windows.FontStyles.Normal, System.Windows.FontWeights.Medium, System.Windows.FontStretches.Normal);

            System.Windows.Media.Color color = System.Windows.Media.Color.FromArgb(textColor.A, textColor.R, textColor.G, textColor.B);
            System.Windows.Media.SolidColorBrush brush = new System.Windows.Media.SolidColorBrush(color);
            System.Globalization.CultureInfo cultureInfo = System.Globalization.CultureInfo.GetCultureInfo("en-us");

#pragma warning disable CS0618 // Type or member is obsolete
            System.Windows.Media.FormattedText ft = new System.Windows.Media.FormattedText(text,
                                                    cultureInfo,
                                                    System.Windows.FlowDirection.LeftToRight,
                                                    tf,
                                                    fontPoint,
                                                    brush);
#pragma warning restore CS0618 // Type or member is obsolete

            System.Drawing.Size size;
            using (System.Windows.Media.DrawingContext drawingContext = drawingVisual.RenderOpen())
            {
                drawingContext.DrawText(ft, new System.Windows.Point(0, 0));

                size = new System.Drawing.Size((int)Math.Ceiling(ft.Width), (int)Math.Ceiling(ft.Height));

                if (size.Width > maxSize)
                {
                    float ratio = maxSize / (float)size.Width;
                    size = new System.Drawing.Size(maxSize, (int)(size.Height * ratio));
                }

                if (size.Height > maxSize)
                {
                    float ratio = maxSize / (float)size.Height;
                    size = new System.Drawing.Size((int)(size.Width * ratio), maxSize);
                }
            }

            return WPFtoTexture(graphics, drawingVisual, size.Width, size.Height);
        }

        public static Texture2D WPFtoTextureSlow(GraphicsDevice graphics, System.Windows.Media.DrawingVisual drawingVisual, int width, int height)
        {
            System.Windows.Media.Imaging.RenderTargetBitmap bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(drawingVisual);

            MemoryStream stream = new MemoryStream();

            System.Windows.Media.Imaging.PngBitmapEncoder png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            png.Save(stream);

            Texture2D texture = Texture2D.FromStream(graphics, stream);
            return texture;
        }

        public static Texture2D WPFtoTexture(GraphicsDevice graphics, System.Windows.Media.DrawingVisual drawingVisual, int width, int height)
        {
            System.Windows.Media.Imaging.RenderTargetBitmap renderTargetBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            renderTargetBitmap.Render(drawingVisual);

            return WPFtoTexture(graphics, renderTargetBitmap);
        }


        public static Texture2D WPFtoTexture(GraphicsDevice graphics, System.Windows.Media.Imaging.RenderTargetBitmap renderTargetBitmap)
        {
            int stride = renderTargetBitmap.PixelWidth * (renderTargetBitmap.Format.BitsPerPixel / 8);
            byte[] pixels = new byte[renderTargetBitmap.PixelHeight * stride];

            renderTargetBitmap.CopyPixels(pixels, stride, 0);

            Texture2D texture = WPFtoTexture(graphics, pixels, renderTargetBitmap.PixelWidth, renderTargetBitmap.PixelHeight);

            return texture;
        }

        public static Texture2D WPFtoTexture(GraphicsDevice graphics, byte[] pixels, int width, int height)
        {
            Texture2D texture = new Texture2D(graphics, width, height, false, SurfaceFormat.Bgra32);
#if DEBUG
            Random r = new Random();
            Color color = new Color((float)r.NextDouble(), (float)r.NextDouble(), (float)r.NextDouble());
            Color[] colors = new Color[width * height];

            for(int i = 0; i < width; i++)
            {

            }
#endif


            texture.SetData(pixels);

            return texture;
        }

        public static Texture2D GDItoTexture(GraphicsDevice graphics, System.Drawing.Image image)
        {
            MemoryStream stream = new MemoryStream();
            image.Save(stream, ImageFormat.Png);

            Texture2D texture = Texture2D.FromStream(graphics, stream);

            //BlendState BlendColorBlendState = new BlendState
            //{
            //    ColorDestinationBlend = Blend.Zero,
            //    ColorWriteChannels = ColorWriteChannels.Red | ColorWriteChannels.Green | ColorWriteChannels.Blue,
            //    AlphaDestinationBlend = Blend.Zero,
            //    AlphaSourceBlend = Blend.SourceAlpha,
            //    ColorSourceBlend = Blend.SourceAlpha
            //};

            //BlendState BlendAlphaBlendState = new BlendState
            //{
            //    ColorWriteChannels = ColorWriteChannels.Alpha,
            //    AlphaDestinationBlend = Blend.Zero,
            //    ColorDestinationBlend = Blend.Zero,
            //    AlphaSourceBlend = Blend.One,
            //    ColorSourceBlend = Blend.One
            //};

            //using (SpriteBatch sb = new SpriteBatch(graphics))
            //{
            //    // Setup a render target to hold our final texture which will have premulitplied alpha values
            //    using (RenderTarget2D renderTarget = new RenderTarget2D(graphics, texture.Width, texture.Height))
            //    {
            //        Viewport viewportBackup = graphics.Viewport;
            //        graphics.SetRenderTarget(renderTarget);
            //        graphics.Clear(Color.Black);

            //        // Multiply each color by the source alpha, and write in just the color values into the final texture
            //        sb.Begin(SpriteSortMode.Immediate, BlendColorBlendState);
            //        sb.Draw(texture, texture.Bounds, Color.White);
            //        sb.End();

            //        // Now copy over the alpha values from the source texture to the final one, without multiplying them
            //        sb.Begin(SpriteSortMode.Immediate, BlendAlphaBlendState);
            //        sb.Draw(texture, texture.Bounds, Color.White);
            //        sb.End();

            //        // Release the GPU back to drawing to the screen
            //        graphics.SetRenderTarget(null);
            //        graphics.Viewport = viewportBackup;

            //        // Store data from render target because the RenderTarget2D is volatile
            //        Color[] data = new Color[texture.Width * texture.Height];
            //        renderTarget.GetData(data);

            //        // Unset texture from graphic device and set modified data back to it
            //        graphics.Textures[0] = null;
            //        texture.SetData(data);
            //    }
            //}


            return texture;
        }

        public static System.Drawing.Color ToGDI(this Microsoft.Xna.Framework.Color c)
        {
            return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
        }

        public static byte[] GetBGRAResize(string filename, int width, int height)
        {
            byte[] data;
            using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                using (System.Drawing.Image source = System.Drawing.Image.FromStream(fs))
                {
                    System.Drawing.Bitmap dest = new System.Drawing.Bitmap(width, height);
                    System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(dest);
                    graphics.DrawImage(source, new System.Drawing.Rectangle(0, 0, width, height));

                    data = new byte[width * height * 4];

                    for (int x = 0; x < width; x++)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            System.Drawing.Color c = dest.GetPixel(x, y);

                            int pos = ((y * width) + x) * 4;
                            data[pos + 0] = c.B;
                            data[pos + 1] = c.G;
                            data[pos + 2] = c.R;
                        }
                    }

                    graphics.Dispose();
                    dest.Dispose();
                    source.Dispose();
                }
            }
            return data;
        }
    }
}
