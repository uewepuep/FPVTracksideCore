using Composition;
using Composition.Nodes;
using Composition.Text;
using ImageServer;
using System.Diagnostics;
using Tools;

namespace FPVTuxsideCore
{
    public class TuxPlatformTools : PlatformTools
    {
        private TuxClipboard tuxClipboard;

        public override IClipboard Clipboard => tuxClipboard;

        public override bool Focused => true;

        public override PlatformFeature[] Features
        {
            get
            {
                return new PlatformFeature[]
                {
                    PlatformFeature.Speech,
                    PlatformFeature.Video
                };
            }
        }

        public override string InstallerExtension => ".AppImage";

        private List<Action> todo;

        private DirectoryInfo workingDirectory;
        public override DirectoryInfo WorkingDirectory => workingDirectory;

        public override bool ThreadedDrawing => false;

        public override Microsoft.Xna.Framework.Input.Keys[] CutCopyPasteModifierKeys =>
            [Microsoft.Xna.Framework.Input.Keys.LeftControl, Microsoft.Xna.Framework.Input.Keys.RightControl];

        public TuxPlatformTools() : this("FPVTrackside") { }

        public TuxPlatformTools(string appName)
        {
            Console.WriteLine("Tux Platform Start");
            Console.WriteLine("Working Dir " + Directory.GetCurrentDirectory());

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            workingDirectory = new DirectoryInfo(Path.Combine(home, "Documents", appName));

            if (!workingDirectory.Exists)
                workingDirectory.Create();

            IOTools.WorkingDirectory = workingDirectory;

            Console.WriteLine("Home " + workingDirectory.FullName);

            DirectoryInfo baseDirectory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            Directory.SetCurrentDirectory(baseDirectory.FullName);

            if (baseDirectory.Exists)
                CopyToHomeDir(baseDirectory);

            todo = new List<Action>();
            tuxClipboard = new TuxClipboard();
        }

        private void CopyToHomeDir(DirectoryInfo oldWorkDir)
        {
            Console.WriteLine("Source " + oldWorkDir.FullName);

            string[] toCopy = new string[] { "themes", "img", "bitmapfonts", "httpfiles", "formats", "sounds", "Content" };
            foreach (string copy in toCopy)
            {
                try
                {
                    DirectoryInfo newDirectory = workingDirectory.GetDirectories().FirstOrDefault(d => d.Name == copy);
                    DirectoryInfo oldDirectory = oldWorkDir.GetDirectories().FirstOrDefault(d => d.Name == copy);

                    if (oldDirectory != null && oldDirectory.Exists)
                    {
                        if (newDirectory == null)
                            newDirectory = workingDirectory.CreateSubdirectory(copy);

                        IOTools.CopyDirectory(oldDirectory, newDirectory, IOTools.Overwrite.IfNewer);
                    }
                }
                catch (Exception e)
                {
                    Logger.AllLog.LogException(this, e);
                }
            }

            string[] filesToCopy = new string[] { "Translations.xlsx" };
            foreach (string fileName in filesToCopy)
            {
                try
                {
                    FileInfo sourceFile = new FileInfo(Path.Combine(oldWorkDir.FullName, fileName));
                    FileInfo destFile = new FileInfo(Path.Combine(workingDirectory.FullName, fileName));

                    if (sourceFile.Exists && (!destFile.Exists || sourceFile.LastWriteTime > destFile.LastWriteTime))
                    {
                        sourceFile.CopyTo(destFile.FullName, true);
                        Console.WriteLine($"Copied {fileName} to working directory");
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
            return new TextRenderSkia();
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
                    action();
                todo.Clear();
            }
        }

        public override ISpeaker CreateSpeaker(string voice)
        {
            return new TuxSpeaker();
        }

        public override string OpenFileDialog(string title, string fileExtension)
        {
            try
            {
                string filter = BuildKdialogFilter(fileExtension);
                string args = $"--getopenfilename . \"{filter}\"";
                if (!string.IsNullOrEmpty(title))
                    args = $"--title \"{title}\" " + args;

                var psi = new ProcessStartInfo("kdialog", args)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                string result = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                return proc.ExitCode == 0 ? result : null;
            }
            catch (Exception ex)
            {
                Logger.UI.LogException(this, ex);
                return null;
            }
        }

        public override string SaveFileDialog(string title, string fileExtension)
        {
            try
            {
                string filter = BuildKdialogFilter(fileExtension);
                string args = $"--getsavefilename . \"{filter}\"";
                if (!string.IsNullOrEmpty(title))
                    args = $"--title \"{title}\" " + args;

                var psi = new ProcessStartInfo("kdialog", args)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                string result = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                return proc.ExitCode == 0 ? result : null;
            }
            catch (Exception ex)
            {
                Logger.UI.LogException(this, ex);
                return null;
            }
        }

        private string BuildKdialogFilter(string fileExtension)
        {
            if (string.IsNullOrEmpty(fileExtension))
                return "*";

            // Input format: "CSV|*.csv" → kdialog wants "*.csv"
            var parts = fileExtension.Split('|');
            return parts.Length > 1 ? parts[1] : parts[0];
        }

        public override void ShowNewWindow(Node node) { }

        public override void OpenFileManager(string directory)
        {
            Process.Start("xdg-open", directory);
        }
    }
}
