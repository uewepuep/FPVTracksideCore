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
    public class TabbedMultiNode : Node
    {
        public Node Showing { get { return multiNode.Showing; } }

        private MultiNode multiNode;
        public Action<string, Node> OnTabChange { get; set; }

        public TabButtonsNode TabButtonsNode { get; private set; }  

        private Dictionary<Node, TextButtonNode> mapBack;

        public IEnumerable<KeyValuePair<TextButtonNode, Node>> Tabs
        {
            get
            {
                foreach (var kvp in mapBack)
                {
                    if (kvp.Value.Enabled)
                    {
                        yield return new KeyValuePair<TextButtonNode, Node>(kvp.Value, kvp.Key);
                    }
                }
            }
        }

        public TabbedMultiNode(TimeSpan animation, TabButtonsNode tabContainer)
        {
            mapBack = new Dictionary<Node, TextButtonNode>();

            multiNode = new MultiNode(animation);
            multiNode.OnShowChange += MultiNode_OnShowChange;
            multiNode.Direction = MultiNode.Directions.Horizontal;

            TabButtonsNode = tabContainer;

            AddChild(multiNode);
        }

        public TextButtonNode AddTab(string text, Node node)
        {
            TextButtonNode textButtonNode = TabButtonsNode.AddTab(text);
            textButtonNode.OnClick += (mie) => { Show(node); };

            if (node != null)
            {
                multiNode.AddChild(node);
                mapBack.Add(node, textButtonNode);
            }

            return textButtonNode;
        }

        public TextButtonNode AddTab(string text, Node node, MouseInputDelegate action)
        {
            TextButtonNode textButtonNode = TabButtonsNode.AddTab(text, action);
            multiNode.AddChild(node);

            mapBack.Add(node, textButtonNode);

            return textButtonNode;
        }

        public TextButtonNode InsertTab(int index, string text, Node node, MouseInputDelegate action)
        {
            TextButtonNode textButtonNode = AddTab(text, node, action);

            Node parent = textButtonNode.Parent;
            if (parent != null && parent.ChildCount >= index)
            {
                // Remove it and add it at the right index.
                parent.RemoveChild(textButtonNode);
                parent.AddChild(textButtonNode, index);
            }

            return textButtonNode;
        }

        private void MultiNode_OnShowChange(Node obj)
        {
            if (mapBack.TryGetValue(obj, out TextButtonNode textButtonNode)) 
            {
                OnTabChange?.Invoke(textButtonNode.Text, obj);
            }
        }

        public virtual void Show(Node node)
        {
            if (Showing != null)
            {
                TextButtonNode oldtbn;
                if (mapBack.TryGetValue(Showing, out oldtbn))
                {
                    oldtbn.Background = TabButtonsNode.Background;
                }
            }

            multiNode.Show(node);

            if (node != null)
            {
                TextButtonNode newtbn;
                if (mapBack.TryGetValue(node, out newtbn))
                {
                    newtbn.Background = TabButtonsNode.HoverCover;
                }
            }
        }
    }

    public class TabButtonsNode : Node
    {
        private ColorNode tabBack;

        public Color Background { get; private set; }
        public Color HoverCover { get; private set; }
        private Color textColor;

        public TabButtonsNode(Color tabBackground, Color tabButtonBackground, Color hover, Color text) 
        {
            Background = tabButtonBackground;
            HoverCover = hover;
            textColor = text;

            tabBack = new ColorNode(tabBackground);
            AddChild(tabBack);
        }

        public TextButtonNode AddTab(string text)
        {
            TextButtonNode textButtonNode = new TextButtonNode(text, Background, HoverCover, textColor);
            tabBack.AddChild(textButtonNode);
            return textButtonNode;
        }

        public TextButtonNode AddTab(string text, MouseInputDelegate action)
        {
            TextButtonNode textButtonNode = AddTab(text);
            textButtonNode.OnClick += action;
            return textButtonNode;
        }

        public override void Layout(RectangleF parentBounds)
        {
            AlignHorizontally(0.01f, tabBack.VisibleChildren.ToArray());
            base.Layout(parentBounds);
        }
    }
}
