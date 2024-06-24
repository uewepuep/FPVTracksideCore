using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace Composition
{
    public class GlobalInterceptKeys : IDisposable
    {
        public static GlobalInterceptKeys Instance { get; private set; }

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private static LowLevelKeyboardProc proc = HookCallback;
        private static IntPtr hookID = IntPtr.Zero;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private List<Keys> keysDown;
        private List<Keys> listeningTo;

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
            if (listeningTo.Contains(key) && !keysDown.Contains(key))
            {
                keysDown.Add(key);
            }
        }

        private void KeyUp(Keys key)
        {
            if (listeningTo.Contains(key))
            {
                keysDown.Remove(key);
            }
        }

        public KeyboardState GetKeyboardState()
        {
            KeyboardState state = new KeyboardState(keysDown.ToArray());
            return state;
        }

        public void AddListen(Keys key)
        {
            listeningTo.Add(key);
        }

        public void AddListen(IEnumerable<Keys> keys)
        {
            foreach (Keys key in keys)
            { 
               listeningTo.Add(key);
            }
        }

        public void RemoveListen(Keys key) 
        {
            listeningTo.Remove(key);
        }

        public void ClearListen()
        {
            listeningTo.Clear();
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

                if (wParam == (IntPtr)WM_KEYDOWN)
                {
                    Instance.KeyDown(key);
                }

                if (wParam == (IntPtr)WM_KEYUP)
                {
                    Instance.KeyUp(key);
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
