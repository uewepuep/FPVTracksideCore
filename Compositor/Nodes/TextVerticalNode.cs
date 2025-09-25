using Composition.Text;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class TextVerticalNode : TextNode
    {
        private float rotation;

        public TextVerticalNode(string text, Color textColor) 
            : base(text, textColor)
        {
            rotation = MathHelper.ToRadians(-90);
        }

        public override void UpdateGeometry()
        {
            ITextRenderer textRenderer = this.textRenderer;
            if (textRenderer == null)
                return;

            if (needsUpdate == UpdateTypes.Geometry || needsUpdate == UpdateTypes.Size)
            {
                needsUpdate = UpdateTypes.Texture;

                int newHeight = height;

                if (height == 0 || !IsAnimatingSize())
                {
                    newHeight = Bounds.Width;
                }

                if (textRenderer == null)
                    return;

                int width = -1;

                if (textRenderer == null)
                    return;

                textRenderer.CreateGeometry(width, newHeight, DrawingText, Style);
                height = newHeight;
            }
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            DebugTimer.DebugStartTime(this);

            try
            {
                ITextRenderer textRenderer = this.textRenderer;
                if (textRenderer == null)
                {
                    this.textRenderer = textRenderer = CompositorLayer.LayerStack.PlatformTools.CreateTextRenderer();
                }

                bool isAnimatingSize = IsAnimatingSize();

                if (string.IsNullOrEmpty(DrawingText) || Bounds.Height == 0)
                {
                    return;
                }

                bool updateGeomety = false;

                if (needsUpdate == UpdateTypes.Geometry)
                {
                    updateGeomety = true;
                }
                else if (needsUpdate == UpdateTypes.Size && !IsAnimatingInvisiblity() && !isAnimatingSize && Alpha > 0)
                {
                    updateGeomety = true;
                }

                if (updateGeomety)
                {
                    UpdateGeometry();

                    if (textRenderer.CanCreateTextures)
                    {
                        id.PreProcess(this);
                    }
                    else
                    {
                        textRenderer.Reset();
                        RequestRedraw();
                    }
                }

                Scale scale;
                if (CanScale)
                {
                    if (isAnimatingSize /*|| needsGeometryUpdate || needsTextureUpdate */)
                    {
                        scale = Composition.Text.Scale.Force;
                    }
                    else
                    {
                        scale = Composition.Text.Scale.Can;
                    }
                }
                else
                {
                    scale = Composition.Text.Scale.Disallowed;
                }

                // Don't draw if geometry is wrong.
                if (needsUpdate != UpdateTypes.Geometry)
                {
                    textRenderer.Draw(id, Bounds, Alignment, scale, Tint, Vector2.Zero, rotation);
                }
            }
            catch
            {
                textRenderer?.Reset();
            }
            DebugTimer.DebugEndTime(this);

            // Has to be done because drawing children normally happens in base.
            DrawChildren(id, parentAlpha);

            if (!OverrideHeight.HasValue && needsUpdate == UpdateTypes.None && height != Bounds.Height)
            {
                needsUpdate = UpdateTypes.Size;
            }
        }
    }
}
