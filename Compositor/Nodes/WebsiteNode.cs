using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class WebsiteNode : ImageNode
    {
        public Uri URL { get; set; }

        public DateTime UpdateTime { get; private set; }

        public System.IO.FileInfo CSSFile { get; private set; }

        public bool IsGenerating { get { return generation != null; } }

        private Process generation;

        private string tempFile;

        public WebsiteNode(System.IO.FileInfo cssFile)
        {
            tempFile = "chat.png";
            Alignment = RectangleAlignment.TopLeft;
            CSSFile = cssFile;
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            DebugTimer.DebugStartTime(this);

            if (Bounds.Width > 0 && Bounds.Height > 0 && !IsAnimatingSize())
            {
                if (Texture == null)
                {
                    Generate();
                }
                else if (Texture.Width != BaseBounds.Width || Texture.Height != BaseBounds.Height)
                {
                    Generate();
                }
            }

            if (generation != null && generation.HasExited)
            {
                generation = null;
                if (Texture != null)
                {
                    Texture.Dispose();
                    Texture = null;
                }

                Texture = TextureHelper.LoadTexture(id.GraphicsDevice, tempFile);

                UpdateTime = DateTime.Now;
                UpdateAspectRatioFromTexture();
                SourceBounds = new Rectangle(0, 0, Texture.Width, Texture.Height);
            }

            if (Texture != null)
            {
                float alpha = parentAlpha * Alpha;
                if (Tint.A != 255)
                {
                    alpha *= Tint.A / 255.0f;
                }

                int width = Math.Min(SourceBounds.Width, BaseBounds.Width);
                int height = Math.Min(SourceBounds.Height, BaseBounds.Height);
                int x = BaseBounds.X;
                int y = BaseBounds.Y;


                if (width < BaseBounds.Width)
                {
                    x = BaseBounds.X + ((BaseBounds.Width - SourceBounds.Width) / 2);
                }

                if (height < BaseBounds.Height)
                {
                    y = BaseBounds.Y + ((BaseBounds.Height - SourceBounds.Height) / 2);
                }

                id.Draw(Texture, new Rectangle(0, 0, width,height), new Rectangle(x, BaseBounds.Y, width, height), Tint, alpha);
            }
            DebugTimer.DebugEndTime(this);
        }

        public void Generate()
        {
            if (generation == null)
            {
                generation = Web2BMPWrapper(tempFile, URL, CSSFile.FullName, BaseBounds.Width, BaseBounds.Height);
            }
        }

        private static Process Web2BMPWrapper(string output, Uri url, string cssFile, int width, int height)
        {
            try
            {
                string arguments = "\"" + output + "\" \"" + url.AbsoluteUri + "\" \"" + cssFile + "\" \"" + width + "\" \"" + height + "\"";

                Process p = Process.Start("web2bmp.exe", arguments);
                p.PriorityClass = ProcessPriorityClass.BelowNormal;

                Logger.UI.Log(p, "web2bmp.exe", arguments);

                return p;
            }
            catch (Exception e)
            {
                Logger.UI.LogException(null, e);
                return null;
            }
        }
        
    }
}
