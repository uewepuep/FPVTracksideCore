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

        private AlphaAnimatedNode tabContainer;
        private ColorNode tabBack;

        private Color tabButtonBackground;
        private Color hoverColor;
        private Color textColor;

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

        private bool tabVisible;

        public TabbedMultiNode(TimeSpan animation, Color tabBackground, Color tabButtonBackground, Color hover, Color text)
        {
            mapBack = new Dictionary<Node, TextButtonNode>();

            this.tabButtonBackground = tabButtonBackground;
            hoverColor = hover;
            textColor = text;

            tabContainer = new AlphaAnimatedNode();
            tabContainer.AnimationTime = animation;
            tabContainer.RelativeBounds = new RectangleF(0, 0, 1, 0.035f);

            tabBack = new ColorNode(tabBackground);
            tabContainer.AddChild(tabBack);

            multiNode = new MultiNode(animation);
            multiNode.OnShowChange += MultiNode_OnShowChange;
            multiNode.Direction = MultiNode.Directions.Horizontal;

            AddChild(tabContainer);
            AddChild(multiNode);

            SetTabsVisible(true);
        }

        public void SetTabsVisible(bool visible)
        {
            if (tabVisible != visible)
            {
                if (visible)
                {
                    multiNode.RelativeBounds = new RectangleF(0, tabContainer.RelativeBounds.Bottom, 1, 1 - tabContainer.RelativeBounds.Bottom);
                    tabContainer.SetAnimatedVisibility(true);
                }
                else
                {
                    tabContainer.SetAnimatedVisibility(false);
                    multiNode.RelativeBounds = new RectangleF(0, 0, 1, 1);
                }
                tabVisible = visible;
            }
        }

        public TextButtonNode AddTab(string text, Node node)
        {
            TextButtonNode textButtonNode = new TextButtonNode(text, tabButtonBackground, hoverColor, textColor);
            textButtonNode.OnClick += (mie) => { Show(node); };
            tabBack.AddChild(textButtonNode);

            multiNode.AddChild(node);

            mapBack.Add(node, textButtonNode);

            return textButtonNode;
        }

        public TextButtonNode AddTab(string text, Node node, MouseInputDelegate action)
        {
            TextButtonNode textButtonNode = new TextButtonNode(text, tabButtonBackground, hoverColor, textColor);
            textButtonNode.OnClick += action;
            tabBack.AddChild(textButtonNode);

            multiNode.AddChild(node);

            mapBack.Add(node, textButtonNode);

            return textButtonNode;
        }

        public override void Layout(Rectangle parentBounds)
        {
            AlignHorizontally(0.01f, tabBack.VisibleChildren.ToArray());

            base.Layout(parentBounds);
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
                    oldtbn.Background = tabButtonBackground;
                }
            }
            

            multiNode.Show(node);

            if (node != null)
            {
                TextButtonNode newtbn;
                if (mapBack.TryGetValue(node, out newtbn))
                {
                    newtbn.Background = hoverColor;
                }
            }
        }
    }
}
