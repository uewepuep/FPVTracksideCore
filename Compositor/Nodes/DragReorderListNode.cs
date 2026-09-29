using Composition.Input;
using Composition.Layers;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;

namespace Composition.Nodes
{
    // A row that can be dragged (anywhere on it, no separate handle/arrows) to reorder its
    // parent list, while still behaving like a normal clickable row when just clicked.
    // Replaces the old ItemNode<T> up/down-arrow approach, which also drew unreliably on Linux.
    public class DragReorderItemNode<T> : Node
    {
        public T Item { get; private set; }

        public event MouseInputDelegate OnClick;

        public ColorNode BackgroundNode { get; private set; }
        public HoverNode HoverNode { get; private set; }
        public TextNode TextNode { get; private set; }
        public TextNode NumberNode { get; private set; }
        public Node DragHandle { get; private set; }

        public string Text { get { return TextNode.Text; } set { TextNode.Text = value; } }

        private int number;
        public int Number
        {
            get { return number; }
            set
            {
                number = value;
                if (NumberNode != null)
                {
                    NumberNode.Text = number > 0 ? number + "." : "";
                }
            }
        }

        private bool pressed;
        private bool canReorder;

        public DragReorderItemNode(T item, TextNode content, Color background, Color hover, bool canReorder)
        {
            Item = item;
            this.canReorder = canReorder;

            BackgroundNode = new ColorNode(background);
            AddChild(BackgroundNode);

            Color textColor = content.Style.TextColor;

            float left = 0.02f;
            float right = 0.98f;

            if (canReorder)
            {
                const float handleWidth = 0.03f;
                DragHandle = BuildDragHandle(textColor);
                DragHandle.RelativeBounds = new RectangleF(right - handleWidth, 0.3f, handleWidth, 0.4f);
                AddChild(DragHandle);
                right -= handleWidth + 0.02f;

                NumberNode = new TextNode("", textColor);
                NumberNode.RelativeBounds = new RectangleF(left, 0.28f, 0.06f, 0.45f);
                NumberNode.Alignment = RectangleAlignment.CenterLeft;
                AddChild(NumberNode);
                left += 0.07f;
            }

            TextNode = content;
            TextNode.RelativeBounds = new RectangleF(left, 0.17f, right - left, 0.73f);
            TextNode.Alignment = RectangleAlignment.CenterLeft;
            AddChild(TextNode);

            HoverNode = new HoverNode(hover);
            AddChild(HoverNode);
        }

        private static Node BuildDragHandle(Color textColor)
        {
            // Three short bars (a plain "grip" icon), drawn from rectangles rather than a font
            // glyph - the previous up/down arrows used Unicode triangles that didn't always
            // render on Linux, so this avoids relying on the font having any particular glyph.
            Node handle = new Node();
            Color barColor = new Color(textColor, 0.5f);

            for (int i = 0; i < 3; i++)
            {
                ColorNode bar = new ColorNode(barColor);
                bar.RelativeBounds = new RectangleF(0, i * 0.44f, 1, 0.12f);
                handle.AddChild(bar);
            }

            return handle;
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (base.OnMouseInput(mouseInputEvent))
            {
                return true;
            }

            if (mouseInputEvent.Button == MouseButtons.Left)
            {
                if (mouseInputEvent.ButtonState == ButtonStates.Pressed)
                {
                    pressed = true;

                    if (canReorder)
                    {
                        GetLayer<DragLayer>()?.RegisterDrag(this, mouseInputEvent);
                    }

                    return true;
                }

                if (mouseInputEvent.ButtonState == ButtonStates.Released && pressed)
                {
                    pressed = false;
                    OnClick?.Invoke(mouseInputEvent);
                    return true;
                }
            }

            if (mouseInputEvent is MouseInputLeaveEvent)
            {
                pressed = false;
            }

            return false;
        }
    }

    // A simple, self-contained, drag-reorderable list. Give it items and a way to render each
    // one; dragging a row anywhere onto another reorders them, no arrow buttons involved.
    public class DragReorderListNode<T> : Node
    {
        public event Action<IEnumerable<T>> OnReordered;
        public event Action<T, MouseInputEvent> OnItemClicked;

        public IEnumerable<T> Items { get { return list.ChildrenOfType.Select(r => r.Item); } }

        public ListNode<DragReorderItemNode<T>> List { get { return list; } }

        public int ItemHeight { get { return list.ItemHeight; } set { list.ItemHeight = value; } }

        public bool CanReOrder { get; set; }

        private readonly ListNode<DragReorderItemNode<T>> list;
        private readonly Func<T, TextNode> contentBuilder;
        private readonly Color background;
        private readonly Color hover;

        public DragReorderListNode(Func<T, TextNode> contentBuilder, Color background, Color hover, Color scrollColor)
        {
            this.contentBuilder = contentBuilder;
            this.background = background;
            this.hover = hover;
            CanReOrder = true;

            list = new ListNode<DragReorderItemNode<T>>(scrollColor);
            list.ItemHeight = 30;
            AddChild(list);
        }

        public void SetItems(IEnumerable<T> items)
        {
            list.ClearDisposeChildren();

            foreach (T item in items)
            {
                DragReorderItemNode<T> row = new DragReorderItemNode<T>(item, contentBuilder(item), background, hover, CanReOrder);
                T captured = item;
                row.OnClick += (mie) => OnItemClicked?.Invoke(captured, mie);
                list.AddChild(row);
            }

            Renumber();
            list.RequestLayout();
        }

        private void Renumber()
        {
            int i = 1;
            foreach (DragReorderItemNode<T> row in list.ChildrenOfType)
            {
                row.Number = i;
                i++;
            }
        }

        public override bool OnDrop(MouseInputEvent finalInputEvent, Node node)
        {
            DragReorderItemNode<T> dropped = node as DragReorderItemNode<T>;
            if (dropped != null && list.ChildrenOfType.Contains(dropped))
            {
                Point adjusted = finalInputEvent.Position;
                adjusted.Y += (int)list.Scroller.CurrentScrollPixels;

                List<DragReorderItemNode<T>> rows = list.ChildrenOfType.ToList();
                int index = rows.Count - 1;

                foreach (DragReorderItemNode<T> other in rows)
                {
                    if (other.Bounds.Contains(adjusted))
                    {
                        index = rows.IndexOf(other);
                        break;
                    }
                }

                rows.Remove(dropped);
                rows.Insert(index, dropped);

                list.RemoveChild(list.ChildrenOfType.ToArray());
                foreach (DragReorderItemNode<T> row in rows)
                {
                    list.AddChild(row);
                }
                Renumber();
                list.RequestLayout();

                OnReordered?.Invoke(rows.Select(r => r.Item));
                return true;
            }

            return base.OnDrop(finalInputEvent, node);
        }
    }
}
