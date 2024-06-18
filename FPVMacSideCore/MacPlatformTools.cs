using System;
using System.IO;
using System.Collections.Generic;
using Composition;
using Composition.Nodes;
using Composition.Text;
using Tools;
using System.Linq;
using ImageServer;

namespace FPVMacsideCore
{
    public class MacPlatformTools : PlatformTools
    {

        private Maclipboard maclipboard;
        public override IClipboard Clipboard
        {
            get
            {
                return maclipboard;
            }
        }

        public override bool Focused
        {
            get
            {
                return true;
            }
        }

        public override PlatformFeature[] Features
        {
            get
            {
                return new PlatformFeature[]
                {
                    PlatformFeature.Speech
                };
            }
        }

        public override string InstallerExtension
        {
            get
            {
                return ".dmg";
            }
        }

        private List<Action> todo;


        private DirectoryInfo workingDirectory;
        public override DirectoryInfo WorkingDirectory { get { return workingDirectory; } }

        public override bool ThreadedDrawing { get { return false; } }

        public MacPlatformTools()
        {
            Console.WriteLine("Mac Platform Start");
            Console.WriteLine("Working Dir " + Directory.GetCurrentDirectory());

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            workingDirectory = new DirectoryInfo(home + "/Documents/FPVTrackside");

            if (!WorkingDirectory.Exists)
            {
                WorkingDirectory.Create();
            }

            IOTools.WorkingDirectory = WorkingDirectory;

            Console.WriteLine("Home " + workingDirectory.FullName);


            DirectoryInfo baseDirectory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            Directory.SetCurrentDirectory(baseDirectory.FullName);

            if (baseDirectory.Exists)
            {
                CopyToHomeDir(baseDirectory);
            }

            todo = new List<Action>();
            maclipboard = new Maclipboard();

            VideoFrameworks.Available = new VideoFrameWork[]
            {
                //new FfmpegMediaPlatform.FfmpegMediaFramework()
            };
        }

        private void CopyToHomeDir(DirectoryInfo oldWorkDir)
        {
            Console.WriteLine("Source " + oldWorkDir.FullName);


            string[] toCopy = new string[] { "themes", "img", "bitmapfonts", "httpfiles", "formats", "sounds", "Content" };
            foreach (string copy in toCopy)
            {
                try
                {

                    DirectoryInfo newDirectory = WorkingDirectory.GetDirectories().FirstOrDefault(d => d.Name == copy);
                    DirectoryInfo oldDirectory = oldWorkDir.GetDirectories().FirstOrDefault(d => d.Name == copy);

                    if (oldDirectory != null && oldDirectory.Exists)
                    {
                        if (newDirectory == null)
                        {
                            newDirectory = WorkingDirectory.CreateSubdirectory(copy);
                        }

                        IOTools.CopyDirectory(oldDirectory, newDirectory, IOTools.Overwrite.IfNewer);
                    }
                }
                catch (Exception e)
                {
                    Logger.AllLog.LogException(this, e);
                }
            }
        }


        public override ITextRenderer CreateTextRenderer()
        {
            return new TextRenderBMP();
        }

        public override void Invoke(Action value)
        {
            lock (todo)
            {
                todo.Add(value);
            }
        }

        public void Do()
        {
            lock (todo)
            {
                foreach (Action action in todo)
                {
                    action();
                }
                todo.Clear();
            }
        }

        public override ISpeaker CreateSpeaker(string voice)
        {
            return new MacSpeaker();
        }

        public override string OpenFileDialog(string title, string fileExtension)
        {
            return "";
        }

        public override string SaveFileDialog(string title, string fileExtension)
        {
            return "";
        }

        public override void ShowNewWindow(Node node)
        {

        }

        public override void OpenFileManager(string directory)
        {
            System.Diagnostics.Process.Start("open", directory);
        }
    }
}

