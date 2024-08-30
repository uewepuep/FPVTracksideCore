using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition.Layers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Tools;

namespace Composition.Nodes
{
    public class GridNode : AspectNode
    {
        public enum GridTypes
        {
            None = 0,
            One = 1,
            Two = 2,
            Three = 3,
            Four = 4,
            Six = 6,
            Eight = 8,
            Nine = 9,
            Ten = 10,
            Twelve = 12,
            Fifteen = 15,
            Sixteen = 16,
            Twenty = 20,
            SingleRow = 40,
        }


        public GridTypes GridType { get; private set; }

        private Size singleSize;
        public Size SingleSize 
        { 
            get => singleSize; 
            set 
            {   
                singleSize = value; 
            } 
        }

        public event System.Action<GridTypes> OnGridTypeChanged;
        public event System.Action<int> OnGridCountChanged;

        private Node[] childrenAtTypeChange;

        public virtual IEnumerable<GridTypes> AllowedGridTypes
        {
            get
            {
                return Enum.GetValues(typeof(GridTypes)).OfType<GridTypes>();
            }
        }

        public float Padding { get; set; }

        private int oldVisibleChildrenCount;

        public bool ForceUpdate { get; set; }

        public bool LockGridType { get; set; }

        private int lastVisibiltyCount;

        public GridNode()
        {
            Padding = 0.01f;
            SingleSize = new Size(400, 300);
            KeepAspectRatio = true;
        }

        public virtual IEnumerable<Node> OrderedChildren(IEnumerable<Node> input)
        {
            return input;
        }

        public virtual void UpdateVisibility(IEnumerable<Node> input)
        {
        }

        protected virtual int VisibleChildCount()
        {
            return VisibleChildren.Count();
        }

        public override void Layout(RectangleF parentBounds)
        {
            UpdateVisibility(Children);

            int visibleChildrenCount = VisibleChildCount();
            if (visibleChildrenCount != lastVisibiltyCount)
            {
                OnGridCountChanged?.Invoke(visibleChildrenCount);
                lastVisibiltyCount = visibleChildrenCount;
            }

            GridTypes newType = DecideLayout(visibleChildrenCount);
            if (newType != GridType
             || oldVisibleChildrenCount < visibleChildrenCount
             || ForceUpdate)
            {
                GridType = newType;
                oldVisibleChildrenCount = visibleChildrenCount;
                ForceUpdate = false;

                // Cache the children, we don't want them to move around within the same layout grid.
                childrenAtTypeChange = Children.ToArray();

                OnGridTypeChanged?.Invoke(newType);

                IEnumerable<Node> nodes = OrderedChildren(childrenAtTypeChange).Where(n => n.Visible);

                int count = nodes.Count(); 

                int width, height;
                GetWidthHeight(GridType, count, out width, out height);

                Size size = new Size(SingleSize.Width * width, SingleSize.Height * height);
                SetAspectRatio(size);

                LayoutGrid(nodes, width, height);

                foreach (Node n in childrenAtTypeChange)
                {
                    n.Scale(1 - Padding);
                }

                float smallHeight = 0.01f;
                float widtha = smallHeight * SingleSize.AspectRatio;
                foreach (Node n in InvisibleChildren)
                {
                    n.RelativeBounds = new RectangleF(0.5f - (widtha / 2), 0.5f - (smallHeight / 2), widtha, smallHeight);
                }

                SetOrder(nodes);
            }

            base.Layout(parentBounds);
        }

        public static int GridTypeItemCount(GridTypes gt)
        {
            return (int)gt;
        }

        protected virtual GridTypes DecideLayout(int count)
        {
            if (LockGridType)
                return GridType;

            if (count == 0)
                return GridTypes.None;

            foreach (GridTypes gt in AllowedGridTypes)
            {
                if (count <= GridTypeItemCount(gt))
                {
                    return gt;
                }
            }

            return GridTypes.One;

        }

        private void GetWidthHeight(GridTypes gridType, int count, out int width, out int height)
        {
            switch (gridType)
            {
                case GridTypes.SingleRow:
                    width = count;
                    height = 1;
                    break;

                case GridTypes.None:
                case GridTypes.One:
                default:
                    width = 1;
                    height = 1;
                    break;

                case GridTypes.Two:
                    width = 2;
                    height = 1;
                    break;

                case GridTypes.Three:
                    width = 3;
                    height = 1;
                    break;

                case GridTypes.Four:
                    width = 2;
                    height = 2;
                    break;

                case GridTypes.Six:
                    width = 3;
                    height = 2;
                    break;

                case GridTypes.Eight:
                    width = 4;
                    height = 2;
                    break;

                case GridTypes.Nine:
                    width = 3;
                    height = 3;
                    break;

                case GridTypes.Ten:
                    width = 5;
                    height = 2;
                    break;

                case GridTypes.Twelve:
                    width = 4;
                    height = 3;
                    break;

                case GridTypes.Fifteen:
                    width = 5;
                    height = 3;
                    break;

                case GridTypes.Sixteen:
                    width = 4;
                    height = 4;
                    break;

                case GridTypes.Twenty:
                    width = 5;
                    height = 4;
                    break;
            }
        }

        private void LayoutGrid(IEnumerable<Node> nodes, int width, int height)
        {
            if (width == 0 || height == 0)
                return;

            IEnumerator<Node> enumerator = nodes.GetEnumerator();

            float relWidth = 1.0f / width;
            float relHeight = 1.0f / height;
           
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (enumerator.MoveNext())
                    {
                        Node next = enumerator.Current;
                        next.RelativeBounds = new RectangleF(x * relWidth, y * relHeight, relWidth, relHeight);
                    }
                }
            }
        }

        public void AllVisible(bool visible)
        {
            Node[] children = Children.ToArray();
            foreach (Node c in children)
            {
                if (c is AnimatedNode)
                {
                    ((AnimatedNode)c).SetAnimatedVisibility(visible);
                }
                else
                {
                    c.Visible = visible;
                }
            }
            RequestLayout();
        }

        public void ToggleVisiblity(int gridSpace)
        {
            Node[] children = Children.ToArray();
            if (gridSpace >= 0 && gridSpace < children.Length)
            {
                children[gridSpace].Visible = !children[gridSpace].Visible;
                RequestLayout();
            }
        }

        public void FullScreen(int gridSpaceNotToToggle)
        {
            Node[] children = Children.ToArray();
            if (gridSpaceNotToToggle >= 0 && gridSpaceNotToToggle < children.Length)
            {
                for (int i = 0; i < children.Length; i++)
                {
                    if (i == gridSpaceNotToToggle)
                    {
                        children[i].Visible = true;
                    }
                    else
                    {
                        children[i].Visible = false;
                    }
                }
                RequestLayout();
            }
        }
    }
}
