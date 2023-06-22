using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition.Layers
{
    public class DragLayer : Layer
    {
        private RenderTarget2D renderTarget;
        private object renderTargetLock;
        private Drawer drawer;
        private GraphicsDevice device;

        public Node DragNode { get; private set; }
        public Point DragNodeOffset { get; private set; }

        public MouseInputEvent DragStart { get; private set; }

        public Point DragDistance { get; private set; }

        public bool OverDragThreshold { get; private set; }
        public bool IsDragging { get { return DragNode != null; } }
        public bool needsRender;

        public DragLayer(GraphicsDevice device)
        {
            this.device = device;
            renderTargetLock = new object();
            drawer = new Drawer(device, true);
        }

        public override void Dispose()
        {
            drawer?.Dispose();
            base.Dispose();
        }

        protected override void OnUpdate(GameTime gameTime)
        {
            base.OnUpdate(gameTime);

            Rectangle bounds = LayerStack.Bounds;
            if ((renderTarget == null || bounds != renderTarget.Bounds) && bounds.Width > 0 && bounds.Height > 0)
            {
                lock (renderTargetLock)
                {
                    renderTarget?.Dispose();
                    renderTarget = new RenderTarget2D(device, bounds.Width, bounds.Height);
                }
            }

            if (needsRender)
            {
                needsRender = false;
                DrawToTexture();
            }
        }

        protected override void OnDraw()
        {

            base.OnDraw();
            if (renderTarget != null && DragNode != null && OverDragThreshold)
            {
                drawer.Begin();
                Rectangle destination = renderTarget.Bounds;
                destination.Location = DragDistance + DragNodeOffset;
                drawer.Draw(renderTarget, renderTarget.Bounds, destination, Color.White, 0.8f);
                drawer.End();
            }
        }

        public override bool OnMouseInput(MouseInputEvent inputEvent)
        {
            bool result = false;
            if (DragStart != null && DragNode != null)
            {
                DragDistance = inputEvent.ScreenPosition - DragStart.ScreenPosition;

                if (!OverDragThreshold && DragDistance.ToVector2().Length() > 10)
                {
                    OverDragThreshold = true;
                    needsRender = true;
                }

                if (inputEvent.Button == DragStart.Button && inputEvent.ButtonState == ButtonStates.Released)
                {
                    if (OverDragThreshold)
                    {
                        FinishDrag(inputEvent);
                        result = true;
                    }
                    else
                    {
                        ClearDrag();
                    }
                }

                if (inputEvent.ButtonState != ButtonStates.None && OverDragThreshold)
                {
                    result = true;
                }

            }

            return result;
        }

        public void FinishDrag(MouseInputEvent inputEvent)
        {
            IEnumerable<CompositorLayer> compositiorLayers = LayerStack.GetLayers<CompositorLayer>();

            foreach (CompositorLayer cl in compositiorLayers)
            {
                if (cl.DragEndEvent(inputEvent, DragNode))
                {
                    break;
                }
            }
            ClearDrag();
        }

        public void ClearDrag()
        {
            OverDragThreshold = false;
            DragNode = null;
            DragStart = null;
            DragDistance = Point.Zero;
        }

        public void RegisterDrag(Node dragged, MouseInputEvent mie)
        {
            Point mouseMinusTranslation = mie.Position - mie.Translation;

            Point offsetFromNodeZero = mie.Position - dragged.Bounds.Location;

            OverDragThreshold = false;
            DragNode = dragged;
            DragNodeOffset = mouseMinusTranslation - offsetFromNodeZero;
            DragStart = mie;
            DragDistance = Point.Zero;
        }

        protected bool DrawToTexture()
        {
            try
            {
                if (DragNode != null && renderTarget != null)
                {
                    lock (renderTargetLock)
                    {
                        try
                        {
                            // Set the render target
                            device.SetRenderTarget(renderTarget);
                            device.Clear(Color.Transparent);

                            drawer.Offset = new Point(-DragNode.Bounds.X, -DragNode.Bounds.Y);
                            drawer.Begin();
                            DragNode.Draw(drawer, 1);
                            drawer.Offset = Point.Zero;

                            //drawer.Draw(texture, sourceBounds, new Rectangle(0, 0, Size.Width, Size.Height), Color.White, 1);
                            drawer.End();
                        }
                        finally
                        {
                            // Drop the render target
                            device.SetRenderTarget(null);
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void PreProcess(Drawer id)
        {
            throw new NotImplementedException();
        }
    }
}
