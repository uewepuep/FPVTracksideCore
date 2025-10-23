using Composition;
using Composition.Input;
using Composition.Layers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tools;
using WinFormsGraphicsDevice;

namespace WindowsPlatform.GDI
{
    public class GDILayerStackControl : Control
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public GDILayerStack LayerStack { get; private set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public DateTime Start { get; private set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public DateTime LastFrame { get; private set; }

        public event Action<GraphicsDevice, LayerStack> OnInitialise;

        private Point oldMouse;

        public GDILayerStackControl()
        {
            oldMouse = new Point(0, 0);

            LayerStack = new GDILayerStack(new WindowsPlatformTools());
            LayerStack.InputEventFactory.CreateKeyboardEvents = false;
            LayerStack.InputEventFactory.CreateMouseEvents = false;

            LayerStack.OnRequestRedraw += LayerStack_OnRequestRedraw;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            LayerStack.OnRequestRedraw -= LayerStack_OnRequestRedraw;
            LayerStack?.Dispose();
            LayerStack = null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            DateTime now = DateTime.Now;
            GameTime gameTime = new GameTime(now - Start, now - LastFrame);

            LayerStack.DoBackground();

            LayerStack.Update(gameTime);
            LayerStack.Draw(e.Graphics);
        }

        private void LayerStack_OnRequestRedraw()
        {
            Redraw();
        }

        private void Redraw()
        {
            try
            {
                BeginInvoke(new Action(Invalidate));
            }
            catch (Exception e)
            {
                Logger.UI.LogException(this, e);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            LayerStack.InputEventFactory.OnMouseInput(new MouseInputEvent(ButtonStates.Pressed, ButtonTranslator(e.Button), new Point(e.X, e.Y)));
            LayerStack.InputEventFactory.ProcessInputs();
            Redraw();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            LayerStack.InputEventFactory.OnMouseInput(new MouseInputEvent(ButtonStates.Released, ButtonTranslator(e.Button), new Point(e.X, e.Y)));
            LayerStack.InputEventFactory.ProcessInputs();
            Redraw();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            Point mouse = new Point(e.X, e.Y);
            LayerStack.InputEventFactory.OnMouseInput(new MouseInputEvent(mouse, oldMouse));
            LayerStack.InputEventFactory.ProcessInputs();
            oldMouse = mouse;
            Redraw();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            LayerStack.InputEventFactory.OnMouseInput(new MouseInputEvent(e.Delta, new Point(e.X, e.Y)));
            LayerStack.InputEventFactory.ProcessInputs();
            Redraw();
        }

        public Composition.Input.MouseButtons ButtonTranslator(System.Windows.Forms.MouseButtons mouseButton)
        {
            switch (mouseButton)
            {
                case System.Windows.Forms.MouseButtons.Left:
                    return Composition.Input.MouseButtons.Left;
                case System.Windows.Forms.MouseButtons.Right:
                    return Composition.Input.MouseButtons.Right;
            }
            return Composition.Input.MouseButtons.None;
        }
    }


}
