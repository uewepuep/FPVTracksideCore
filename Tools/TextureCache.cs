using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools
{
    public class TextureCache : IDisposable
    {
        private Dictionary<string, Texture2D> stringToTexture;

        public GraphicsDevice GraphicsDevice { get; private set; }

        public TextureCache(GraphicsDevice device)
        {
            GraphicsDevice = device;
            stringToTexture = new Dictionary<string, Texture2D>();
        }

        public void Dispose()
        {
            foreach (Texture2D texture2D in stringToTexture.Values)
            {
                texture2D.Dispose();
            }
            stringToTexture.Clear();
        }

        public Texture2D GetTextureFromColor(Color color)
        {
            if (stringToTexture == null)
            {
                stringToTexture = new Dictionary<string, Texture2D>();
            }

            string colorString = color.ToString();

            Texture2D texture;
            lock (stringToTexture)
            {
                if (!stringToTexture.TryGetValue(colorString, out texture))
                {
                    texture = TextureHelper.CreateTextureFromColor(GraphicsDevice, color);
                    stringToTexture.Add(colorString, texture);
                }
            }
            return texture;
        }

        public Texture2D GetTextureFromFilename(string filename, bool forceRefresh)
        {
            return GetTextureFromFilename(filename, Color.Transparent, forceRefresh);
        }

        public Texture2D GetTextureFromFilename(string filename, Color fallback, bool forceRefresh)
        {
            if (stringToTexture == null)
            {
                stringToTexture = new Dictionary<string, Texture2D>();
            }

            // mac compatibility.
            filename = filename.Replace("\\", "/");

            Texture2D texture;
            lock (stringToTexture)
            {
                if (forceRefresh)
                {
                    if (stringToTexture.ContainsKey(filename))
                    {
                        stringToTexture.Remove(filename);
                    }
                }

                if (!stringToTexture.TryGetValue(filename, out texture))
                {
                    try
                    {
                        texture = TextureHelper.LoadTexture(GraphicsDevice, filename);
                        if (texture != null)
                        {
                            stringToTexture.Add(filename, texture);
                        }
                    }
                    catch
                    {
                        return GetTextureFromColor(fallback);
                    }
                }
            }
            return texture;
        }
    }
}
