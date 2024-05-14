using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using Composition.Input;

namespace Composition.Layers
{
    public class LayerStack : IDisposable
    {
        private Layer[] layerStack;
        public InputEventFactory InputEventFactory { get; private set; }

        public LayerStackGame Game { get; private set; }
        public GameWindow Window { get; private set; }
        public GraphicsDevice GraphicsDevice { get; private set; }

        public event Action OnRequestRedraw;

        private object locker;

        public PlatformTools PlatformTools { get; private set; }

        public Rectangle Bounds { get; private set; }

        public ContentManager Content { get; private set; }

        public LayerStack(GraphicsDevice graphicsDevice, LayerStackGame game, PlatformTools platformTools)
            :this(graphicsDevice, game.Window, platformTools)
        {
            Game = game;
            Content = game.Content;
        }

        public LayerStack(GraphicsDevice graphicsDevice, GameWindow gameWindow, PlatformTools platformTools)
        {
            PlatformTools = platformTools;

            locker = new object();
            GraphicsDevice = graphicsDevice;
            Window = gameWindow;

            layerStack = new Layer[0];
            InputEventFactory = new InputEventFactory(this, Window, platformTools);

            InputEventFactory.OnKeyboardInputEvent += OnKeyboardInputEvent;
            InputEventFactory.OnMouseInputEvent += OnMouseInputEvent;
            InputEventFactory.OnTextInputEvent += OnTextInputEvent;
        }

        public virtual void Dispose()
        {
            InputEventFactory.Dispose();

            lock (locker)
            {
                foreach (Layer layer in layerStack)
                {
                    layer.Dispose();
                }
                layerStack = new Layer[0];
            }
        }

        public virtual Rectangle GetBounds()
        {
            return Bounds;
        }

        private bool OnKeyboardInputEvent(KeyboardInputEvent inputEvent)
        {
            Layer[] layerArray = layerStack;
            //iterate through in reverse order
            for (int i = layerArray.Length - 1; i >= 0; i--)
            {
                if (!layerArray[i].Visible && !layerArray[i].AlwaysGetKeyboardEvents)
                    continue;

                if (layerArray[i].OnKeyboardInput(inputEvent))
                    return true;
            }
            return false;
        }

        protected virtual bool OnMouseInputEvent(MouseInputEvent inputEvent)
        {
            lock (locker)
            {
                Layer[] layerArray = layerStack;
                //iterate through in reverse order
                for (int i = layerArray.Length - 1; i >= 0; i--)
                {
                    if (!layerArray[i].Visible)
                        continue;

                    if (layerArray[i].OnMouseInput(inputEvent))
                        return true;
                }
            }

            return false;
        }

        private bool OnTextInputEvent(TextInputEventArgs text)
        {
            if (!PlatformTools.Focused)
                return false;


            Layer[] layerArray = layerStack;
            //iterate through in reverse order
            for (int i = layerArray.Length - 1; i >= 0; i--)
            {
                if (!layerArray[i].Visible)
                    continue;

                if (layerArray[i].OnTextInput(text))
                    return true;
            }

            return false;
        }

        public void Update(GameTime gameTime)
        {
            Layer[] layerArray = layerStack;
            //iterate through in reverse order
            for (int i = layerArray.Length - 1; i >= 0; i--)
            {
                layerArray[i].Update(gameTime);
            }

            InputEventFactory.Update(gameTime);
        }

        public virtual void Draw()
        {
            Bounds = new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

            Layer[] layerArray = layerStack;

            //iterate through in reverse order
            for (int i = 0; i < layerArray.Length; i++)
            {
                layerArray[i].Draw();
            }
        }


        public void AddBottom(Layer layer)
        {
            lock (locker)
            {
                if (layer.LayerStack != null) throw new Exception("Layer already exists in a stack");
                layer.SetLayerStack(this);
                layerStack = (new Layer[] { layer }).Union(layerStack).ToArray();
            }
        }

        public void Add(Layer layer)
        {
            lock (locker)
            {
                if (layer.LayerStack != null) throw new Exception("Layer already exists in a stack");
                layer.SetLayerStack(this);
                layerStack = layerStack.Union(new Layer[] { layer }).ToArray();
            }
        }

        public void Remove(Layer layer)
        {
            lock (locker)
            {
                layerStack = layerStack.Except(new Layer[] { layer }).ToArray();
            }
        }

        public void AddBelow<T>(Layer toAdd)
        {
            if (toAdd.LayerStack != null) throw new Exception("Layer already exists in a stack");

            lock (locker)
            {
                List<Layer> listStack = layerStack.ToList();
                for (int i = 0; i < listStack.Count; i++)
                {
                    if (listStack[i] is T)
                    {
                        toAdd.SetLayerStack(this);

                        listStack.Insert(i, toAdd);
                        break;
                    }
                }

                layerStack = listStack.ToArray();
            }
        }

        public void AddAbove<T>(Layer toAdd)
        {
            if (toAdd.LayerStack != null) throw new Exception("Layer already exists in a stack");

            lock (locker)
            {
                List<Layer> listStack = layerStack.ToList();
                for (int i = 0; i < listStack.Count; i++)
                {
                    if (listStack[i] is T)
                    {
                        toAdd.SetLayerStack(this);

                        listStack.Insert(i + 1, toAdd);
                        break;
                    }
                }

                layerStack = listStack.ToArray();
            }
        }

        public T GetLayer<T>() where T: Layer
        {
            return GetLayers<T>().FirstOrDefault();
        }

        public IEnumerable<T> GetLayers<T>() where T : Layer
        {
            foreach (Layer l in layerStack)
            {
                if (typeof(T).IsAssignableFrom(l.GetType()))
                {
                    yield return (T)l;
                }
            }
        }

        public void RequestRedraw()
        {
            OnRequestRedraw?.Invoke();
        }

        public void DoBackground()
        {
            foreach (Layer l in layerStack)
            {
                l.DoBackground();
            }
        }
    }
}
