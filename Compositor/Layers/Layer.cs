using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using Composition.Input;

namespace Composition.Layers
{
    public class Layer : IDisposable
    {
        public bool Visible { get; set; }

        private LayerStack parentLayerStack;
        public LayerStack LayerStack 
        { 
            get 
            { 
                return parentLayerStack;
            } 
            private set 
            {
                if (parentLayerStack == null)
                {
                    parentLayerStack = value;
                }
            } 
        }

        public LayerStackGame Game { get { return LayerStack.Game; } }

        public InputEventFactory InputEventFactory { get { return LayerStack.InputEventFactory; } }

        public bool AlwaysGetKeyboardEvents { get; set; }

        public Layer()
        {
            LayerStack = null;
            Visible = true;
            AlwaysGetKeyboardEvents = false;
        }

        public virtual void SetLayerStack(LayerStack layerStack)
        {
            this.LayerStack = layerStack;
        }

        public void Remove()
        {
            if (LayerStack != null)
            {
                LayerStack.Remove(this);
            }
        }

        public virtual void Dispose()
        {
            Remove();
        }

        public void Update(GameTime gameTime)
        {
            OnUpdate(gameTime);
        }

        protected virtual void OnUpdate(GameTime gameTime) { }
        
        public void Draw()
        {
            if (Visible)
            {
                OnDraw();
            }
        }

        protected virtual void OnDraw() { }

        public virtual bool OnMouseInput(MouseInputEvent inputEvent) { return false; }

        public virtual bool OnKeyboardInput(KeyboardInputEvent inputEvent) { return false; }

        public virtual bool OnTextInput(TextInputEventArgs text) { return false; }

        public virtual void DoBackground() { }
    }
}
