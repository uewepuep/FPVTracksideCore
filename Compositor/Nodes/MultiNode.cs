using Composition.Input;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class MultiNode : Node, IUpdateableNode
    {
        public float Padding { get; set; }

        public InterpolatedFloat Offset { get; private set; }

        public Node Showing { get; private set; }

        public bool Single { get { return Offset.Finished && Showing != null && finalized; } }

        public event Action<Node> OnShowChange;

        private bool finalized;

        public enum Directions { Vertical, Horizontal }

        public Directions Direction { get; set; }

        private RectangleF holding;

        private List<Node> active;

        public Node[] Active 
        {
            get 
            { 
                lock (active)
                {
                    return active.ToArray();
                }
            } 
        }

        public MultiNode(TimeSpan animationTime)
        {
            holding = new RectangleF(-1, -1, 1, 1);

            active = new List<Node>();

            Padding = 0.25f;
            Offset = new InterpolatedFloat(0, 0, animationTime);
            Offset.SmoothStep = true;

            Showing = null;

            Direction = Directions.Vertical;
        }

        public override bool IsAnimating()
        {
            if (!Offset.Finished)
            {
                return true;
            }

            return base.IsAnimating();
        }

        public override void Snap()
        {
            Offset.Snap();
            base.Snap();
        }

        public override void Layout(RectangleF parentBounds)
        {
            NeedsLayout = false;
            BoundsF = CalculateRelativeBounds(parentBounds);

            LayoutLinearChildren();
        }

        private void LayoutLinearChildren()
        {
            switch (Direction)
            {
                case Directions.Horizontal:
                    float transX = -Offset.Output;
                    foreach (Node node in Active)
                    {
                        node.RelativeBounds = new RectangleF(transX, 0, 1, 1);
                        node.Layout(BoundsF);
                        transX += node.RelativeBounds.Width + Padding;
                    }
                    break;

                case Directions.Vertical:
                    float transY = -Offset.Output;
                    foreach (Node node in Active)
                    {
                        node.RelativeBounds = new RectangleF(0, transY, 1, 1);
                        node.Layout(BoundsF);
                        transY += node.RelativeBounds.Height + Padding;
                    }
                    break;
            }
        }

        public void Update(GameTime gameTime)
        {
            if (IsAnimating())
            {
                RequestLayout();
            }

            if (Offset.Finished && !finalized)
            {
                finalized = true;
                lock (active)
                {
                    active.RemoveAll(r => r != Showing);
                }
                Offset.SetTarget(0, 0);
                LayoutLinearChildren();
            }
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            foreach (Node n in Active)
            {
                n.Draw(id, parentAlpha);
            }
        }


        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (Showing != null)
            {
                return Showing.OnMouseInput(mouseInputEvent);
            }
            return false;
        }

        public override bool OnKeyboardInput(KeyboardInputEvent inputEvent)
        {
            if (Showing != null)
            {
                return Showing.OnKeyboardInput(inputEvent);
            }
            return false;
        }

        public void Show(Node node)
        {
            if (node == null)
            {
                return;
            }

            lock (active)
            {
                if (Showing == null)
                {
                    Showing = node;
                    active.Add(node);
                    Offset.SetTarget(0, 0);
                    return;
                }

                int index = IndexOf(node);
                if (Showing != node && index >= 0)
                {
                    active.Clear();

                    int oldIndex = IndexOf(Showing);
                    if (oldIndex > index)
                    {
                        active.Add(node);
                        active.Add(Showing);
                        Offset.SetTarget(1 + Padding, 0);
                    }
                    else
                    {
                        active.Add(Showing);
                        active.Add(node);
                        Offset.SetTarget(0, 1 + Padding);
                    }

                    foreach (Node n in Children)
                    {
                        if (!active.Contains(n))
                        {
                            n.RelativeBounds = holding;
                        }
                    }

                    finalized = false;
                    Showing = node;
                    LayoutLinearChildren();
                    RequestLayout();

                    OnShowChange?.Invoke(node);
                }
            }
        }
    }
}
