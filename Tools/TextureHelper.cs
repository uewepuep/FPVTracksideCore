using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Tools
{
    public enum ChromaKeyColor
    {
        Red,
        Green, 
        Blue
    };

    public static class TextureHelper
    {
        public static Texture2D CreatePowerOf2Texture(GraphicsDevice graphicsDevice, Texture2D original)
        {
            int nextSizeUpX = 2;
            int nextSizeUpY = 2;
            while (nextSizeUpX < original.Bounds.Width) nextSizeUpX *= 2;
            while (nextSizeUpY < original.Bounds.Height) nextSizeUpY *= 2;

            Texture2D newTexture = new Texture2D(graphicsDevice, nextSizeUpX, nextSizeUpY);

            Color[] oldData = new Color[original.Bounds.Width * original.Bounds.Height];
            original.GetData<Color>(oldData);

            Color[] newData = new Color[nextSizeUpY * nextSizeUpX];

            Color nonDataColor = new Color(0, 0, 0, 0);

            for (int y = 0; y < nextSizeUpY; y++)
            {
                for (int x = 0; x < nextSizeUpX; x++)
                {
                    int newIndex = (y * nextSizeUpX) + x;
                    int oldIndex = (y * original.Bounds.Width) + x;

                    if (x < original.Bounds.Width && y < original.Bounds.Height)
                    {
                        newData[newIndex] = oldData[oldIndex];
                    }
                    else
                    {
                        newData[newIndex] = nonDataColor;
                    }
                }
            }

            newTexture.SetData<Color>(newData);
            return newTexture;
        }

        public static Texture2D LoadTexture(GraphicsDevice device, string filename)
        {
            // mac compatibility.
            filename = filename.Replace("\\", "/");

            using (FileStream fs = new FileStream(filename, FileMode.Open))
            {
                Texture2D texture = Texture2D.FromStream(device, fs);

                PreMultiplyAlpha(texture);

                return texture;
            }
        }
        
        public static void PreMultiplyAlpha(Texture2D texture)
        {
            Color[] data = new Color[texture.Width * texture.Height];
            texture.GetData(data);
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = Color.FromNonPremultiplied(data[i].ToVector4());
            }
            texture.SetData(data);
        }

        public static Texture2D CreateTextureFromColor(GraphicsDevice graphicsDevice, Color color)
        {
            Texture2D texture = new Texture2D(graphicsDevice, 8, 8);

            Color preMultiplied = Color.FromNonPremultiplied(color.ToVector4());

            Color[] colorArray = new Color[texture.Height * texture.Width];
            for (int i = 0; i < colorArray.Length; i++)
            {
                colorArray[i] = preMultiplied;
            }

            texture.SetData<Color>(colorArray);
            return texture;
        }

        public static Texture2D CreateTintTexture(Texture2D texture, Color tint)
        {
            Vector4 tintV = tint.ToVector4();

            Texture2D tinted = new Texture2D(texture.GraphicsDevice, texture.Width, texture.Height);

            Color[] sourceData = new Color[texture.Height * texture.Width];
            texture.GetData<Color>(sourceData);

            Color[] destData = new Color[texture.Height * texture.Width];
            for (int i = 0; i < sourceData.Length; i++)
            {
                Color source = sourceData[i];

                destData[i] = new Color(source.ToVector4() * tintV);
            }

            tinted.SetData<Color>(destData);
            return tinted;
        }

        public static Texture2D FlipHorizontal(Texture2D original)
        {
            Texture2D flipped = new Texture2D(original.GraphicsDevice, original.Width, original.Height, false, original.Format);

            Color[] sourceData = new Color[original.Height * original.Width];
            original.GetData<Color>(sourceData);

            Color[] destData = new Color[original.Height * original.Width];

            for (int y = 0; y < original.Height; y++)
            {
                for (int x = 0; x < original.Width; x++)
                {
                    int i = y * original.Width + x;
                    int j = y * original.Width + (original.Width - (x + 1));
                    destData[j] = sourceData[i];
                }
            }

            flipped.SetData<Color>(destData);
            return flipped;
        }

        public static Texture2D FlipVertical(Texture2D original)
        {
            Texture2D flipped = new Texture2D(original.GraphicsDevice, original.Width, original.Height, false, original.Format);

            Color[] sourceData = new Color[original.Height * original.Width];
            original.GetData<Color>(sourceData);

            Color[] destData = new Color[original.Height * original.Width];

            for (int y = 0; y < original.Height; y++)
            {
                for (int x = 0; x < original.Width; x++)
                {
                    int i = y * original.Width + x;
                    int j = ((original.Height - 1) - y) * original.Width + x;
                    destData[j] = sourceData[i];
                }
            }

            flipped.SetData<Color>(destData);
            return flipped;
        }

        public static Texture2D Clone(this Texture2D texture)
        {
            if (texture == null)
                return null;

            Color[] data = new Color[texture.Width * texture.Height];
            texture.GetData(data);
            Texture2D newTexture = new Texture2D(texture.GraphicsDevice, texture.Width, texture.Height);
            newTexture.SetData(data);
            return newTexture;
        }


        public static void SaveAs(this Texture2D texture, string filename, bool mirrored = false, bool flipped = false)
        {
            if (texture == null)
            {
                return;
            }

            if (File.Exists(filename))
                File.Delete(filename);

            Rectangle src = new Rectangle(0, 0, texture.Width, texture.Height);
            Rectangle dest = new Rectangle(0, 0, texture.Width, texture.Height);

            Texture2D cloned;

            if (flipped)
                src = src.Flip(texture.Height);

            if (mirrored)
                src = src.Mirror(texture.Width);

            cloned = ResizeTexture(texture, src, dest);

            using (FileStream fs = new FileStream(filename, FileMode.CreateNew))
            {
                if (filename.EndsWith(".png"))
                {
                    cloned.SaveAsPng(fs, cloned.Width, cloned.Height);
                }

                if (filename.EndsWith(".jpg"))
                {
                    cloned.SaveAsJpeg(fs, cloned.Width, cloned.Height);
                }
            }
            cloned.Dispose();
        }

        public static Texture2D GetEmbeddedTexture(GraphicsDevice graphics, System.Reflection.Assembly assembly, string name)
        {
            try
            {
                using (Stream resourceStream = assembly.GetManifestResourceStream(name))
                {
                    if (resourceStream != null)
                    {
                        Texture2D t = Texture2D.FromStream(graphics, resourceStream);
                        return t;
                    }
                    return null;
                }
            }
            catch (Exception e)
            {
                Tools.Logger.AllLog.LogException(e, e);

                string[] names = assembly.GetManifestResourceNames();
                Tools.Logger.AllLog.Log(e, "Invalid resource name. Try:", string.Join(",", names));
                return null;
            }
        }
        
        
        public static Texture2D ResizeTexture(Texture2D sourceImage, int width, int height)
        {
            return ResizeTexture(sourceImage, new Rectangle(0, 0, sourceImage.Width, sourceImage.Height), new Rectangle(0, 0, width, height));
        }

        public static Texture2D ResizeTexture(Texture2D sourceImage, Rectangle sourceRectangle, Rectangle destinationRectangle)
        {
            RenderTarget2D renderTarget = new RenderTarget2D(sourceImage.GraphicsDevice, destinationRectangle.Width, destinationRectangle.Height, false, SurfaceFormat.Color, DepthFormat.None);

            try
            {
                sourceImage.GraphicsDevice.SetRenderTarget(renderTarget);
                sourceImage.GraphicsDevice.Clear(Color.Transparent);
                using (SpriteBatch batch = new SpriteBatch(sourceImage.GraphicsDevice))
                {
                    batch.Begin();
                    batch.Draw(sourceImage, destinationRectangle, sourceRectangle, Color.White);
                    batch.End();
                }
            }
            finally
            {
                sourceImage.GraphicsDevice.SetRenderTarget(null);
            }
            return renderTarget;
        }

        public static Texture2D LoadTextureResize(GraphicsDevice graphicsDevice, string sourceImagePath, int width, int height)
        {
            Texture2D sourceImage = LoadTexture(graphicsDevice, sourceImagePath);
            return ResizeTexture(sourceImage, width, height);
        }

        public static void ChromaKey(Texture2D texture, ref Color[] data, ref Texture2D replacementTexture, ChromaKeyColor chromaKeyColor, byte limit)
        {
            if (texture == null)
                return;

            if (replacementTexture == null || texture.Width != replacementTexture.Width || texture.Height != replacementTexture.Height)
            {
                replacementTexture?.Dispose();
                replacementTexture = new Texture2D(texture.GraphicsDevice, texture.Width, texture.Height, false, SurfaceFormat.Bgra32);
                data = new Color[texture.Width * texture.Height];
            }

            texture.GetData(data);

            ChromaKey(data, chromaKeyColor, limit); 

            replacementTexture.SetData(data);
        }

        public static void ChromaKey(Color[] data, ChromaKeyColor chromaKeyColor, byte limit)
        {
            switch (chromaKeyColor)
            {
                case ChromaKeyColor.Red:
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (data[i].R > data[i].B + limit &&
                            data[i].R > data[i].G + limit)
                        {
                            data[i] = Color.Transparent;
                        }
                        else
                        {
                            data[i].A = 255;
                        }
                    }
                    break;
                case ChromaKeyColor.Green:
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (data[i].G > data[i].B + limit &&
                            data[i].G > data[i].R + limit)
                        {
                            data[i] = Color.Transparent;
                        }
                        else
                        {
                            data[i].A = 255;
                        }
                    }
                    break;
                case ChromaKeyColor.Blue:
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (data[i].B > data[i].G + limit &&
                            data[i].B > data[i].R + limit)
                        {
                            data[i] = Color.Transparent;
                        }
                        else
                        {
                            data[i].A = 255;
                        }
                    }
                    break;
            }
        }

        public static void TextureCombiner(GraphicsDevice device, int height, IEnumerable<Texture2D> ts, out Rectangle[] bounds, out Texture2D texture)
        {
            Texture2D[] textures = ts.ToArray();
            List<Rectangle> rects = new List<Rectangle>();

            int totalArea = textures.Select(r => r.Width * height).Sum();
            double eachSide = Math.Sqrt(totalArea);

            int nextSizeUp = 2;
            while (nextSizeUp < eachSide) nextSizeUp *= 2;

            RenderTarget2D renderTarget = new RenderTarget2D(device, nextSizeUp, nextSizeUp);

            device.SetRenderTarget(renderTarget);
            device.Clear(Color.Transparent);

            using (SpriteBatch batch = new SpriteBatch(device))
            {
                batch.Begin();

                int x = 0, y = 0;


                foreach (Texture2D tex in textures)
                {
                    Rectangle destinationRectangle = new Rectangle(x, y, tex.Width, tex.Height);

                    if (destinationRectangle.Right > renderTarget.Width)
                    {
                        y += height;
                        x = 0;
                        destinationRectangle = new Rectangle(x, y, tex.Width, tex.Height);
                    }
                    {
                        batch.Draw(tex, destinationRectangle, new Rectangle(0, 0, tex.Width, tex.Height), Color.White);
                    }

                    x += tex.Width;

                    rects.Add(destinationRectangle);
                }

                batch.End();
            }

            device.SetRenderTarget(null);

            bounds = rects.ToArray();
            texture = renderTarget;
        }
    }
}
