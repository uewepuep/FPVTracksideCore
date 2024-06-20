using Composition.Input;
using Composition.Layers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows.Markup;
using Tools;

namespace Composition.Nodes
{
    public class BaseObjectEditorNode<T> : Node
    {
        public T Single { get { return Objects.FirstOrDefault(); } }

        public List<T> Objects { get; set; }

        public Type Type { get { return typeof(T); } }

        private T selected;
        public T Selected { get { return selected; } set { SetSelected(value); } }

        protected ListNode<ItemNode<T>> multiItemBox;
        protected ListNode<PropertyNode<T>> objectProperties;

        public Color ButtonBackground { get; set; }
        public Color ButtonHover { get; set; }
        public Color TextColor { get; set; }
        public string Text { get { return heading.Text; } set { heading.Text = value; } }

        public event Action<BaseObjectEditorNode<T>> OnOK;
        public event Action<BaseObjectEditorNode<T>> OnCancel;
        public event Action<BaseObjectEditorNode<T>> OnRefreshList;

        protected Node container;
        protected Node left;
        protected Node right;

        protected TextButtonNode okButton;
        protected TextButtonNode cancelButton;
        protected TextButtonNode addButton;
        protected TextButtonNode removeButton;
        protected TextNode heading;

        protected Node buttonContainer;

        protected TextNode itemName;

        protected Node root;

        protected ColorNode selectedHover;

        public bool CanEdit { get; set; }
        public bool CanReOrder { get; set; }

        public string TypeName { get; private set; }

        public virtual bool GroupItems { get { return false; } }

        private List<Change> changes;

        public bool NeedsRestart { get { return changes.Any(c => c.NeedsRestart); } }

        public IEnumerable<PropertyNode<T>> PropertyNodes { get { return objectProperties.ChildrenOfType; } }

        private bool needsDispose;

        public bool Clip
        {
            set
            {
                objectProperties.Clip = value;
                multiItemBox.Clip = value;
            }
        }

        public BaseObjectEditorNode(Color buttonBackground, Color buttonHover, Color textColor, Color scrollColor, bool hasButtons = true)
        {
            changes = new List<Change>();

            CanEdit = true;
            CanReOrder = true;

            root = new Node();
            AddChild(root);

            selectedHover = new ColorNode(buttonHover);
            AddChild(selectedHover);

            ButtonBackground = buttonBackground;
            ButtonHover = buttonHover;
            TextColor = textColor;

            DisplayNameAttribute dna = Type.GetCustomAttribute<DisplayNameAttribute>();
            if (dna != null)
            {
                TypeName = dna.DisplayName;
            }
            else
            {
                TypeName = Type.Name.CamelCaseToHuman();
            }

            heading = new TextNode(TypeName + " Editor", TextColor);
            heading.RelativeBounds = new RectangleF(0, 0, 1, 0.05f);
            root.AddChild(heading);

            container = new Node();
            container.RelativeBounds = new RectangleF(0, heading.RelativeBounds.Bottom, 1, 1 - heading.RelativeBounds.Bottom);
            root.AddChild(container);

            left = new Node();
            container.AddChild(left);

            right = new Node();
            container.AddChild(right);

            multiItemBox = new ListNode<ItemNode<T>>(scrollColor);
            multiItemBox.ItemHeight = 30;
            left.AddChild(multiItemBox);

            itemName = new TextNode("", TextColor);
            itemName.RelativeBounds = new RectangleF(0, 0, 1, 0.07f);
            itemName.Alignment = RectangleAlignment.BottomLeft;
            right.AddChild(itemName);

            objectProperties = new ListNode<PropertyNode<T>>(scrollColor);
            objectProperties.ItemHeight = 20;
            objectProperties.LayoutInvisibleItems = false;
            right.AddChild(objectProperties);

            SplitHorizontally(left, right, 0.3f);
            container.RequestLayout();

            buttonContainer = new Node();
            right.AddChild(buttonContainer);

            SetButtonsHeight(0.05f);

            okButton = new TextButtonNode("Ok", ButtonBackground, ButtonHover, TextColor);
            cancelButton = new TextButtonNode("Cancel", ButtonBackground, ButtonHover, TextColor);
            addButton = new TextButtonNode("Add", ButtonBackground, ButtonHover, TextColor);
            removeButton = new TextButtonNode("Remove", ButtonBackground, ButtonHover, TextColor);

            buttonContainer.AddChild(addButton, removeButton, cancelButton, okButton);
            AlignHorizontally(0.05f, removeButton, addButton, null, cancelButton, okButton);

            okButton.ButtonNode.NodeName = "OkButton";

            heading.Scale(0.9f);
            itemName.Scale(0.5f);

            cancelButton.OnClick += CancelButton_OnClick;
            okButton.OnClick += OkButton_OnClick;
            addButton.OnClick += AddOnClick;
            removeButton.OnClick += Remove;

            buttonContainer.Visible = hasButtons;
        }
        public override void Dispose()
        {
            base.Dispose();
            if (selectedHover != null)
            {
                selectedHover.Dispose();
                selectedHover = null;
            }
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (needsDispose)
            {
                id.CleanUp(this);
            }
            base.Draw(id, parentAlpha);
        }

        public void SetHeadingText(string text)
        {
            heading.Text = text;
        }

        public void SetItemNameText(string text)
        {
            itemName.Text = text;
        }

        public void SetButtonsHeight(float height)
        {
            SetHeadingButtonsHeight(heading.RelativeBounds.Height, height);
        }

        public void SetHeadingButtonsHeight(float headingHeight, float buttonHeight)
        {
            heading.RelativeBounds = new RectangleF(0, 0, 1, headingHeight);
            container.RelativeBounds = new RectangleF(0, heading.RelativeBounds.Bottom, 1, 1 - heading.RelativeBounds.Bottom);
            buttonContainer.RelativeBounds = new RectangleF(0, 1 - buttonHeight, 1, buttonHeight);
            objectProperties.RelativeBounds = new RectangleF(0, itemName.RelativeBounds.Bottom, 1, buttonContainer.RelativeBounds.Y - itemName.RelativeBounds.Bottom);
            RequestLayout();
        }

        public void SetObject(T toEdit, bool addRemove = false, bool cancelButton = true)
        {
            SetObjects(new T[] { toEdit }, addRemove, cancelButton);
        }

        public virtual void SetObjects(IEnumerable<T> toEdit, bool addRemove = false, bool cancelButton = true)
        {
            Objects = Order(toEdit).ToList();

            if (toEdit.Count() > 1 || addRemove)
            {
                RefreshList();
                SplitHorizontally(left, right, 0.3f);

                itemName.Visible = true;
            }
            else
            {
                left.Visible = false;
                right.RelativeBounds = new RectangleF(0, 0, 1, 1);

                if (toEdit.Any())
                {
                    SetSelected(toEdit.First());
                }

                itemName.Visible = false;
            }

            if (Type.GetConstructor(Type.EmptyTypes) != null || Type.IsValueType || addRemove)
            {
                addButton.Visible = true;
            }
            else
            {
                addButton.Visible = false;
            }

            if (!addRemove)
            {
                addButton.Visible = false;
                removeButton.Visible = false;
            }

            if (!cancelButton)
            {
                this.cancelButton.Visible = false;
            }

            SetSelected(Objects.FirstOrDefault());
        }

        public void RefreshList()
        {
            selectedHover?.Remove();
            multiItemBox.ClearDisposeChildren();

            var grouped = Order(Objects).GroupBy(o => ItemToGroupString(o));
            foreach (var group in grouped)
            {
                if (GroupItems)
                {
                    Node container = new Node();
                    TextNode textNode = new TextNode(group.Key, TextColor);
                    textNode.Scale(0.8f);
                    container.AddChild(textNode);
                    multiItemBox.AddChild(container);
                }

                bool moreThanOne = group.Count() > 1;
                foreach (T t in Order(group))
                {
                    if (!IsVisible(t))
                        continue;

                    ItemNode<T> tbn = new ItemNode<T>(t, ItemToString(t), ButtonBackground, ButtonHover, TextColor, CanReOrder && moreThanOne);
                    tbn.OnClick += (mie) => { SetSelected(t); };
                    tbn.OnUpClick += (mie) => { MoveUp(tbn); };
                    tbn.OnDownClick += (mie) => { MoveDown(tbn); };
                    multiItemBox.AddChild(tbn);
                }

                if (GroupItems)
                {
                    // spacer
                    multiItemBox.AddChild(new SpacerNode());
                }
            }
            multiItemBox.RequestLayout();
            RequestLayout();

            OnRefreshList?.Invoke(this);
        }

        public void RefreshSelectedObjectProperties()
        {
            if (selected != null)
            {
                foreach (var property in PropertyNodes)
                {
                    property.RequestUpdateFromObject();
                }
            }
        }

        public virtual bool IsVisible(T t)
        {
            return true;
        }

        public virtual IEnumerable<T> Order(IEnumerable<T> ts)
        {
            return ts;
        }

        private void MoveUp(ItemNode<T> tbn)
        {
            try
            {
                T item = tbn.Item;

                int index = Objects.IndexOf(item);
                index--;
                if (index < 0)
                    index = 0;

                Objects.Remove(item);
                Objects.Insert(index, item);
                multiItemBox.RequestLayout();
                RefreshList();
            }
            catch
            {

            }
        }

        private void MoveDown(ItemNode<T> tbn)
        {
            try
            {
                T item = tbn.Item;

                int index = Objects.IndexOf(item);
                index++;

                if (index >= Objects.Count)
                {
                    index = Objects.Count - 1;
                }

                Objects.Remove(item);
                Objects.Insert(index, item);

                RefreshList();
            }
            catch
            {

            }
        }

        public ItemNode<T> GetItemNode(T t)
        {
            foreach (var tbi in multiItemBox.ChildrenOfType)
            {
                if (tbi.Item.Equals(t))
                {
                    return tbi;
                }
            }
            return null;
        }

        public virtual void ClearSelected()
        {
            selectedHover.Remove();
            objectProperties.ClearDisposeChildren();
            itemName.Text = "";
        }

        protected virtual IEnumerable<PropertyInfo> GetPropertyInfos(T obj)
        {
            // A whole lot of stuff to just make sure Base object fields are first in the list.
            IEnumerable<Type> orderedBaseTypes = obj.GetType().GetBaseTypes();
            List<PropertyInfo> propertyInfos = new List<PropertyInfo>();
            foreach (Type type in orderedBaseTypes)
            {
                IEnumerable<PropertyInfo> pis = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(x => x.MetadataToken);
                propertyInfos.AddRange(pis);
            }
            return propertyInfos;
        }

        protected virtual void SetSelected(T obj)
        {
            ClearSelected();

            if (!IsVisible(obj))
                return;

            if (obj != null)
            {
                ItemNode<T> node = GetItemNode(obj);
                if (node != null)
                {
                    node.AddChild(selectedHover);
                }

                itemName.Text = obj.ToString();

                IEnumerable<PropertyInfo> propertyInfos = GetPropertyInfos(obj);
                CreatePropertyNodes(obj, propertyInfos);

                selected = obj;
            }
            UpdateCategoryVisibilities();

            RequestLayout();
            objectProperties.RequestLayout();
            multiItemBox.RequestLayout();
        }

        protected virtual void CreatePropertyNodes(T obj, IEnumerable<PropertyInfo> propertyInfos)
        {
            string lastCat = "";
            foreach (PropertyInfo pi in propertyInfos)
            {
                if (pi.GetAccessors(true)[0].IsStatic)
                    continue;

                string category = "";

                CategoryAttribute ca = pi.GetCustomAttribute<CategoryAttribute>();
                if (ca != null)
                {
                    category = ca.Category;
                }

                IEnumerable<PropertyNode<T>> newNodes = CreatePropertyNodes(obj, pi);
                foreach (var newNode in newNodes)
                {
                    if (newNode == null)
                        continue;

                    if (newNode.Category != "")
                    {
                        category = newNode.Category;
                    }

                    if (category != lastCat)
                    {
                        if (objectProperties.ChildCount > 0)
                        {
                            SpacerNode spacer = new SpacerNode();
                            objectProperties.AddChild(spacer);
                        }

                        if (category != "")
                        {
                            CategoryNode categoryTitle = new CategoryNode(category, TextColor);
                            objectProperties.AddChild(categoryTitle);
                        }
                        
                        lastCat = category;
                    }
                    AddPropertyNode(newNode);
                }
            }
            objectProperties.RequestLayout();
        }

        protected void AddPropertyNode(PropertyNode<T> newNode)
        {
            if (newNode != null)
            {
                objectProperties.AddChild(newNode);
                newNode.OnChanged += ChildValueChanged;
                newNode.OnFocusNext += MoveFocusNext;
            }
        }

        protected virtual IEnumerable<PropertyNode<T>> CreatePropertyNodes(T obj, PropertyInfo pi)
        {
            yield return CreatePropertyNode(obj, pi);
        }

        protected virtual PropertyNode<T> CreatePropertyNode(T obj, PropertyInfo pi)
        {
            BrowsableAttribute ba = pi.GetCustomAttribute<BrowsableAttribute>();
            if (ba != null && !ba.Browsable)
                return null;

            ReadOnlyAttribute ro = pi.GetCustomAttribute<ReadOnlyAttribute>();
            if (ro != null && ro.IsReadOnly)
                return null;

            PropertyNode<T> newNode = null;

            bool HasPublicSetter = pi.GetSetMethod() != null;

            if (!CanEdit || !HasPublicSetter)
            {
                newNode = new StaticTextPropertyNode<T>(obj, pi, TextColor);
                return newNode;
            }

            if (pi.PropertyType == typeof(string))
            {
                if (pi.Name.ToLower().Contains("password"))
                {
                    newNode = new PasswordPropertyNode<T>(obj,  pi, ButtonBackground, TextColor);
                }
                else
                {
                    newNode = new TextPropertyNode<T>(obj, pi, ButtonBackground, TextColor);
                }
            }
            else if (pi.PropertyType == typeof(int) || pi.PropertyType == typeof(double) || pi.PropertyType == typeof(float) || pi.PropertyType == typeof(byte))
            {
                if (pi.Name.ToLower().Contains("percent"))
                {
                    newNode = new PercentagePropertyNode<T>(obj, pi, ButtonBackground, TextColor);
                }
                else
                {
                    newNode = new NumberPropertyNode<T>(obj, pi, ButtonBackground, TextColor);
                }
            }
            else if (pi.PropertyType == typeof(int[]))
            {
                newNode = new NumberArrayPropertyNode<T>(obj, pi, ButtonBackground, TextColor);
            }
            else if (pi.PropertyType == typeof(TimeSpan))
            {
                newNode = new TimeSpanPropertyNode<T>(obj, pi, ButtonBackground, TextColor);
            }
            else if (pi.PropertyType.IsEnum)
            {
                newNode = new EnumPropertyNode<T>(obj, pi, ButtonBackground, TextColor, ButtonHover);
            }
            else if (pi.PropertyType == typeof(bool))
            {
                newNode = new BoolPropertyNode<T>(obj, pi, TextColor, ButtonHover);
            }
            else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(pi.PropertyType))
            {
                newNode = null;
            }
            else if (pi.PropertyType == typeof(DateTime))
            {
                newNode = new DateTimePropertyNode<T>(obj, pi, ButtonBackground, TextColor);
            }
            else
            {
                newNode = new StaticTextPropertyNode<T>(obj, pi, TextColor);
            }

            return newNode;
        }

        private void UpdateCategoryVisibilities()
        {
            bool anyVisible = false;
            CategoryNode lastCategoryNode = null;

            foreach (Node n in objectProperties.Children)
            {
                CategoryNode cn = n as CategoryNode;
                if (cn != null)
                {
                    if (lastCategoryNode != null)
                    {
                        lastCategoryNode.Visible = anyVisible;
                    }

                    lastCategoryNode = cn;
                    anyVisible = false;
                }
                else
                {
                    if (n.Visible && !(n is SpacerNode))
                    {
                        anyVisible = true;
                    }
                }
            }

            if (lastCategoryNode != null)
            {
                lastCategoryNode.Visible = anyVisible;
            }
            objectProperties.RequestLayout();
        }

        protected virtual void ChildValueChanged(Change newChange)
        {
            if (cancelButton.Visible)
            {
                Change existing = changes.FirstOrDefault(c => c.PropertyInfo == newChange.PropertyInfo && c.Object == newChange.Object);

                if (existing != null)
                {
                    changes.Remove(existing);
                }

                changes.Add(newChange);
            }

            foreach (ItemNode<T> text in multiItemBox.ChildrenOfType)
            {
                text.Text = ItemToString(text.Item);
            }

            foreach (PropertyNode<T> p in objectProperties.ChildrenOfType)
            {
                p.RequestUpdateFromObject();
            }
            multiItemBox.RequestLayout();

            UpdateCategoryVisibilities();
        }

        protected virtual string ItemToString(T item)
        {
            if (item == null)
            {
                return "";
            }

            string name = item.ToString();

            if (name == null)
            {
                name = item.GetType().Name;
            }

            int max = 28;
            if (name.Length > max)
            {
                name = name.Substring(0, max);
            }

            return name;
        }

        protected virtual string ItemToGroupString(T item)
        {
            return "";
        }

        protected virtual void Remove(MouseInputEvent mie)
        {
            if (Selected != null)
            {
                GetLayer<PopupLayer>()?.PopupConfirmation("Remove '" + Selected.ToString()+ "'?", () =>
                {
                    Objects.Remove(Selected);
                    RefreshList();
                    if (Objects.Any())
                    {
                        SetSelected(Objects.FirstOrDefault());
                    }
                    else
                    {
                        ClearSelected();
                    }
                });
            }
        }

        protected virtual void AddOnClick(MouseInputEvent mie)
        {
            T t = CreateT();
            if (t != null)
            {
                AddNew(t);
            }
        }

        protected virtual void AddNew(T t)
        {
            Objects.Add(t);
            RefreshList();
            SetSelected(t);
            objectProperties.RequestLayout();
        }

        protected virtual T CreateT()
        {
            return Activator.CreateInstance<T>();
        }

        protected void OkButton_OnClick(MouseInputEvent mie)
        {
            if (CompositorLayer != null)
            {
                CompositorLayer.FocusedNode = null;
            }

            OnOK?.Invoke(this);

            needsDispose = true;
        }

        protected void CancelButton_OnClick(MouseInputEvent mie)
        {
            foreach (Change c in changes)
            {
                try
                {
                    c.Cancel();
                }
                catch (Exception e)
                {
                    Tools.Logger.UI.LogException(this, e);
                }
            }

            OnCancel?.Invoke(this);
            needsDispose = true;
        }

        public override bool OnDrop(MouseInputEvent finalInputEvent, Node node)
        {
            int y = finalInputEvent.Position.Y;

            y += (int)multiItemBox.Scroller.CurrentScrollPixels;

            Point adjustedMouse = finalInputEvent.Position;
            adjustedMouse.Y = y;

            ItemNode<T> dropped = node as ItemNode<T>;
            if (dropped != null && left.Contains(finalInputEvent.Position))
            {
                int index = Objects.Count - 1;
                foreach (ItemNode<T> other in multiItemBox.ChildrenOfType)
                {
                    if (other.Bounds.Contains(adjustedMouse))
                    {
                        index = Objects.IndexOf(other.Item);
                        break;
                    }
                }

                Objects.Remove(dropped.Item);
                Objects.Insert(index, dropped.Item);


                RefreshList();
            }
            return base.OnDrop(finalInputEvent, node);
        }

        public void MoveFocusNext(PropertyNode<T> current)
        {
            bool found = false;

            // Try and find the next spot
            foreach (PropertyNode<T> node in objectProperties.Children.OfType<PropertyNode<T>>())
            {
                if (found)
                {
                    if (node.Focus())
                    {
                        return;
                    }
                }
                else if (node == current)
                {
                    found = true;
                }
            }

            // Now just find the first..
            foreach (PropertyNode<T> node in objectProperties.Children.OfType<PropertyNode<T>>())
            {
                if (node.Focus())
                {
                    return;
                }
            }
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            base.OnMouseInput(mouseInputEvent);
            return true;
        }

    }

    public class Change
    {
        public object Object { get; private set; }
        public PropertyInfo PropertyInfo { get; private set; }
        public object OriginalValue { get; private set; }
        public object NewValue { get; private set; }
        public bool NeedsRestart 
        {
            get
            {
                return PropertyInfo.GetCustomAttribute<NeedsRestartAttribute>() != null;
            }
        }

        public Change(object target, PropertyInfo propertyInfo, object original, object newValue)
        {
            Object = target;
            PropertyInfo = propertyInfo;
            OriginalValue = original;
            NewValue = newValue;
        }

        public void Cancel()
        {
            PropertyInfo.SetValue(Object, OriginalValue);
        }
    }


    public class PropertyNode<T> : Node
    {
        public T Object { get; private set; }
        public PropertyInfo PropertyInfo { get; private set; }

        public event Action<Change> OnChanged;
        public event Action<PropertyNode<T>> OnFocusNext;

        private object originalValue;

        public string Category { get; set; }

        public PropertyNode(T obj, PropertyInfo pi)
        {
            Category = "";

            PropertyInfo = pi;
            Object = obj;

            if (pi != null)
            {
                originalValue = pi.GetValue(Object, null);
            }
        }

        public void RequestUpdateFromObject()
        {
            if (!Children.Any(c => c.HasFocus || c.Children.Any(c2 => c2.HasFocus)))
            {
                UpdateFromObject();
            }
        }

        public virtual void UpdateFromObject()
        {
        }

        protected virtual void SetValue(object value)
        {
            try
            {
                PropertyInfo.SetValue(Object, value);

                if (originalValue != value)
                {
                    OnChanged?.Invoke(new Change(Object, PropertyInfo, originalValue, value));
                }

                RequestRedraw();
            }
            catch (Exception ex) 
            {
                Logger.UI.LogException(this, ex);
            }
        }

        public virtual bool Focus()
        {
            return false;
        }

        public void FocusNext()
        {
            OnFocusNext?.Invoke(this);
        }
    }

    public class NamedPropertyNode<T> : PropertyNode<T>
    {
        public TextNode Name { get; private set; }

        public NamedPropertyNode(T obj, PropertyInfo pi, Color textColor) : base(obj, pi)
        {
            string name = "";
            if (pi != null)
            {
                DisplayNameAttribute dna = pi.GetCustomAttribute<DisplayNameAttribute>();
                if (dna != null)
                {
                    name = dna.DisplayName;
                }
                else
                {
                    name = pi.Name.CamelCaseToHuman();
                }
            }

            Name = new TextNode(name, textColor);
            Name.Alignment = RectangleAlignment.BottomLeft;
            AddChild(Name);
        }
    }

    public class SubPropertyNode<T, J> : PropertyNode<T>
    {
        public PropertyNode<J> SubProperty { get; private set; }

        public SubPropertyNode(T obj, PropertyInfo pi, PropertyNode<J> subProperty) : base(obj, pi)
        {
            SubProperty = subProperty;
            AddChild(SubProperty);
        }
    }

    public class CategoryNode : Node
    {
        public TextNode Title { get; private set; }
        public CategoryNode(string text, Color textColor)
        {
            Title = new TextNode(text, textColor);
            Title.Alignment = RectangleAlignment.CenterLeft;
            Title.Style.Bold = true; 

            AddChild(Title);
        }
    }

    public class SpacerNode : Node
    {
        public SpacerNode()
        {
        }
    }

    public class StaticTextPropertyNode<T> : NamedPropertyNode<T>
    {
        public TextNode Value { get; private set; }

        public StaticTextPropertyNode(T obj, PropertyInfo pi, Color textColor)
            : base(obj, pi, textColor)
        {
            Value = new TextNode("", textColor);
            Value.Alignment = RectangleAlignment.BottomLeft;
            AddChild(Value);

            AlignHorizontally(0.01f, Children.ToArray());

            RequestUpdateFromObject();
        }

        public override void UpdateFromObject()
        {
            object value = PropertyInfo.GetValue(Object, null);

            Value.Text = ValueToString(value);
        }

        protected override void SetValue(object value)
        {
            try
            {
                Value.Text = ValueToString(value);
                base.SetValue(value);
                RequestRedraw();
            }
            catch (Exception ex) 
            {
                Logger.Input.LogException(this, ex);
            }
        }

        public virtual string ValueToString(object value)
        {
            if (value == null)
            {
                return "";
            }
            return value.ToString();
        }
    }

    public class TextPropertyNode<T> : NamedPropertyNode<T>
    {
        public TextEditNode Value { get; private set; }

        public TextPropertyNode(T obj, PropertyInfo pi, Color textBackground, Color textColor)
            : base(obj, pi, textColor)
        {
            ColorNode textBackgroundNode = new ColorNode(textBackground);
            AddChild(textBackgroundNode);

            Value = new TextEditNode("", textColor);
            Value.TextChanged += SetValue;
            Value.Alignment = RectangleAlignment.BottomLeft;
            Value.OnTab += FocusNext;
            Value.OnReturn += FocusNext;

            textBackgroundNode.AddChild(Value);

            AlignHorizontally(0.01f, Children.ToArray());

            Value.OnFocusChanged += (b) => 
            {
                if (!b)
                {
                    SetValue(Value.Text);
                    RequestUpdateFromObject();
                }
            };

            RequestUpdateFromObject();
        }

        public override void UpdateFromObject()
        {
            object value = PropertyInfo.GetValue(Object, null);

            if (value != null)
            {
                Value.Text = ValueToString(value);
            }
        }

        public override bool Focus()
        {
            Value.HasFocus = true;
            return true;
        }

        public virtual string ValueToString(object value)
        {
            return value.ToString();
        }
    }

    public class PasswordPropertyNode<T> : NamedPropertyNode<T>
    {
        public PasswordNode Value { get; private set; }

        public PasswordPropertyNode(T obj, PropertyInfo pi, Color background, Color textColor)
            : base(obj, pi, textColor)
        {
            ColorNode textBackgroundNode = new ColorNode(background);
            AddChild(textBackgroundNode);

            Value = new PasswordNode("", textColor);
            Value.TextChanged += SetValue;
            Value.Alignment = RectangleAlignment.BottomLeft;
            Value.OnTab += FocusNext;
            Value.OnReturn += FocusNext;

            textBackgroundNode.AddChild(Value);

            AlignHorizontally(0.01f, Children.ToArray());

            Value.OnFocusChanged += (b) =>
            {
                if (!b)
                {
                    SetValue(Value.Text);
                    RequestUpdateFromObject();
                }
            };

            RequestUpdateFromObject();
        }

        public override void UpdateFromObject()
        {
            object value = PropertyInfo.GetValue(Object, null);

            if (value != null)
            {
                Value.Text = value.ToString();
            }
        }

        public override bool Focus()
        {
            Value.HasFocus = true;
            return true;
        }
    }

    public class FilenamePropertyNode<T> : TextPropertyNode<T>
    {
        public TextButtonNode FileChooser { get; set; }

        public string FileExtension { get; set; }

        public FilenamePropertyNode(T obj, PropertyInfo pi, Color background, Color hover, Color textColor, string fileExtension) 
            : base(obj, pi, background, textColor)
        {
            FileExtension = fileExtension;

            FileChooser = new TextButtonNode("Choose", background, hover, textColor);
            AddChild(FileChooser);

            RectangleF valueBounds = Value.RelativeBounds;
            float right = valueBounds.Right;
            valueBounds.Width *= 0.83f;
            Value.RelativeBounds = valueBounds;

            float start = valueBounds.Right + 0.02f;

            FileChooser.RelativeBounds = new RectangleF(start, valueBounds.Y, right - start, valueBounds.Height);

            FileChooser.OnClick += FileChooser_OnClick;
        }

        private void FileChooser_OnClick(MouseInputEvent mie)
        {
            SetValue(PlatformTools.OpenFileDialog("Choose File", FileExtension));
        }
    }

    public class NumberPropertyNode<T> : TextPropertyNode<T>
    {
        public NumberPropertyNode(T obj, PropertyInfo pi, Color textBackground, Color textColor) 
            : base(obj, pi, textBackground, textColor)
        {
        }

        public override void UpdateFromObject()
        {
            object value = PropertyInfo.GetValue(Object, null);
            Value.Text = ValueToString(value);
        }

        public override string ValueToString(object value)
        {
            if (value != null)
            {
                string text;

                if (PropertyInfo.PropertyType == typeof(double))
                {
                    text = ((double)value).ToString("0.00");
                }
                else if (PropertyInfo.PropertyType == typeof(float))
                {
                    text = ((float)value).ToString("0.00");
                }
                else
                {
                    text = value.ToString();
                }

                return text;
            }
            return "";
        }

        protected override void SetValue(object value)
        {
            try
            {
                string str = value.ToString();
                double d;
                int i;
                byte b;

                if (PropertyInfo.PropertyType == typeof(double) && double.TryParse(str, out d))
                {
                    base.SetValue(d);
                }
                if (PropertyInfo.PropertyType == typeof(float) && double.TryParse(str, out d))
                {
                    base.SetValue((float)d);
                }
                else if (PropertyInfo.PropertyType == typeof(int) && int.TryParse(str, out i))
                {
                    base.SetValue(i);
                }
                else if (PropertyInfo.PropertyType == typeof(byte) && byte.TryParse(str, out b))
                {
                    base.SetValue(b);
                }
            }
            catch (Exception ex)
            {
                Logger.Input.LogException(this, ex);
                UpdateFromObject();
            }
        }
    }

    public class PercentagePropertyNode<T> : NumberPropertyNode<T>
    {
        public PercentagePropertyNode(T obj, PropertyInfo pi, Color textBackground, Color textColor) 
            : base(obj, pi, textBackground, textColor)
        {
        }

        public override void UpdateFromObject()
        {
            object value = PropertyInfo.GetValue(Object, null);

            if (value != null)
            {
                string text = value.ToString();
                Value.Text = text + "%";
            }
        }

        protected override void SetValue(object value)
        {
            try
            {
                string noPercentage = value.ToString().Replace("%", "");
                base.SetValue(noPercentage);
            }
            catch (Exception ex)
            {
                Logger.Input.LogException(this, ex);
                UpdateFromObject();
            }
        }
    }

    public class NumberArrayPropertyNode<T> : TextPropertyNode<T>
    {
        public NumberArrayPropertyNode(T obj, PropertyInfo pi, Color textBackground, Color textColor)
            : base(obj, pi, textBackground, textColor)
        {
        }

        public override void UpdateFromObject()
        {
            int[] value = (int[])PropertyInfo.GetValue(Object, null);
            Value.Text = string.Join(", ", value.Select(i => i.ToString()));
        }


        protected override void SetValue(object value)
        {
            try
            {
                string[] splits = value.ToString().Split(',');

                int i;

                List<int> values = new List<int>();
                foreach (string str in splits)
                {
                    if (int.TryParse(str, out i))
                    {
                        values.Add(i);
                    }
                    else
                    {
                        return;
                    }
                }

                base.SetValue(values.ToArray());
            }
            catch (Exception ex)
            {
                Logger.Input.LogException(this, ex);
                UpdateFromObject();
            }
        }
    }

    public class TimeSpanPropertyNode<T> : TextPropertyNode<T>
    {
        public TimeSpanPropertyNode(T obj, PropertyInfo pi, Color textBackground, Color textColor)
            : base(obj, pi, textBackground, textColor)
        {
        }

        protected override void SetValue(object value)
        {
            string str = value.ToString();

            try
            {
                TimeSpan timeSpan;
                double d;
                if (double.TryParse(str, out d))
                {
                    timeSpan = TimeSpan.FromSeconds(d);
                    base.SetValue(timeSpan);
                }
                else if (TimeSpan.TryParse(str, out timeSpan))
                {
                    base.SetValue(timeSpan);
                }
            }
            catch (Exception ex) 
            {
                Logger.Input.LogException(this, ex);
            }
        }

        public override string ValueToString(object obj)
        {
            if (obj == null)
                return "";

            TimeSpan value = (TimeSpan)obj;
            if (value != default(TimeSpan))
            {
                return value.TotalSeconds.ToString();
            }
            else
            {
                return "";
            }
        }
    }

    public class DateTimePropertyNode<T> : TextPropertyNode<T>
    {
        public bool ShowTime { get; private set; }

        public DateTimePropertyNode(T obj, PropertyInfo pi, Color textBackground, Color textColor)
            : base(obj, pi, textBackground, textColor)
        {
            bool dateOnly = pi.GetCustomAttribute<DateOnlyAttribute>() != null;
            SetShowTime(!dateOnly);
        }

        public void SetShowTime(bool showTime)
        {
            ShowTime = showTime;
            RequestUpdateFromObject();
        }

        public override string ValueToString(object value)
        {
            if (value == null)
            {
                return "";
            }

            if (value.GetType() != typeof(DateTime)) 
            {
                return "";
            }

            DateTime dateTime = (DateTime)value;

            if (dateTime != default(DateTime))
            {
                string showTimeText = "";
                if (ShowTime)
                {
                    showTimeText = " HH:mm";
                }

                return dateTime.ToString("yyyy/M/d" + showTimeText);
            }
            return "";
        }

        protected override void SetValue(object value)
        {
            try
            {
                string str = value.ToString();
                DateTime temp;
                string[] splits = str.Split('/', '-','\\');
                if (splits.Length == 3 && DateTime.TryParse(str, out temp))
                {
                    int y, m, d;

                    if (int.TryParse(splits[0], out y) &&
                        int.TryParse(splits[1], out m) &&
                        int.TryParse(splits[2], out d))
                    {
                            DateTime dateTime = new DateTime(y, m, d);
                            base.SetValue(dateTime);

                    }
                }
            }
            catch (Exception e)
            {
                Logger.Input.LogException(this, e);
                UpdateFromObject();
            }
        }
    }
              


    public class ListPropertyNode<T> : TextPropertyNode<T>
    {
        public List<object> Options { get; protected set; }

        public event Action<object> onChanged;

        public ListPropertyNode(T obj, PropertyInfo pi, Color textBackground, Color textColor, Color hover, System.Array options = null)
        : base(obj, pi, textBackground, textColor)
        {
            Value.CanEdit = false;

            if (options != null && options.Length > 0)
            {
                SetOptions(options.OfType<object>());
            }

            Value.AddChild(new HoverNode(hover));
        }

        //public ListPropertyNode(T obj, PropertyInfo pi, Color textColor, Color hover, System.Array options = null)
        //    :this(obj, pi, Color.Transparent, textColor, hover, options)
        //{
        //}
            

        public void SetOptions(IEnumerable<object> options)
        {
            Options = options.ToList();
        }

        public override void UpdateFromObject()
        {
            object value = PropertyInfo.GetValue(Object, null);

            if (value != null)
            {
                Value.Text = ValueToString(value);
            }
        }

        public override string ValueToString(object value)
        {
            if (value == null)
                return "";

            return value.ToString().CamelCaseToHuman();
        }

        protected override void SetValue(object value)
        {
            try
            {
                base.SetValue(value);
                Value.Text = ValueToString(value);

                onChanged?.Invoke(value);
            }
            catch (Exception ex) 
            {
                UpdateFromObject();
                Logger.Input.LogException(this, ex);
            }
        }

        protected virtual void ShowMouseMenu()
        {
            if (Options == null)
            {
                return;
            }

            MouseMenu mm = new MouseMenu(this);
            foreach (var value in Options)
            {
                mm.AddItem(ValueToString(value), () => { SetValue(value); });
            }

            Point screenPosition = Value.GetScreenPosition();

            // Drop from the bottom..
            screenPosition.Y += Value.Bounds.Height;

            mm.Show(screenPosition);
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.Button == MouseButtons.Left && mouseInputEvent.ButtonState == ButtonStates.Released)
            {
                ShowMouseMenu();
            }
            return base.OnMouseInput(mouseInputEvent);
        }

        public override bool Focus()
        {
            return false;
        }
    }

    public class EnumPropertyNode<T> : ListPropertyNode<T>
    {
        public EnumPropertyNode(T obj, PropertyInfo pi, Color background, Color textColor, Color hover)
            : base(obj, pi, background, textColor, hover)
        {
            List<object> enums = new List<object>();
            foreach (var enume in Enum.GetValues(pi.PropertyType))
            {
                if (!enums.Contains(enume))
                {
                    enums.Add(enume);
                }
            }

            SetOptions(enums);
        }
    }


    public class ButtonPropertyNode<T> : NamedPropertyNode<T>
    {
        private TextButtonNode button;

        public ButtonPropertyNode(T obj, PropertyInfo pi, Color backgroundColor, Color textColor, Color hoverColor, string text, Action action) 
            : base(obj, pi, textColor)
        {
            button = new TextButtonNode(text, backgroundColor, hoverColor, textColor);
            button.OnClick += (mie) =>
            {
                action?.Invoke();
            };
            AddChild(button);

            AlignHorizontally(0.01f, Children.ToArray());

            RequestUpdateFromObject();
        }

    }


    public class BoolPropertyNode<T> : NamedPropertyNode<T>
    {
        private CheckboxNode checkbox;

        public BoolPropertyNode(T obj, PropertyInfo pi, Color textColor, Color hoverColor)
            : base(obj, pi, textColor)
        {
            checkbox = new CheckboxNode(hoverColor);
            checkbox.Alignment = RectangleAlignment.CenterLeft;
            checkbox.ValueChanged += Checkbox_ValueChanged;
            AddChild(checkbox);

            AlignHorizontally(0.01f, Children.ToArray());

            RequestUpdateFromObject();
        }

        private void Checkbox_ValueChanged(bool obj)
        {
            SetValue(obj);
        }

        public bool GetValue()
        {
            object value = PropertyInfo.GetValue(Object, null);
            if (value is bool)
            {
                return (bool)value;
            }
            return false;
        }

        public override void UpdateFromObject()
        {
            checkbox.Value = GetValue();
        }
    }

    public class ArrayContainsPropertyNode<T, V> : NamedPropertyNode<T>
    {
        private CheckboxNode checkbox;

        public V Value { get; private set; }

        public V[] Array
        {
            get
            {
                return PropertyInfo.GetValue(Object) as V[]; 
            }
        }

        public ArrayContainsPropertyNode(T obj, PropertyInfo pi, V value, Color textColor, Color hoverColor) 
            : base(obj, pi, textColor)
        {
            Name.Text = value.ToString();

            Value = value;

            checkbox = new CheckboxNode(hoverColor);
            checkbox.Alignment = RectangleAlignment.CenterLeft;
            checkbox.ValueChanged += Checkbox_ValueChanged;
            AddChild(checkbox);

            AlignHorizontally(0.01f, Children.ToArray());

            RequestUpdateFromObject();
        }

        private void Checkbox_ValueChanged(bool obj)
        {
            List<V> list = Array.ToList();
            if (obj)
            {
                if (!list.Contains(Value))
                {
                    list.Add(Value);
                }
            }
            else
            {
                if (list.Contains(Value))
                {
                    list.Remove(Value);
                }
            }

            SetValue(list.ToArray());
        }

        public override void UpdateFromObject()
        {
            checkbox.Value = Array.Contains(Value);
        }
    }

    public class ItemNode<T> : Node
    {
        public T Item { get; private set; }

        private TextButtonNode textButtonNode;

        private TextButtonNode up;
        private TextButtonNode down;

        public string Text { get { return textButtonNode.Text; } set { textButtonNode.Text = value; } }

        public event MouseInputDelegate OnClick;
        public event MouseInputDelegate OnUpClick;
        public event MouseInputDelegate OnDownClick;

        private bool canReorder;

        public ItemNode(T t, string toString, Color background, Color hover, Color textColor, bool canReorder)
        {
            this.canReorder = canReorder;
            textButtonNode = new TextButtonNode(toString, background, hover, textColor);
            textButtonNode.OnClick += (mie) => 
            {
                OnClick(mie);
            };
            AddChild(textButtonNode);
            Item = t;

            textButtonNode.TextNode.Alignment = RectangleAlignment.CenterLeft;

            if (canReorder)
            {
                up = new TextButtonNode("▲", background, hover, textColor);
                up.OnClick += (mie) =>
                {
                    OnUpClick(mie);
                };

                down = new TextButtonNode("▼", background, hover, textColor);
                down.OnClick += (mie) =>
                {
                    OnDownClick(mie);
                };

                AddChild(up);
                AddChild(down);

                textButtonNode.RelativeBounds = new RectangleF(0, 0, 0.95f, 1);
                up.RelativeBounds = new RectangleF(textButtonNode.RelativeBounds.Right, 0, 1 - textButtonNode.RelativeBounds.Right, 0.5f);
                down.RelativeBounds = new RectangleF(textButtonNode.RelativeBounds.Right, 0.5f, 1 - textButtonNode.RelativeBounds.Right, 0.5f);
            }
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (base.OnMouseInput(mouseInputEvent))
            {
                return true;
            }

            if (canReorder && mouseInputEvent.Button == MouseButtons.Left && mouseInputEvent.ButtonState == ButtonStates.Pressed)
            {
                GetLayer<DragLayer>()?.RegisterDrag(this, mouseInputEvent);
            }

            return false;
        }
    }

    public class NeedsRestartAttribute : Attribute
    {
    }
}