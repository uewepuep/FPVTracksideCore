using Composition.Input;
using Composition.Layers;
using Composition.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
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
                if (text != value)
                {
                    text = value;
                    needsGeometryUpdate = true;
                }
            }
        }

        public Color Tint { get; set; }

        public virtual string DrawingText { get { return text; } }

        protected bool needsGeometryUpdate;
        protected bool needsTextureUpdate;

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
            needsGeometryUpdate = true;
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
                if (textRenderer == null)
                {
                    textRenderer = CompositorLayer.LayerStack.PlatformTools.CreateTextRenderer();
                }

                bool isAnimatingSize = IsAnimatingSize();

                if (string.IsNullOrEmpty(DrawingText) || Bounds.Height == 0)
                {
                    return;
                }

                if (needsGeometryUpdate)
                {
                    if (!IsAnimatingInvisiblity() && !isAnimatingSize && Alpha > 0)
                    {
                        id.EnqueueBackgroundWork(() => { UpdateTexture(id); });
                    }
                }

                Scale scale;
                if (CanScale)
                {
                    if (IsAnimatingSize() /*|| needsGeometryUpdate || needsTextureUpdate */)
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

                textRenderer?.Draw(id, Bounds, Alignment, scale, Tint, parentAlpha * Alpha);
            }
            catch
            {
                textRenderer?.Reset();
            }
            DebugTimer.DebugEndTime(this);

            // Has to be done because drawing children normally happens in base.
            DrawChildren(id, parentAlpha);
        }


        public virtual void PreProcess(Drawer id)
        {
            if (textRenderer != null)
            {
                textRenderer.CreateTextures(id);
                needsTextureUpdate = false;
                RequestRedraw();
            }
        }

        public void UpdateTexture(Drawer id)
        {
            if (textRenderer == null)
                return;

            if (needsGeometryUpdate)
            {
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

                textRenderer.CreateGeometry(width, newHeight, DrawingText, Style);
                needsTextureUpdate = true;
                needsGeometryUpdate = false;

                height = newHeight;
                RequestRedraw();
            }

            if (textRenderer == null)
                return;

            if (textRenderer.CanCreateTextures)
            {
                id.PreProcess(this);
            }
            else
            {
                textRenderer.Reset();
            }
        }

        public override void Layout(Microsoft.Xna.Framework.Rectangle parentBounds)
        {
            Microsoft.Xna.Framework.Rectangle oldBounds = Bounds;
            base.Layout(parentBounds);
            if (oldBounds.Height != Bounds.Height)
            {
                needsGeometryUpdate = true;
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
    }

}
