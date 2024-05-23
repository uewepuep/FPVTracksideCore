using Composition;
using Composition.Nodes;
using Composition.Text;
using ImageServer;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tools;

namespace WindowsPlatform
{
    public class WindowsPlatformTools : PlatformTools
    {

        private IClipboard clipboard;
        public override IClipboard Clipboard { get => clipboard; }
        
        private bool focused;
        public override bool Focused { get => focused; }

        public Control Control { get; private set; }

        public const string loginFile = @"data\login.enc";

        public override string InstallerExtension
        {
            get
            {
                return ".msi";
            }
        }

        public override PlatformFeature[] Features
        {
            get
            {
                return new PlatformFeature[]
                {
                    PlatformFeature.Speech,
                    PlatformFeature.Video,
                    PlatformFeature.GMFBridge
                };
            }
        }

        private DirectoryInfo workingDirectory;
        public override DirectoryInfo WorkingDirectory
        {
            get
            {
                return workingDirectory;
            }
        }

        public override bool ThreadedDrawing { get { return true; } }

        public WindowsPlatformTools()
        {
            workingDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
            clipboard = new Clipboard();
        }

        public override ITextRenderer CreateTextRenderer()
        {
            return new TextRenderWPF();
        }

        public override void ShowNewWindow(Node node)
        {
            Action d = () =>
            {
                CompositionLayerForm.ShowNewWindow(Control, node);
            };

            Control.BeginInvoke(d);
        }

        public void SetGameWindow(GameWindow window, System.Drawing.Icon icon)
        {
            Control = System.Windows.Forms.Control.FromHandle(window.Handle);

            Form form = Control as Form;
            if (form != null)
            {
                form.Icon = icon;
            }

            if (Control != null)
            {
                Control.GotFocus += (e, s) => { focused = true; };
                Control.LostFocus += (e, s) => { focused = false; };
                focused = Control.Focused;
            }
            else
            {
                throw new Exception("Windows forms was expected");
            }
        }

        public override string OpenFileDialog(string title, string filter)
        {
            string output = null;
            RunAsSTAThread(() =>
            {
                System.Windows.Forms.OpenFileDialog openDialog = new System.Windows.Forms.OpenFileDialog();
                openDialog.Filter = filter;
                openDialog.Title = title;
                if (openDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    output = openDialog.FileName;
                }
            });

            return output;
        }

        public override string SaveFileDialog(string title, string filter)
        {
            string output = null;
            RunAsSTAThread(() =>
            {
                System.Windows.Forms.SaveFileDialog saveDialog = new System.Windows.Forms.SaveFileDialog();
                saveDialog.Filter = filter;
                saveDialog.Title = title;
                if (saveDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    output = saveDialog.FileName;
                }
            });

            return output;
        }

        public override void Invoke(Action value)
        {
            Control.BeginInvoke(value);
        }

        public override ISpeaker CreateSpeaker(string voice)
        {
            ISpeaker speaker = new WindowsSpeaker();
            if (voice != null)
            {
                speaker.SelectVoice(voice);
            }
            return speaker;
        }

        public static void RunAsSTAThread(Action action)
        {
            AutoResetEvent autoreset = new AutoResetEvent(false);
            Thread thread = new Thread(() =>
                {
                    action();
                    autoreset.Set();
                });
            thread.Name = "STA Hack";
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            autoreset.WaitOne();
            thread.Join();
        }


        public delegate T ActionT<T>();
        public static bool ReturnAsSTAThread<T>(ActionT<T> action, out T output)
        {
            T t = default(T);

            AutoResetEvent autoreset = new AutoResetEvent(false);
            Thread thread = new Thread(() =>
                {
                    t = action();
                    autoreset.Set();
                });
            thread.Name = "STA Hack";
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            autoreset.WaitOne();
            thread.Join();

            output = t;
            return true;
        }

        public override void OpenFileManager(string directory)
        {
            FileInfo file = new FileInfo(directory);
            if (file.Exists)
            {
                directory = file.DirectoryName;
            }
            System.Diagnostics.Process.Start("explorer.exe", directory);
        }
    }
}
