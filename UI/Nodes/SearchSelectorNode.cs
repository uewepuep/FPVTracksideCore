using Composition.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class SearchSelectorNode<S, T> : BorderPanelShadowNode where S : SearchableNode<T>
    {
        private ListNode<SearchableNode<T>> searchables;

        private TextEditNode searcherNode;

        private HeadingNode titleNode;

        public event Action<T> OnSelected;

        public Node buttonsContainer;

        private DockNode dockNode;

        public SearchSelectorNode(string title)
        {
            Scale(0.7f, 0.7f);

            dockNode = new DockNode(70, 40);
            AddChild(dockNode);

            titleNode = new HeadingNode(Theme.Current.InfoPanel, title);
            titleNode.RelativeBounds = new RectangleF(0, 0, 1, 0.5f);
            dockNode.Top.AddChild(titleNode);

            Node searchContainer = new Node();
            searchContainer.RelativeBounds = new RectangleF(0, 0.6f, 1, 0.4f);
            searchContainer.Scale(0.9f);
            dockNode.Top.AddChild(searchContainer);

            TextNode search = new TextNode("Search: ", Theme.Current.InfoPanel.Text.XNA);
            search.RelativeBounds = new RectangleF(0, 0, 0.3f, 1);
            search.Alignment = RectangleAlignment.CenterLeft;
            search.Scale(0.9f);
            searchContainer.AddChild(search);

            float searchRight = search.RelativeBounds.Right + 0.01f;

            ColorNode background = new ColorNode(Theme.Current.InfoPanel.Foreground.XNA);
            background.RelativeBounds = new RectangleF(searchRight, 0, 1 - searchRight, 1);
            searchContainer.AddChild(background);

            searcherNode = new TextEditNode("", Theme.Current.InfoPanel.Text.XNA);
            searcherNode.Alignment = RectangleAlignment.BottomLeft;
            searcherNode.TextChanged += SearcherNode_TextChanged;
            background.AddChild(searcherNode);

            searchables = new ListNode<SearchableNode<T>>(Theme.Current.ScrollBar.XNA);
            searchables.LayoutInvisibleItems = false;
            searchables.ItemHeight = ItemHeight();
            dockNode.Center.AddChild(searchables);

            buttonsContainer = new Node();
            buttonsContainer.Scale(0.95f, 0.8f);
            dockNode.Bottom.AddChild(buttonsContainer);

            TextButtonNode cancel = new TextButtonNode("Cancel", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
            cancel.RelativeBounds = new RectangleF(0, 0.94f, 0.2f, 0.05f);
            cancel.OnClick += (m) => { Dispose(); };

            buttonsContainer.AddChild(cancel);

            AlignHorizontally(0.1f, cancel, null, null, null, null);
        }

        public virtual int ItemHeight()
        {
            return 30;
        }

        private void SearcherNode_TextChanged(string search)
        {
            search = search.ToLower();

            foreach (SearchableNode<T> searchable in searchables.ChildrenOfType)
            {
                searchable.Visible = searchable.ToString().ToLower().Contains(search);
            }
            RequestLayout();
        }

        public void SetValues(IEnumerable<T> values)
        {
            searchables.ClearDisposeChildren();
            List<SearchableNode<T>> nodes = new List<SearchableNode<T>>();
            foreach (T value in values)
            {
                SearchableNode<T> searchable = Activator.CreateInstance(typeof(S), value) as SearchableNode<T>;
                searchable.OnClick += (mie) => { Select(searchable.Value); };
                nodes.Add(searchable);
            }
            foreach (SearchableNode<T> node in nodes.OrderBy(n => n.DisplayString()))
            {
                searchables.AddChild(node);
            }
            RequestLayout();
        }

        public void Select(T value)
        {
            OnSelected?.Invoke(value);
            Dispose();
        }
    }

    public class SearchableNode<T> : Node
    {
        public T Value { get; private set; }

        public event MouseInputDelegate OnClick;

        protected TextButtonNode textButtonNode;

        public SearchableNode(T value) 
        {
            Value = value;
            Init();
        }

        public virtual string SearchString()
        {
            return Value.ToString();
        }

        public virtual string DisplayString()
        {
            return Value.ToString();
        }

        public virtual void Init()
        {
            textButtonNode = new TextButtonNode(DisplayString(), Theme.Current.InfoPanel.Background.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
            textButtonNode.TextNode.Alignment = RectangleAlignment.BottomLeft;
            textButtonNode.OnClick += (mie) => OnClick?.Invoke(mie);
            AddChild(textButtonNode);
        }

        public override string ToString()
        {
            return DisplayString();
        }
    }
}
