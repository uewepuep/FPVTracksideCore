using System;
using System.Collections.Generic;
using System.Threading;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Tools;

namespace Composition.Layers
{
    public class CompositorLayer : Layer
    {
        private bool needsRedraw;

        public bool NeedsLayout { get; private set; }
        public Rectangle Bounds { get; private set; }
        public GraphicsDevice GraphicsDevice { get; private set; }

        private Drawer drawer;

        public Node Root { get; private set; }

        private Node focusNode;
        public Node FocusedNode
        {
            get
            {
                return focusNode;
            }
            set
            {
                Node old = focusNode;
                focusNode = value;

                if (old != null)
                    old.FocusChanged(false);


                if (focusNode != null)
                    focusNode.FocusChanged(true);
            }
        }

        public int FrameNumber { get; private set; }

        private List<IUpdateableNode> updateables;

        public PlatformTools PlatformTools
        {
            get
            {
                if (LayerStack == null)
                    return null;

                return LayerStack.PlatformTools;
            }
        }

        public CompositorLayer(GraphicsDevice device)
        {
            updateables = new List<IUpdateableNode>();

            drawer = new Drawer(device);
            GraphicsDevice = device;

            needsRedraw = true;

            Root = new Node();
            Root.SetCompositorLayer(this);
        }

        public override void Dispose()
        {
            drawer?.Dispose();

            Root?.Dispose();
            base.Dispose();
        }

        private void Layout(Rectangle bounds)
        {
            Bounds = bounds;
            NeedsLayout = false;
            Root.Layout(new RectangleF(Bounds));
        }

        public void RequestRedraw()
        {
            if (!needsRedraw)
            {
                LayerStack.RequestRedraw();
            }
        }

        public void RequestLayout()
        {
            NeedsLayout = true;
            RequestRedraw();
        }

        protected override void OnUpdate(GameTime gameTime)
        {
            DebugTimer.DebugStartTime("CompLayer.Layout");
            Rectangle bounds = LayerStack.GetBounds();
            if (bounds != Bounds)
            {
                Layout(bounds);
                Root.Snap();
            }
            else if (NeedsLayout)
            {
                Layout(bounds);
            }
            DebugTimer.DebugEndTime("CompLayer.Layout");


            DebugTimer.DebugStartTime("CompLayer.Update");

            IUpdateableNode[] array;
            lock (updateables)
            {
                array = updateables.ToArray();
            }

            foreach (IUpdateableNode updateableNode in array)
            {
                try
                {
                    updateableNode.Update(gameTime);
                }
                catch (Exception e)
                {
                    Logger.UI.LogException(this, e);
                }
            }

            base.OnUpdate(gameTime);

            DebugTimer.DebugEndTime("CompLayer.Update");
        }

        protected override void OnDraw()
        {
            DebugTimer.DebugStartTime("CompLayer.Draw");
            needsRedraw = false;
            FrameNumber++;

            base.OnDraw();
            if (Root.Visible)
            {
                try
                {
                    drawer.Begin();
                    Root.Draw(drawer, 1);
                    drawer.End();
                }
                catch (Exception e)
                {
                    drawer?.Dispose();
                    Logger.UI.LogException(this, e);
                    Logger.UI.Log(this, "Creating new Drawing object", drawer);
                    drawer = new Drawer(GraphicsDevice);
                }
            }
            DebugTimer.DebugEndTime("CompLayer.Draw");
        }

        public override bool OnMouseInput(MouseInputEvent inputEvent)
        {
            if (Root.OnMouseInput(inputEvent))
            {
                return true;
            }
            return base.OnMouseInput(inputEvent);
        }

        public override bool OnKeyboardInput(KeyboardInputEvent inputEvent)
        {
            if (FocusedNode != null)
            {
                if (FocusedNode.OnKeyboardInput(inputEvent))
                {
                    return true;
                }
            }

            return base.OnKeyboardInput(inputEvent);
        }

        public override bool OnTextInput(TextInputEventArgs inputEvent)
        {
            if (FocusedNode != null)
            {
                if (FocusedNode.OnTextInput(inputEvent))
                {
                    return true;
                }
            }

            return base.OnTextInput(inputEvent);
        }

        public bool DragEndEvent(MouseInputEvent finalInputEvent, Node node)
        {
            if (Root.OnDrop(finalInputEvent, node))
            {
                return true;
            }
            return false;
        }

        public void Register(Node node)
        {
            if (node is IUpdateableNode)
            {
                lock (updateables)
                {
                    updateables.Add(node as IUpdateableNode);
                }
            }
        }

        public void Unregister(Node node)
        {
            if (node is IUpdateableNode)
            {
                lock (updateables)
                {
                    updateables.Remove(node as IUpdateableNode);
                }
            }
        }

        public void CleanUp(IDisposable disposable)
        {
            if (drawer != null)
            {
                drawer.CleanUp(disposable);
            }
            else
            {
                disposable.Dispose();
            }
        }

        public override void DoBackground()
        {
            base.DoBackground();
            drawer.DoPreProcess();
        }

        public bool InView(Rectangle rectangle)
        {
            Rectangle zeroedView = Bounds;
            zeroedView.X = 0;
            zeroedView.Y = 0;

            return zeroedView.Intersects(rectangle);
        }

        public void PreProcess(IPreProcessable toPreProcess, bool forced = false)
        {
            if (drawer != null)
            {
                drawer.PreProcess(toPreProcess, forced);
            }
        }
    }
}
