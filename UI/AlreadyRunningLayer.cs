using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools;
using UI.Nodes;

namespace UI
{
    public class AlreadyRunningLayer : CompositorLayer
    {
        private Mutex mutex;

        public AlreadyRunningLayer(GraphicsDevice device, Mutex mutex) 
            : base(device)
        {
            this.mutex = mutex;

            Color c = new Color(Color.Black, 0.75f);

            ColorNode dimNode = new ColorNode(c);
            Root.AddChild(dimNode);

            BorderPanelNode borderPanelNode = new BorderPanelNode();
            borderPanelNode.RelativeBounds = new RectangleF(0.3f, 0.275f, 0.4f, 0.2f);
            dimNode.AddChild(borderPanelNode);

            TextNode textNode = new TextNode("This software is already running in another instance.\nPlease close the other instance or kill it in task manager.", Theme.Current.TextMain.XNA);
            textNode.Scale(0.8f);
            borderPanelNode.AddChild(textNode);
        }

        protected override void OnUpdate(GameTime gameTime)
        {
            try
            {
                if (mutex.WaitOne(TimeSpan.Zero))
                {
                    Dispose();
                }
            }
            catch
            {
            }
            base.OnUpdate(gameTime);
        }

        public override bool OnMouseInput(MouseInputEvent inputEvent)
        {
            return true;
        }

        public override bool OnKeyboardInput(KeyboardInputEvent inputEvent)
        {
            return true;
        }

        public override bool OnTextInput(TextInputEventArgs inputEvent)
        {
            return true;
        }
    }
}
