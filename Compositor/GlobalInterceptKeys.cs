using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Tools;
using static System.Net.Mime.MediaTypeNames;

namespace Composition
{
    public class GlobalInterceptKeys : IDisposable
    {
        public static GlobalInterceptKeys Instance { get; private set; }

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private static LowLevelKeyboardProc proc = HookCallback;
        private static IntPtr hookID = IntPtr.Zero;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private List<Keys> keysDown;
        private List<Keys> listeningTo;

        public event Action OnKeyPress;

        public GlobalInterceptKeys()
        {
            keysDown = new List<Keys>();
            listeningTo = new List<Keys>();

            Instance = this;
            hookID = SetHook(proc);
        }

        public void Dispose()
        {
            UnhookWindowsHookEx(hookID);
        }

        private void KeyDown(Keys key)
        {
            lock (keysDown)
            {
                if (listeningTo.Contains(key) && !keysDown.Contains(key))
                {
                    keysDown.Add(key);
                    OnKeyPress?.Invoke();
                    //Logger.UI.LogCall(this, key);
                }
            }
        }

        private void KeyUp(Keys key)
        {
            OnKeyPress?.Invoke();
        }

        public void Clear()
        {
            lock (keysDown)
            {
                keysDown.Clear();
            }
        }

        public KeyboardState GetKeyboardState()
        {
            KeyboardState state = new KeyboardState(keysDown.ToArray());
            return state;
        }

        public void AddListen(Keys key)
        {
            if (!listeningTo.Contains(key))
            {
                listeningTo.Add(key);
            }
        }

        public void AddListen(IEnumerable<Keys> keys)
        {
            foreach (Keys key in keys)
            {
                AddListen(key);
            }
        }

        public void RemoveListen(Keys key) 
        {
            listeningTo.Remove(key);
        }

        public void Reset()
        {
            keysDown.Clear();
            listeningTo.Clear();
            OnKeyPress = null;
        }

        public bool Match(ShortcutKey key)
        {
            if (key == null)
            {
                return false;
            }

            KeyboardState state = GetKeyboardState();
            if (key.Match(state))
            {
                Clear();
                return true;
            }
            return false;
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                Keys key = (Keys)vkCode;

                long iParam = (int)wParam;

                switch (iParam)
                {
                    case WM_KEYUP:
                    case WM_SYSKEYUP:
                        Instance.KeyUp(key);
                        break;

                    case WM_KEYDOWN:
                    case WM_SYSKEYDOWN:
                        Instance.KeyDown(key);
                        break;
                }
            }
            return CallNextHookEx(hookID, nCode, wParam, lParam);
        }


        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
