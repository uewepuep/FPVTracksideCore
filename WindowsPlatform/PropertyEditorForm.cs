using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WindowsPlatform
{
    public partial class PropertyEditorForm<T> : Form
    {
        public List<T> Objects { get; set; }

        public Type Type { get { return typeof(T); } }

        public T Selected
        {
            get
            {
                if (Objects.Count == 1)
                {
                    return Objects.First();
                }

                return (T)propertyGrid.SelectedObject;
            }
        }

        public PropertyEditorForm(T toEdit)
            :this(new T[] { toEdit }, false)
        {
        }

        public PropertyEditorForm(IEnumerable<T> toEdit, bool addRemove = false, bool cancelButton = true)
        {
            InitializeComponent();
            propertyGrid.PropertyValueChanged += PropertyGrid_PropertyValueChanged;

            this.Text = Type.Name + " Editor";

            StartPosition = FormStartPosition.CenterParent;

            if (toEdit.Count() > 1 || addRemove)
            {
                foreach (T t in toEdit)
                {
                    listBox.Items.Add(t);
                }
            }
            else
            {
                listBox.Visible = false;

                int newWidth = propertyGrid.Right - listBox.Left;
                propertyGrid.Left = listBox.Left;
                propertyGrid.Width = newWidth;
                if (toEdit.Any())
                {
                    propertyGrid.SelectedObject = toEdit.First();
                }
            }

            Objects = toEdit.ToList();
            this.AcceptButton = save;

            if (Type.GetConstructor(Type.EmptyTypes) != null || Type.IsValueType)
            {
                this.addButton.Visible = true;
            }
            else
            {
                this.addButton.Visible = false;
            }

            if (!addRemove)
            {
                this.addButton.Visible = false;
                this.remove.Visible = false;
            }

            if (!cancelButton)
            {
                cancel.Visible = false;
            }
        }

        private void PropertyGrid_PropertyValueChanged(object s, PropertyValueChangedEventArgs e)
        {
            if (Objects.Count > 1)
            {
                listBox.Items.Clear();
                foreach (T t in Objects)
                {
                    listBox.Items.Add(t);
                }
            }
        }

        private void cancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void save_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void listBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            propertyGrid.SelectedObject = listBox.SelectedItem;
        }

        private void addButton_Click(object sender, EventArgs e)
        {
            T t = Activator.CreateInstance<T>();
            Objects.Add(t);
            listBox.Items.Add(t);
            propertyGrid.SelectedObject = t;
        }

        private void remove_Click(object sender, EventArgs e)
        {
            if (listBox.SelectedItem != null)
            {
                Objects.Remove((T)listBox.SelectedItem);
                listBox.Items.Remove(listBox.SelectedItem);
            }
        }
    }
}
