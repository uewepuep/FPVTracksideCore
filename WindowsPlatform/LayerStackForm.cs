using Composition.Layers;
using Microsoft.Xna.Framework.Graphics;
using RaceLib;
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
using Tools;
using UI;
using UI.Nodes;

namespace WindowsPlatform
{
    public partial class LayerStackForm : Form
    {
        public LayerStack LayerStack { get { return layerStackControl.LayerStack; } }
        public GraphicsDevice GraphicsDevice { get { return LayerStack.GraphicsDevice; } }

        public LayerStackForm(string name)
        {
            Text = name;

            InitializeComponent();
            layerStackControl.OnInitialise += Initialise;
        }

        protected virtual void Initialise(GraphicsDevice graphicsDevice, LayerStack layerstack)
        {
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            Invalidate();
        }
    }

    public class CompositionLayerForm : LayerStackForm
    {
        public CompositorLayer CompositorLayer { get; private set; }

        private Action<CompositorLayer> onInitialise;

        public CompositionLayerForm(string name, Action<CompositorLayer> OnInitialise)
            : base(name)
        {
            onInitialise = OnInitialise;
        }

        protected override void Initialise(GraphicsDevice graphicsDevice, LayerStack layerstack)
        {
            base.Initialise(graphicsDevice, layerstack);

            BackgroundLayer backgroundLayer = new BackgroundLayer(GraphicsDevice, Theme.Current.Background);
            LayerStack.Add(backgroundLayer);

            CompositorLayer = new CompositorLayer(graphicsDevice);
            LayerStack.Add(CompositorLayer);

            PopupLayer popupLayer = new PopupLayer(graphicsDevice);
            MenuLayer menuLayer = new MenuLayer(graphicsDevice, Theme.Current.MenuBackground.XNA, Theme.Current.Hover.XNA, Theme.Current.MenuText.XNA, Theme.Current.MenuTextInactive.XNA, Theme.Current.ScrollBar.XNA);
            DragLayer dragLayer = new DragLayer(graphicsDevice);

            LayerStack.Add(popupLayer);
            LayerStack.Add(menuLayer);
            LayerStack.Add(dragLayer);

            onInitialise?.Invoke(CompositorLayer);
        }


        public static Form ShowNewWindow(Control parent, Composition.Nodes.Node node)
        {
            string name = node.GetType().Name.Replace("Node", "").Replace("List", "").CamelCaseToHuman();

            CompositionLayerForm form = new CompositionLayerForm(name,
            (compositorLayer) =>
            {
                compositorLayer.Root.AddChild(node);
            });

            parent.BeginInvoke(new Action(form.Show));

            return form;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (WindowState == FormWindowState.Maximized)
            {
                Properties.Settings.Default.Size = RestoreBounds.Size;
            }
            else if (WindowState == FormWindowState.Normal)
            {
                Properties.Settings.Default.Size = Size;
            }
            else
            {
                Properties.Settings.Default.Size = RestoreBounds.Size;
            }
            Properties.Settings.Default.Save();

            base.OnFormClosed(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            if (!Properties.Settings.Default.Size.IsEmpty)
            {
                Size = Properties.Settings.Default.Size;
            }

            base.OnLoad(e);
        }

    }
}
