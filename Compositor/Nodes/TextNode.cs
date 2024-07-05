using Composition.Input;
using Composition.Layers;
using Composition.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class TextNode : Node, IPreProcessable
    {
        protected string text;
        public string Text
        {
            get { return text; }
            set
            {
                //Debug.Assert(value != null);

                if (text != value)
                {
                    text = value;
                    needsUpdate = UpdateTypes.Geometry;
                }
            }
        }

        public Color Tint { get; set; }

        public virtual string DrawingText { get { return text; } }

        protected enum UpdateTypes
        {
            None,
            Geometry,
            Size,
            Texture
        }

        protected UpdateTypes needsUpdate;

        public Style Style { get; private set; }

        public int? OverrideHeight { get; set; }

        protected ITextRenderer textRenderer;

        private int height;
        public RectangleAlignment Alignment { get; set; }

        public bool CanScale { get; set; }

        public TextNode(string text, Color textColor)
        {
            Alignment = RectangleAlignment.Center;
            Style = new Style();

            Style.TextColor = textColor;

            Tint = Color.White;

            Text = text;
            needsUpdate = UpdateTypes.Geometry;
            height = 0;
            CanScale = true;
        }

        public override void Dispose()
        {
            if (textRenderer != null)
            {
                textRenderer.Dispose();
                textRenderer = null;
            }

            base.Dispose();
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
                    // Use floats when animating, ints when static
                    if (isAnimatingSize)
                    {
                        textRenderer.Draw(id, BoundsF, Alignment, scale, Tint, parentAlpha * Alpha);
                    }
                    else
                    {
                        textRenderer.Draw(id, Bounds, Alignment, scale, Tint, parentAlpha * Alpha);
                    }
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


        public virtual void PreProcess(Drawer id)
        {
            ITextRenderer textRenderer = this.textRenderer;
            if (textRenderer != null)
            {
                if (needsUpdate == UpdateTypes.Texture)
                {
                    needsUpdate = UpdateTypes.None;
                    textRenderer.CreateTextures(id);
                    if (id.CanMultiThread)
                    {
                        RequestRedraw();
                    }
                }
            }
        }

        public void UpdateGeometry()
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
                    newHeight = Bounds.Height;
                    if (OverrideHeight.HasValue)
                    {
                        newHeight = OverrideHeight.Value;
                    }
                }

                if (textRenderer == null)
                    return;

                int width = -1;
                if (OverrideHeight.HasValue && !CanScale)
                {
                    width = Bounds.Width;
                }

                if (textRenderer == null)
                    return;

                textRenderer.CreateGeometry(width, newHeight, DrawingText, Style);
                height = newHeight;
            }
        }

        public override void Layout(RectangleF parentBounds)
        {
            RectangleF oldBounds = BoundsF;
            base.Layout(parentBounds);
            if (oldBounds.Height != BoundsF.Height && needsUpdate != UpdateTypes.Geometry)
            {
                needsUpdate = UpdateTypes.Size;
            }
        }

        protected override string GetNodeName()
        {
            return base.GetNodeName() + "(" + Text + ")";
        }

#if DEBUG
        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.Button == MouseButtons.Right && mouseInputEvent.ButtonState == ButtonStates.Pressed)
            {
                if (textRenderer != null && Keyboard.GetState().IsKeyDown(Keys.LeftAlt))
                {
                    string filename = Address;
                    System.Text.RegularExpressions.Regex rgx = new System.Text.RegularExpressions.Regex("[^a-zA-Z0-9 -]");
                    filename = rgx.Replace(filename, "");

                    textRenderer.SavePNG(filename + ".png");
                }
            }

            return base.OnMouseInput(mouseInputEvent);
        }
#endif

        public Size GetTextSize()
        {
            UpdateGeometry();

            if (textRenderer != null)
            {
                return textRenderer.TextSize;
            }
            return default;
        }
    }
}
