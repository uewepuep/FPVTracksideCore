using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools
{
    public class ShortcutKey
    {
        public bool ControlKey { get; set; }
        public bool AltKey { get; set; }
        public bool ShiftKey { get; set; }

        public Keys Key { get; set; }

        public IEnumerable<Keys> InvolvedKeys
        {
            get
            {
                if (ControlKey)
                {
                    yield return Keys.LeftControl;
                    yield return Keys.RightControl;
                }

                if (ShiftKey)
                {
                    yield return Keys.LeftShift;
                    yield return Keys.RightShift;
                }

                if (AltKey)
                {
                    yield return Keys.LeftAlt;
                    yield return Keys.RightAlt;
                }

                yield return Key;
            }
        }

        public ShortcutKey()
        {
            ControlKey = false;
            AltKey = false;
            ShiftKey = false;
        }

        public ShortcutKey(Keys key, bool ctrl = false, bool alt = false, bool shift = false)
        {
            Key = key;
            ControlKey = ctrl;
            AltKey = alt;
            ShiftKey = shift;
        }

        public bool Match(Keys key, bool ctrl, bool alt, bool shift)
        {
            if (ControlKey != ctrl) return false;
            if (ShiftKey != shift) return false;
            if (AltKey != alt) return false;

            return key == Key;
        }

        public override string ToString()
        {
            List<string> texts = new List<string>();

            if (ControlKey) texts.Add("Ctrl");
            if (AltKey) texts.Add("Alt");
            if (ShiftKey) texts.Add("Shift");

            texts.Add(Key.ToString());

            return string.Join(" + ", texts);
        }

        public bool Match(KeyboardState keyboardState)
        {
            if (ControlKey)
            {
                if (!keyboardState.IsKeyDown(Keys.LeftControl) && !keyboardState.IsKeyDown(Keys.RightControl))
                    return false;
            }

            if (AltKey)
            {
                if (!keyboardState.IsKeyDown(Keys.LeftAlt) && !keyboardState.IsKeyDown(Keys.RightAlt))
                    return false;
            }

            if (ShiftKey)
            {
                if (!keyboardState.IsKeyDown(Keys.LeftShift) && !keyboardState.IsKeyDown(Keys.RightShift))
                    return false;
            }

            if (!keyboardState.IsKeyDown(Key))
                return false;

            return true;    
        }
    }
}
