using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition.Input;
using Composition.Layers;
using Tools;
using System.Diagnostics;

namespace Composition.Nodes
{
    public class Node : IDisposable
    {
        public Rectangle Bounds
        {
            get
            {
                return BoundsF.ToRectangle();
            }
        }
        public RectangleF BoundsF { get; set; }

        public virtual RectangleF RelativeBounds { get; set; }

        public CompositorLayer CompositorLayer { get; private set; }
        public bool NeedsDraw { get; protected set; }
        public bool NeedsLayout { get; protected set; }

        public Node Parent { get; private set; }
        private Node[] children;

        private bool containsMouse;

        public Node[] Children
        {
            get
            {
                return children;
            }
        }

        public float Alpha { get; set; }
        public virtual bool Visible { get; set; }

        public virtual bool Drawable
        {
            get
            {
                return Visible && Bounds.Width != 0 && Bounds.Height != 0 && Alpha != 0;
            }
        }

        public virtual IEnumerable<Node> VisibleChildren
        {
            get
            {
                lock (children)
                {
                    return children.Where(c => c.Visible && c.Alpha != 0);
                }
            }
        }

        public virtual IEnumerable<Node> InvisibleChildren
        {
            get
            {
                lock (children)
                {
                    return children.Where(c => !c.Visible);
                }
            }
        }

        public void CollapseDispose()
        {
            Node[] childenTemp = Children;

            foreach (Node child in childenTemp)
            {
                RectangleF bounds = new RectangleF(
                    RelativeBounds.X + (RelativeBounds.Width * child.RelativeBounds.X),
                    RelativeBounds.Y + (RelativeBounds.Height * child.RelativeBounds.Y),
                    RelativeBounds.Width * child.RelativeBounds.Width,
                    RelativeBounds.Height * child.RelativeBounds.Height
                );

                child.RelativeBounds = bounds;
                child.Remove();
                Parent.AddChild(child);
            }
            Dispose();
        }

        public int ChildCount { get { return children.Length; } }

        public IEnumerable<Node> ParentChain
        {
            get
            {
                Node parent = Parent;
                while (parent != null)
                {
                    yield return parent;
                    parent = parent.Parent;
                }
            }
        }

        public bool HasFocus
        {
            get
            {
                if (CompositorLayer == null) return false;
                if (CompositorLayer.PlatformTools == null) return false;
                return CompositorLayer.FocusedNode == this && CompositorLayer.PlatformTools.Focused;
            }
            set
            {
                if (CompositorLayer == null) return;

                if (HasFocus || value)
                {
                    CompositorLayer.FocusedNode = value ? this : null;
                }
            }
        }
        public event Action<bool> OnFocusChanged;
        public void FocusChanged(bool focused) { OnFocusChanged?.Invoke(focused); }

        public Node RootNode { get { if (Parent == null) return this; return Parent.RootNode; } }

        public bool Disposed { get; set; }

        private string nodeName;
        public string NodeName
        {
            get
            {
                if (string.IsNullOrEmpty(nodeName))
                {
                    nodeName = GetNodeName();
                }
                return nodeName;
            }
            set
            {
                nodeName = value;
            }
        }

        public PlatformTools PlatformTools 
        { 
            get 
            { 
                if (CompositorLayer == null)
                {
                    return null;
                }
                return CompositorLayer.PlatformTools; 
            } 
        }

        public Node()
        {
#if DEBUG
            creator = new StackTrace(true);
#endif

            Alpha = 1;
            Visible = true;
            NeedsDraw = true;
            NeedsLayout = true;
            RelativeBounds = new RectangleF(0, 0, 1, 1);
            children = new Node[0];
        }

#if DEBUG

        private string lastAddress;
        private StackTrace creator;

        ~Node()
        {
            if (!Disposed)
            {
                Logger.UI.Log(this, "Not Disposed" + Address + "\n Creator: " + creator);
                System.Diagnostics.Debug.Assert(Disposed);
                Dispose();
            }
        }
#endif

        public string Address
        {
            get
            {
                return string.Join("->", ParentChain.Reverse().Select(p => p.NodeName));
            }
        }

        public virtual void Dispose()
        {
            Disposed = true;

            if (HasFocus)
            {
                HasFocus = false;
            }

            // Remove from parent. Not done via normal remove command for easier debugging.
            if (Parent != null)
            {
                Parent.RemoveChild(this);
            }

            ClearDisposeChildren();
        }

        protected virtual void SetParent(Node parent)
        {
            Parent = parent;

#if DEBUG
            if (Parent != null)
            {
                lastAddress = Parent.Address;
            }
#endif
        }

        public void ClearDisposeChildren()
        {
            Node[] t = children;
            foreach (Node n in t)
            {
                n.Dispose();
            }
            children = new Node[0];
        }

        public virtual void SetCompositorLayer(CompositorLayer compositor)
        {
            CompositorLayer = compositor;

            Node[] t = children;
            foreach (Node n in t)
            {
                n.SetCompositorLayer(compositor);
            }

            CompositorLayer?.Register(this);
        }

        public void AddChild(params Node[] nodes)
        {
            System.Diagnostics.Debug.Assert(!nodes.Any(n => n.Disposed));

            if (Disposed)
            {
                foreach (Node node in nodes)
                {
                    node.Dispose();
                }
                return;
            }

            lock (children)
            {
                children = children.Union(nodes).ToArray();
                foreach (Node node in nodes)
                {
                    node.SetParent(this);
                    node.SetCompositorLayer(CompositorLayer);
                }
            }
        }

        public void AddChild(Node node, int index)
        {
            if (Disposed)
            {
                node.Dispose();
                return;
            }

            lock (children)
            {
                List<Node> asList = children.ToList();
                asList.Insert(index, node);
                children = asList.ToArray();

                node.SetParent(this);
                node.SetCompositorLayer(CompositorLayer);
            }
        }

        public void RemoveChild(params Node[] nodes)
        {
            lock (children)
            {
                children = children.Except(nodes).ToArray();
                foreach (Node node in nodes)
                {
                    node.SetParent(null);
                }
            }
            foreach (Node node in nodes)
            {
                CompositorLayer?.Unregister(node);
            }
        }

        public Node RemoveChild(int index)
        {
            lock (children)
            {
                Node n = GetChild(index);
                if (n != null)
                {
                    RemoveChild(n);
                }
                return n;
            }
        }

        public void Remove()
        {
            if (Parent != null)
            {
                Parent.RemoveChild(this);
            }
        }

        public Node GetChild(int index)
        {
            lock (children)
            {
                if (index >= ChildCount)
                {
                    return null;
                }

                return children[index];
            }
        }

        public T GetChild<T>(int index) where T : Node
        {
            return GetChild(index) as T;
        }

        public int IndexOf(Node node)
        {
            return Array.IndexOf(children, node);
        }


        public virtual RectangleF CalculateRelativeBounds(RectangleF parentPosition)
        {
            return new RectangleF()
            {
                X = parentPosition.X + parentPosition.Width * RelativeBounds.X,
                Y = parentPosition.Y + parentPosition.Height * RelativeBounds.Y,
                Width = parentPosition.Width * RelativeBounds.Width,
                Height = parentPosition.Height * RelativeBounds.Height
            };
        }

        public virtual void SetBounds(RectangleF bounds)
        {
            RectangleF parentBounds = Parent.BoundsF;
            RelativeBounds = new RectangleF(
                (bounds.X - parentBounds.X) / parentBounds.Width,
                (bounds.Y - parentBounds.Y) / parentBounds.Height,
                bounds.Width / parentBounds.Width,
                bounds.Height / parentBounds.Height
                );
        }

        public virtual void Layout(RectangleF parentBounds)
        {
            NeedsLayout = false;
            BoundsF = CalculateRelativeBounds(parentBounds);
            LayoutChildren(BoundsF);
        }

        protected virtual void LayoutChildren(RectangleF bounds)
        {
            Node[] t = children;
            foreach (Node n in t)
            {
                n.Layout(bounds);
            }
        }

        public virtual void Draw(Drawer id, float parentAlpha)
        {
            NeedsDraw = false;
            DrawChildren(id, parentAlpha);
        }

        public void DrawChildren(Drawer id, float parentAlpha)
        {
            Node[] t = children;
            foreach (Node n in t)
            {
                if (n.Drawable)
                {
                    n.Draw(id, parentAlpha * Alpha);
                }
            }
        }

        public virtual bool Contains(Point point)
        {
            return BoundsF.Contains(point);
        }

        public virtual bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            foreach (Node child in VisibleChildren.Reverse().ToArray())
            {
                bool newContains = child.Contains(mouseInputEvent.Position);

                if (newContains != child.containsMouse && mouseInputEvent.EventType == MouseInputEvent.EventTypes.Move)
                {
                    MouseInputEvent subEvent;
                    if (newContains)
                        subEvent = new MouseInputEnterEvent(mouseInputEvent);
                    else
                        subEvent = new MouseInputLeaveEvent(mouseInputEvent);

                    if (child.OnMouseInput(subEvent))
                        return true;
                }
                else if (newContains)
                {
                    if (child.OnMouseInput(mouseInputEvent))
                        return true;
                }

                child.containsMouse = newContains;
            }
            return false;
        }

        public virtual bool OnKeyboardInput(KeyboardInputEvent inputEvent)
        {
            return false;
        }

        public virtual bool OnTextInput(TextInputEventArgs inputEvent)
        {
            return false;
        }

        public virtual bool OnDrop(MouseInputEvent finalInputEvent, Node node)
        {
            foreach (Node child in VisibleChildren.Reverse().ToArray())
            {
                if (child.Contains(finalInputEvent.Position))
                {
                    if (child.OnDrop(finalInputEvent, node))
                    {
                        return true;
                    }
                }
            }
#if DEBUG
            if (ChildCount == 0)
            {
                // handy to work out where you dragged too...
                Console.WriteLine(string.Join(", ", ParentChain.Select(p => p.ToString())));
            }
#endif
            return false;
        }

        public void SetOrder<TNode, TKey>(Func<TNode, TKey> keySelector) where TNode : Node
        {
            lock (children)
            {
                TNode[] ofType = children.OfType<TNode>().OrderBy(keySelector).ToArray();
                Node[] notOfType = children.Where(t => !ofType.Contains(t)).ToArray();

                children = ofType.Union(notOfType).ToArray();
            }
        }

        public void SetOrder<T>(IEnumerable<T> nodes) where T : Node
        {
            lock (children)
            {
                // Remove them and then append them.
                children = children.Except(nodes).Union(nodes).ToArray();
            }
        }

        public void SetBack(params Node[] n)
        {
            lock (children)
            {
                children = n.Union(children.Except(n)).ToArray();
            }
        }

        public void SetFront(params Node[] n)
        {
            lock (children)
            {
                children = children.Except(n).Union(n).ToArray();
            }
        }

        //public virtual void DrawDebug(Graphics g)
        //{
        //    using (Pen p = new Pen(Color.Magenta))
        //    {
        //        g.DrawRectangle(p, Bounds);
        //    }

        //    if (this.children == null)
        //        return;
        //    Node[] children = VisibleChildren.ToArray();
        //    foreach (Node node in children)
        //    {
        //        node.DrawDebug(g);
        //    }
        //}

        public virtual void RequestRedraw()
        {
            NeedsDraw = true;

            Node parent = Parent;
            if (parent == null)
            {
                if (CompositorLayer != null)
                {
                    CompositorLayer.RequestRedraw();
                }
            }
            else
            {
                parent.RequestRedraw();
            }
        }

        public virtual void RequestLayout()
        {
            NeedsLayout = true;
            NeedsDraw = true;

            if (Parent == null)
            {
                if (CompositorLayer != null)
                {
                    CompositorLayer.RequestLayout();
                }
            }
            else
            {
                Parent.RequestLayout();
            }
        }

        public virtual void Snap()
        {
            foreach (Node node in Children)
            {
                node.Snap();
            }
        }

        public void Scale(float scale)
        {
            Scale(scale, scale);
        }

        public void Scale(float scaleX, float scaleY)
        {
            RelativeBounds = RelativeBounds.Scale(scaleX, scaleY);
        }

        public void Translate(float x, float y)
        {
            RelativeBounds = new RectangleF(RelativeBounds.X + x,
                                           RelativeBounds.Y + y,
                                           RelativeBounds.Width,
                                           RelativeBounds.Height);
        }

        public void SetSize(float width, float height)
        {
            RelativeBounds = new RectangleF(RelativeBounds.X,
                                           RelativeBounds.Y,
                                           width,
                                           height);
        }

        public void AddSize(float width, float height)
        {
            RelativeBounds = new RectangleF(RelativeBounds.X,
                                           RelativeBounds.Y,
                                           RelativeBounds.Width + width,
                                           RelativeBounds.Height + height);
        }

        public virtual bool IsAnimating()
        {
            if (Parent != null)
            {
                return Parent.IsAnimating();
            }

            return false;
        }

        public virtual bool IsAnimatingSize()
        {
            Node parent = Parent;
            if (parent != null)
            {
                return parent.IsAnimatingSize();
            }

            return false;
        }

        public virtual bool IsAnimatingInvisiblity()
        {
            Node parent = Parent;
            if (parent != null)
            {
                return parent.IsAnimatingInvisiblity();
            }

            return false;
        }

        protected virtual string GetNodeName()
        {
            string name = GetType().Name;
            if (Parent == null)
            {
                return "Root" + name;
            }

            return name;
        }

        public T GetNodeByName<T>(string name) where T : Node
        {
            if (name == NodeName && typeof(T).IsAssignableFrom(GetType()))
            {
                return (T)this;
            }

            foreach (Node node in Children)
            {
                T found = node.GetNodeByName<T>(name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        public static void AlignHorizontally(params Node[] nodes)
        {
            AlignHorizontally(0.0f, nodes);
        }

        public static void AlignHorizontally(float padding, params Node[] nodes)
        {
            int count = nodes.Count();
            AlignHorizontally(padding, count, nodes);
        }

        public static void AlignHorizontally(float padding, int count, params Node[] nodes)
        {
            float width = 1.0f / count;
            float x = 0;
            foreach (Node n in nodes)
            {
                if (n != null)
                {
                    n.RelativeBounds = new RectangleF(x, 0, width, 1);
                    n.Scale(1 - padding, 1);
                }
                x += width;
            }
        }

        public static void SplitHorizontally(Node left, Node right, float ratio)
        {
            left.RelativeBounds = new RectangleF(0, 0, ratio, 1);
            right.RelativeBounds = new RectangleF(ratio, 0, 1 - ratio, 1);
        }


        public static void AlignVertically(params Node[] nodes)
        {
            AlignVertically(0.0f, nodes);
        }

        public static void AlignVertically(float padding, params Node[] nodes)
        {
            int count = nodes.Count();
            AlignVertically(padding, count, nodes);
        }

        public static void AlignVertically(float padding, int count, params Node[] nodes)
        {
            float height = 1.0f / count;

            float y = 0;
            foreach (Node n in nodes)
            {
                if (n != null)
                {
                    n.RelativeBounds = new RectangleF(0, y, 1, height);
                    n.Scale(1, 1 - padding);
                }

                y += height;
            }
        }

        public static void MakeColumns(IEnumerable<Node> nodes, int count, float leftAlign, float width)
        {
            AlignVertically(0, count, nodes.ToArray());

            foreach (Node n in nodes)
            {
                RectangleF bounds = n.RelativeBounds;
                bounds.X = leftAlign;
                bounds.Width = width;
                n.RelativeBounds = bounds;
            }
        }

        public virtual RectangleF ParentChainTargetBounds()
        {
            if (Parent == null)
            {
                return BoundsF;
            }

            RectangleF parent = Parent.ParentChainTargetBounds();

            RectangleF p = new RectangleF();
            p.X = parent.X + parent.Width * RelativeBounds.X;
            p.Y = parent.Y + parent.Height * RelativeBounds.Y;
            p.Width = parent.Width * RelativeBounds.Width;
            p.Height = parent.Height * RelativeBounds.Height;
            return p;
        }

        public T GetLayer<T>() where T : Layer
        {
            return CompositorLayer.LayerStack.GetLayer<T>();
        }

        public virtual IEnumerable<Node> GetRecursiveChildren()
        {
            yield return this;
            lock (children)
            {
                foreach (var child in children)
                {
                    foreach (var recursive in child.GetRecursiveChildren())
                    {
                        yield return recursive;
                    }
                }
            }
        }

        public Point GetScreenPosition()
        {
            Point position = Point.Zero;
            foreach (IScrollableNode iscroller in ParentChain.OfType<IScrollableNode>())
            {
                position += iscroller.ScrollOffset;

                switch (iscroller.Scroller.ScrollType)
                {
                    case ScrollerNode.Types.Horizontal:
                        position.X -= (int)iscroller.Scroller.CurrentScrollPixels;
                        break;

                    case ScrollerNode.Types.VerticalLeft:
                    case ScrollerNode.Types.VerticalRight:
                        position.Y -= (int)iscroller.Scroller.CurrentScrollPixels;
                        break;
                }
            }

            position += Bounds.Location;

            return position;
        }

        public void ReplaceWith(Node node)
        {
            if (Parent == null)
            {
                return;
            }

            node.RelativeBounds = RelativeBounds;
            node.BoundsF = BoundsF;
            node.Visible = Visible;
            node.Alpha = Alpha;
            node.Parent = Parent;


            Node[] newChildren = new Node[Parent.children.Length];

            for (int i = 0; i < newChildren.Length; i++)
            {
                if (children[i] == this)
                {
                    newChildren[i] = node;
                }
                else
                {
                    newChildren[i] = children[i];
                }
            }
            Parent = null;
        }
    }

    public interface IUpdateableNode
    {
        void Update(GameTime gameTime);
    }

}
