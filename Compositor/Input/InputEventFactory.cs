using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Composition.Layers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Tools;

namespace Composition.Input
{
    public enum MouseButtons
    {
        None,
        Left,
        Middle,
        Right,
        Wheel
    };

    public enum ButtonStates
    {
        None,
        Released,
        Pressed,
        Repeat
    };

    public class InputEventFactory : IDisposable
    {
        public KeyboardState OldKeyboardState { get; private set; }
        public MouseState OldMouseState { get; private set; }

        private Dictionary<Keys, DateTime> repeatKeysTime;

        public delegate bool KeyboardInput(KeyboardInputEvent inputEvent);
        public delegate bool MouseInput(MouseInputEvent inputEvent);
        public delegate bool TextInput(TextInputEventArgs inputEvent);

        public event KeyboardInput OnKeyboardInputEvent;
        public event MouseInput OnMouseInputEvent;
        public event TextInput OnTextInputEvent;

        public bool CreateMouseEvents { get; set; }
        public bool CreateKeyboardEvents { get; set; }

        public DateTime LastKeyboardUpdateTime { get; private set; }
        public DateTime LastMouseUpdateTime { get; private set; }

        public GameWindow Window { get; private set; }

        private List<KeyboardInputEvent> keyboardInputs;
        private List<MouseInputEvent> mouseInputs;

        private Thread pollingThread;

        private Keys[] keysDown;

        public TimeSpan RepeatTime { get; set; }
        public TimeSpan InitialRepeatDelay { get; set; }

        private AutoResetEvent autoResetEvent;

        public float ResolutionScale { get; set; }

        public PlatformTools PlatformTools { get; private set; }

        private LayerStack layerStack;

        public InputEventFactory(LayerStack layerStack, GameWindow window, PlatformTools platformTools)
        {
            this.layerStack = layerStack;

            PlatformTools = platformTools;
            ResolutionScale = 1;

            keysDown = new Keys[0];

            keyboardInputs = new List<KeyboardInputEvent>();
            mouseInputs = new List<MouseInputEvent>();
            repeatKeysTime = new Dictionary<Keys, DateTime>();

            RepeatTime = TimeSpan.FromSeconds(1 / 7.0);
            InitialRepeatDelay = TimeSpan.FromSeconds(1 / 3.0);

            Window = window;

            OldKeyboardState = new KeyboardState();
            OldMouseState = new MouseState();

            OnKeyboardInputEvent = null;
            OnMouseInputEvent = null;

            CreateMouseEvents = true;
            CreateKeyboardEvents = true;

            window.TextInput += WindowTextInput;

            autoResetEvent = new AutoResetEvent(false);

            pollingThread = new Thread(PollInputs);
            pollingThread.Name = "InputEventFactory";
            pollingThread.Priority = ThreadPriority.BelowNormal;

            if (platformTools.HasFeature(PlatformFeature.Windows))
            {
#pragma warning disable CA1416 // Validate platform compatibility
                pollingThread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416 // Validate platform compatibility
            }

            pollingThread.Start();
        }

        public void Dispose()
        {
            CreateMouseEvents = false;
            CreateKeyboardEvents = false;
            pollingThread.Join();
        }

        private void WindowTextInput(object sender, TextInputEventArgs e)
        {
            if (CreateKeyboardEvents)
            {
                OnTextInputEvent?.Invoke(e);
            }
        }

        private void PollInputs()
        {
            try
            {
                while (CreateMouseEvents || CreateKeyboardEvents)
                {
                    autoResetEvent.WaitOne(1000);

                    if (CreateKeyboardEvents)
                    {
                        UpdateKeyboard();
                    }

                    if (CreateMouseEvents)
                    {
                        UpdateMouse();
                    }

                    ProcessInputs();
                }
            }
            catch (Exception ex) 
            {
                Tools.Logger.CrashLogger.Log(ex);
                throw ex;
            }
        }

        public void Update(GameTime gameTime)
        {
            autoResetEvent.Set();
        }

        public void ProcessInputs()
        {
            KeyboardInputEvent[] newKeyboardInputs = null;
            lock (keyboardInputs)
            {
                newKeyboardInputs = keyboardInputs.ToArray();
                keyboardInputs.Clear();
            }

            if (OnKeyboardInputEvent != null && newKeyboardInputs != null)
            {
                foreach (KeyboardInputEvent keyboardInput in newKeyboardInputs)
                {
                    OnKeyboardInputEvent(keyboardInput);
                }

            }
            MouseInputEvent[] newMouseInputs = null;
            lock (mouseInputs)
            {
                newMouseInputs = mouseInputs.ToArray();
                mouseInputs.Clear();
            }

            if (OnMouseInputEvent != null)
            {
                foreach (MouseInputEvent mouseInput in newMouseInputs)
                {
                    OnMouseInputEvent(mouseInput);
                }
            }
        }

        private void UpdateKeyboard()
        {
            if (OnKeyboardInputEvent != null && PlatformTools.Focused)
            {
                try
                {
                    DateTime now = DateTime.Now;
                    KeyboardState newState = Keyboard.GetState();

                    Keys[] oldPressedKeys = OldKeyboardState.GetPressedKeys();
                    Keys[] newPressedKeys = newState.GetPressedKeys();

                    // Repeat the keys..
                    foreach (var kvp in repeatKeysTime)
                    {
                        if (newPressedKeys.Contains(kvp.Key))
                        {
                            TimeSpan timePassed = now - kvp.Value;

                            if (timePassed > InitialRepeatDelay)
                            {
                                TimeSpan timePassedLastTime = LastKeyboardUpdateTime - kvp.Value;

                                int repeatsThisTime = (int)(timePassed.TotalMilliseconds % RepeatTime.TotalMilliseconds);
                                int repeatsLastTime = (int)(timePassedLastTime.TotalMilliseconds % RepeatTime.TotalMilliseconds);

                                if (repeatsThisTime > repeatsLastTime)
                                {
                                    OnKeyboardInput(ButtonStates.Repeat, kvp.Key);
                                }
                            }
                        }
                    }

                    foreach (Keys oldKey in oldPressedKeys)
                    {
                        if (!newPressedKeys.Contains(oldKey))
                        {
                            if (repeatKeysTime.ContainsKey(oldKey))
                            {
                                repeatKeysTime.Remove(oldKey);
                            }

                            OnKeyboardInput(ButtonStates.Released, oldKey);
                        }
                    }

                    foreach (Keys newKey in newPressedKeys)
                    {
                        if (!oldPressedKeys.Contains(newKey))
                        {
                            if (!repeatKeysTime.ContainsKey(newKey))
                            {
                                repeatKeysTime.Add(newKey, now);
                            }

                            OnKeyboardInput(ButtonStates.Pressed, newKey);
                        }
                    }

                    LastKeyboardUpdateTime = now;
                    OldKeyboardState = newState;
                    keysDown = newPressedKeys;
                }
                catch (Exception e)
                {
                    Logger.Input.LogException(this, e);
                }
            }
        }

        public bool IsKeyDown(Keys key)
        {
            return keysDown.Contains(key);
        }

        public bool AreAnyKeysDown(params Keys[] keys)
        {
            return keysDown.Intersect(keys).Any();
        }

        public bool AreAllKeysDown(params Keys[] keys)
        {
            return keysDown.Intersect(keys).Count() == keys.Length;
        }

        public bool AreControlKeysDown()
        {
            return AreAnyKeysDown(Keys.LeftControl, Keys.RightControl);
        }

        public bool AreShiftKeysDown()
        {
            return AreAnyKeysDown(Keys.LeftShift, Keys.RightShift);
        }

        public bool AreAltKeysDown()
        {
            return AreAnyKeysDown(Keys.LeftAlt, Keys.RightAlt);
        }

        public void OnKeyboardInput(ButtonStates buttonState, Keys key)
        {
            Keys[] modifiers = new Keys[] { Keys.LeftControl, Keys.RightControl, Keys.LeftShift, Keys.RightShift, Keys.LeftAlt, Keys.RightAlt };

            if (modifiers.Contains(key))
                return;

            KeyboardInputEvent keyboardEvent = new KeyboardInputEvent(buttonState, key, AreShiftKeysDown(), AreControlKeysDown(), AreAltKeysDown());

            lock (keyboardInputs)
            {
                keyboardInputs.Add(keyboardEvent);
            }
        }

        public void OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            lock (mouseInputs)
            {
                mouseInputs.Add(mouseInputEvent);
            }
        }

        private void UpdateMouse()
        {
            if (OnMouseInputEvent != null)
            {
                try
                {
                    MouseState newState = Mouse.GetState(Window);
                    Point cursorPosition = new Point((int)(newState.X * ResolutionScale), (int)(newState.Y * ResolutionScale));

                    if (cursorPosition != OldMouseState.Position)
                    {
                        LastMouseUpdateTime = DateTime.Now;
                    }

                    if (PlatformTools.Focused)
                    {
                        if (newState.LeftButton != OldMouseState.LeftButton)
                        {
                            OnMouseInput(newState.LeftButton, MouseButtons.Left, cursorPosition);
                        }

                        if (newState.MiddleButton != OldMouseState.MiddleButton)
                        {
                            OnMouseInput(newState.MiddleButton, MouseButtons.Middle, cursorPosition);
                        }

                        if (newState.RightButton != OldMouseState.RightButton)
                        {
                            OnMouseInput(newState.RightButton, MouseButtons.Right, cursorPosition);
                        }

                        if (newState.ScrollWheelValue != OldMouseState.ScrollWheelValue)
                        {
                            OnMouseInput(newState.ScrollWheelValue - OldMouseState.ScrollWheelValue, cursorPosition);
                        }

                        if (newState.Position != OldMouseState.Position)
                        {
                            OnMouseInput(cursorPosition, OldMouseState.Position);
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Input.LogException(this, e);
                }
            }
            OldMouseState = Mouse.GetState();
        }

        private void OnMouseInput(int mouseWheelChange, Point cursorPosition)
        {
            MouseInputEvent mouseEvent = new MouseInputEvent(mouseWheelChange, cursorPosition);
            OnMouseInput(mouseEvent);
        }

        public void OnMouseInput(ButtonState xnaButtonState, MouseButtons button, Point position)
        {
            ButtonStates state = ButtonStates.None;
            if (xnaButtonState == ButtonState.Pressed)
            {
                state = ButtonStates.Pressed;
            }
            if (xnaButtonState == ButtonState.Released)
            {
                state = ButtonStates.Released;
            }

            MouseInputEvent mouseEvent = new MouseInputEvent(state, button, position);
            OnMouseInput(mouseEvent);
        }

        public void OnMouseInput(Point newPosition, Point oldPosition)
        {
            MouseInputEvent mouseEvent = new MouseInputEvent(newPosition, oldPosition);
            OnMouseInput(mouseEvent);
        }

    }
}
