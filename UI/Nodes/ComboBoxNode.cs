using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;

namespace UI.Nodes
{
    public class ComboBoxNode<T> : Node
    {
        private TextButtonNode button;
        private List<T> items;
        private string defaultText;

        public event Action<T> OnSelectionChanged;

        public List<T> Items
        {
            get => items;
            set
            {
                items = value;
                if (items == null)
                {
                    button.Text = defaultText;
                }
            }
        }

        public ComboBoxNode(string defaultText, Color background, Color hover, Color textColor)
        {
            this.defaultText = defaultText;
            items = new List<T>();

            button = new TextButtonNode(defaultText, background, hover, textColor);
            button.RelativeBounds = new RectangleF(0, 0, 1, 1); // Fill the entire ComboBox
            button.OnClick += Button_OnClick;
            AddChild(button);
        }

        private void Button_OnClick(MouseInputEvent mouseInputEvent)
        {
            if (items == null || !items.Any()) 
            {
                System.Diagnostics.Debug.WriteLine("ComboBoxNode: No items available for dropdown");
                return;
            }

            var menu = new MouseMenu(this);
            if (menu == null)
            {
                System.Diagnostics.Debug.WriteLine("ComboBoxNode: Failed to create MouseMenu");
                return;
            }

            foreach (var item in items)
            {
                if (item == null)
                {
                    System.Diagnostics.Debug.WriteLine("ComboBoxNode: Skipping null item");
                    continue;
                }
                
                var currentItem = item;  // Capture the current item in closure
                string itemText = currentItem.ToString() ?? "null";
                
                menu.AddItem(itemText, () =>
                {
                    if (button != null)
                    {
                        button.Text = currentItem.ToString() ?? "null";
                    }
                    OnSelectionChanged?.Invoke(currentItem);
                });
            }

            if (button != null)
            {
                menu.Show(button);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("ComboBoxNode: Button is null, cannot show menu");
            }
        }

        public override void Layout(RectangleF bounds)
        {
            base.Layout(bounds);
            if (button != null)
            {
                button.RelativeBounds = new RectangleF(0, 0, 1, 1); // Ensure button fills the ComboBox
            }
        }
    }
} 