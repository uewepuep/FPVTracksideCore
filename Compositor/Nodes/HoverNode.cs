using Composition.Input;
using Composition.Layers;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class HoverNode : Node
    {
        public Color Color { get { return hover.Color; } set { hover.Color = value; } }

        private ColorNode hover;

        public bool Enabled { get; set; }

        public event MouseInputDelegate OnHover;

        private RenderTargetNode renderTargetNode;

        public HoverNode(Color hoverColor)
        {
            hover = new ColorNode(hoverColor);
            AddChild(hover);

            hover.Visible = false;
            Enabled = true;
        }

        public override void Dispose()
        {
            Enabled = false;
            if (renderTargetNode != null && renderTargetNode.HoverNode == this)
            {
                renderTargetNode.HoverNode = null;
            }

            base.Dispose();
        }

        public override void RequestRedraw()
        {
            // This doesn't need to go higher if we have a render target node.
            if (renderTargetNode == null)
            {
                base.RequestRedraw();
            }
        }

        public override void SetCompositorLayer(CompositorLayer compositor)
        {
            base.SetCompositorLayer(compositor);
            renderTargetNode = ParentChain.OfType<RenderTargetNode>().FirstOrDefault();
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            // Just to make sure we never draw when inside a render target
            if (renderTargetNode == null)
            {
                base.Draw(id, parentAlpha);
            }
        }

        public void RenderTargetDraw(Drawer id, float parentAlpha)
        {
            RectangleF localBounds = hover.BoundsF;

            hover.BoundsF = new RectangleF(hover.GetScreenPosition().X, hover.GetScreenPosition().Y, localBounds.Width, localBounds.Height);
            hover.Draw(id, parentAlpha);
            hover.BoundsF = localBounds;
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent is MouseInputEnterEvent && Enabled)
            {
                OnHover?.Invoke(mouseInputEvent);
                hover.Visible = true;

                if (renderTargetNode != null)
                {
                    renderTargetNode.HoverNode = this;
                }
                else
                {
                    RequestRedraw();
                }
            }

            if (mouseInputEvent is MouseInputLeaveEvent && hover.Visible)
            {
                hover.Visible = false;

                if (renderTargetNode != null)
                {
                    if (renderTargetNode.HoverNode == this)
                    {
                        renderTargetNode.HoverNode = null;
                    }
                }
                else
                {
                    RequestRedraw();
                }
            }
            return base.OnMouseInput(mouseInputEvent);
        }
    }
}
