using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Tools;

namespace Composition.Nodes
{
    public class ImageNode : AspectNode
    {
        public Texture2D Texture
        {
            get
            {
                return texture;
            }
            set
            {
                if (value != null)
                {
                    texture = value;
                    RequestRedraw();
                }
                else
                {
                    texture = value;
                }
            }
        }

        protected Texture2D texture;

        public RectangleF RelativeSourceBounds { get; set; }
        public Rectangle SourceBounds { get; set; }

        public string FileName { get; private set; }
        public Color Tint { get; set; }

        protected bool sharedTexture;

        public bool CanScale { get; set; }

        public bool CropToFit { get; set; }

        public bool ReloadFromFile { get; set; }

        public ImageNode()
        {
            ReloadFromFile = false;
            CanScale = true;
            CropToFit = false;
            sharedTexture = false;
            Tint = Color.White;
            RelativeSourceBounds = new RectangleF(0, 0, 1, 1);
            SourceBounds = new Rectangle();
        }

        public ImageNode(Texture2D texture)
            : this()
        {
            sharedTexture = true;
            Texture = texture;
            UpdateAspectRatioFromTexture();
        }

        public ImageNode(string filename)
            :this()
        {
            FileName = filename;
        }

        public ImageNode(string filename, Color tint)
           : this()
        {
            Tint = tint;
            FileName = filename;
        }

        public override void Dispose()
        {
            if (!sharedTexture && texture != null)
            {
                texture.Dispose();
                texture = null;
            }
            base.Dispose();
        }

        public override RectangleF CalculateRelativeBounds(RectangleF parentPosition)
        {
            RectangleF bounds = base.CalculateRelativeBounds(parentPosition);
            if (!CanScale && texture != null)
            {
                return Maths.FitBoxMaintainAspectRatio(BaseBoundsF, new RectangleF(0, 0, texture.Width, texture.Height), 1, Alignment);
            }
            return bounds;
        }

        public void UpdateAspectRatioFromTexture()
        {
            if (Texture != null)
            {
                float oldAspect = AspectRatio;
                SetAspectRatio(Texture.GetSize());
                if (oldAspect != AspectRatio)
                {
                    RequestLayout();
                }
            }
        }

        public override void Layout(RectangleF parentBounds)
        {
            base.Layout(parentBounds);

            Texture2D temp = texture;
            if (temp != null)
            {
                RectangleF sourceBounds = new RectangleF();
                sourceBounds.X = (int)(temp.Width * RelativeSourceBounds.X);
                sourceBounds.Y = (int)(temp.Height * RelativeSourceBounds.Y);
                sourceBounds.Width = (int)(temp.Width * RelativeSourceBounds.Width);
                sourceBounds.Height = (int)(temp.Height * RelativeSourceBounds.Height);

                if (CropToFit)
                {
                    sourceBounds = Maths.FitBoxMaintainAspectRatio(sourceBounds, BaseBoundsF, Alignment, FitType);
                }
                SourceBounds = sourceBounds.ToRectangle();
            }
        }

        public void FlipX()
        {
            RelativeSourceBounds = new RectangleF(1, 0, -1, 1);
        }

        public void FlipY()
        {
            RelativeSourceBounds = new RectangleF(0, 0, 1, 1);
        }

        public void LoadImage(Drawer id)
        {
            try
            {
                texture = id.TextureCache.GetTextureFromFilename(FileName, ReloadFromFile);
                SourceBounds = new Rectangle(0, 0, Texture.Width, Texture.Height);
                sharedTexture = true;
                UpdateAspectRatioFromTexture();
                ReloadFromFile = false;
            }
            catch
            {
                FileName = null;
                texture = null;
            }
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            DebugTimer.DebugStartTime(this);

            if ((texture == null || ReloadFromFile) && !string.IsNullOrEmpty(FileName))
            {
                LoadImage(id);
            }

            if (texture != null)
            {
                float alpha = parentAlpha * Alpha;
                if (Tint.A != 255)
                {
                    alpha *= Tint.A / 255.0f;
                }

                try
                {
                    id.Draw(texture, SourceBounds, Bounds, Tint, alpha);
                }
                catch
                {
                    if (!sharedTexture && texture != null)
                    {
                        texture.Dispose();
                    }
                    Texture = null;
                }
            }

            DebugTimer.DebugEndTime(this);

            base.Draw(id, parentAlpha);
        }

        public void SetFilename(string filename)
        {
            if (!sharedTexture && texture != null)
            {
                texture.Dispose();
            }

            texture = null;
            FileName = filename;
        }
    }
}
