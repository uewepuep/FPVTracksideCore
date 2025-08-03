using System;
using System.IO;
using System.Collections.Generic;
using Composition;
using Composition.Nodes;
using Composition.Text;
using Tools;
using System.Linq;
using ImageServer;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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
                    PlatformFeature.Speech,
                    PlatformFeature.Video
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

            VideoFrameWorks.Available = new VideoFrameWork[]
            {
                new FfmpegMediaPlatform.FfmpegMediaFramework()
            };

            // Set application icon immediately during platform initialization
            SetApplicationIcon();
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
            try
            {
                // Check if we're on the main thread
                if (IsMainThread())
                {
                    return ShowOpenDialogOnMainThread(title, fileExtension);
                }
                else
                {
                    // Use TaskCompletionSource for clean async coordination
                    var tcs = new TaskCompletionSource<string>();
                    
                    // Queue to main thread via the existing Invoke mechanism
                    Invoke(() =>
                    {
                        try
                        {
                            var result = ShowOpenDialogOnMainThread(title, fileExtension);
                            tcs.SetResult(result);
                        }
                        catch (Exception ex)
                        {
                            tcs.SetException(ex);
                        }
                    });
                    
                    // Wait for the result (this will block until the main thread processes it)
                    return tcs.Task.Result;
                }
            }
            catch (Exception ex)
            {
                Logger.UI.LogException(this, ex);
                return null;
            }
        }

        private string ShowOpenDialogOnMainThread(string title, string fileExtension)
        {
            // Parse the filter string (e.g., "CSV|*.csv" or "XLSX|*.xlsx")
            string extension = ".txt";
            if (!string.IsNullOrEmpty(fileExtension))
            {
                var parts = fileExtension.Split('|');
                if (parts.Length > 1)
                {
                    var filterPart = parts[1];
                    if (filterPart.StartsWith("*."))
                    {
                        extension = filterPart.Substring(1);
                    }
                }
            }

            // Create NSOpenPanel
            var openPanel = objc_msgSend(objc_getClass("NSOpenPanel"), sel_registerName("openPanel"));
            
            // Set title
            if (!string.IsNullOrEmpty(title))
            {
                var titleStr = CreateNSString(title);
                objc_msgSend(openPanel, sel_registerName("setTitle:"), titleStr);
                objc_msgSend(titleStr, sel_registerName("release"));
            }

            // Set allowed file types
            var allowedTypes = CreateNSArray(new string[] { extension.TrimStart('.') });
            objc_msgSend(openPanel, sel_registerName("setAllowedFileTypes:"), allowedTypes);
            objc_msgSend(allowedTypes, sel_registerName("release"));

            // Show the dialog
            var response = objc_msgSend(openPanel, sel_registerName("runModal"));
            
            // NSModalResponseOK = 1
            if (response == (IntPtr)1)
            {
                // Get the URL
                var url = objc_msgSend(openPanel, sel_registerName("URL"));
                if (url != IntPtr.Zero)
                {
                    // Get the path NSString from URL
                    var pathNSString = objc_msgSend(url, sel_registerName("path"));
                    if (pathNSString != IntPtr.Zero)
                    {
                        // Get the C string from the NSString
                        var cStringPtr = objc_msgSend(pathNSString, sel_registerName("UTF8String"));
                        if (cStringPtr != IntPtr.Zero)
                        {
                            var path = Marshal.PtrToStringAnsi(cStringPtr);
                            return path;
                        }
                    }
                }
            }
            
            return null; // User cancelled or error
        }

        public override string SaveFileDialog(string title, string fileExtension)
        {
            try
            {
                // Check if we're on the main thread
                if (IsMainThread())
                {
                    return ShowSaveDialogOnMainThread(title, fileExtension);
                }
                else
                {
                    // Use TaskCompletionSource for clean async coordination
                    var tcs = new TaskCompletionSource<string>();
                    
                    // Queue to main thread via the existing Invoke mechanism
                    Invoke(() =>
                    {
                        try
                        {
                            var result = ShowSaveDialogOnMainThread(title, fileExtension);
                            tcs.SetResult(result);
                        }
                        catch (Exception ex)
                        {
                            tcs.SetException(ex);
                        }
                    });
                    
                    // Wait for the result (this will block until the main thread processes it)
                    return tcs.Task.Result;
                }
            }
            catch (Exception ex)
            {
                Logger.UI.LogException(this, ex);
                return null;
            }
        }

        private string ShowSaveDialogOnMainThread(string title, string fileExtension)
        {
            // Parse the filter string (e.g., "CSV|*.csv" or "XLSX|*.xlsx")
            string extension = ".txt";
            if (!string.IsNullOrEmpty(fileExtension))
            {
                var parts = fileExtension.Split('|');
                if (parts.Length > 1)
                {
                    var filterPart = parts[1];
                    if (filterPart.StartsWith("*."))
                    {
                        extension = filterPart.Substring(1);
                    }
                }
            }

            // Create NSSavePanel
            var savePanel = objc_msgSend(objc_getClass("NSSavePanel"), sel_registerName("savePanel"));
            
            // Set title
            if (!string.IsNullOrEmpty(title))
            {
                var titleStr = CreateNSString(title);
                objc_msgSend(savePanel, sel_registerName("setTitle:"), titleStr);
                objc_msgSend(titleStr, sel_registerName("release"));
            }

            // Set allowed file types
            var allowedTypes = CreateNSArray(new string[] { extension.TrimStart('.') });
            objc_msgSend(savePanel, sel_registerName("setAllowedFileTypes:"), allowedTypes);
            objc_msgSend(allowedTypes, sel_registerName("release"));

            // Show the dialog
            var response = objc_msgSend(savePanel, sel_registerName("runModal"));
            
            // NSModalResponseOK = 1
            if (response == (IntPtr)1)
            {
                // Get the URL
                var url = objc_msgSend(savePanel, sel_registerName("URL"));
                if (url != IntPtr.Zero)
                {
                    // Get the path NSString from URL
                    var pathNSString = objc_msgSend(url, sel_registerName("path"));
                    if (pathNSString != IntPtr.Zero)
                    {
                        // Get the C string from the NSString
                        var cStringPtr = objc_msgSend(pathNSString, sel_registerName("UTF8String"));
                        if (cStringPtr != IntPtr.Zero)
                        {
                            var path = Marshal.PtrToStringAnsi(cStringPtr);
                            Console.WriteLine($"SaveFileDialog returning path: '{path}'");
                            Logger.UI.Log(this, $"SaveFileDialog returning path: '{path}'");
                            return path;
                        }
                        else
                        {
                            Console.WriteLine("SaveFileDialog: cStringPtr is Zero");
                            Logger.UI.Log(this, "SaveFileDialog: cStringPtr is Zero");
                        }
                    }
                    else
                    {
                        Console.WriteLine("SaveFileDialog: pathNSString is Zero");
                        Logger.UI.Log(this, "SaveFileDialog: pathNSString is Zero");
                    }
                }
                else
                {
                    Console.WriteLine("SaveFileDialog: url is Zero");
                    Logger.UI.Log(this, "SaveFileDialog: url is Zero");
                }
            }
            else
            {
                Console.WriteLine($"SaveFileDialog: User cancelled or response was {response}");
                Logger.UI.Log(this, $"SaveFileDialog: User cancelled or response was {response}");
            }
            
            return null; // User cancelled or error
        }

        public override void ShowNewWindow(Node node)
        {

        }

                public override void OpenFileManager(string directory)
        {
            System.Diagnostics.Process.Start("open", directory);
        }

        private bool IsMainThread()
        {
            return objc_msgSend(objc_getClass("NSThread"), sel_registerName("isMainThread")) == (IntPtr)1;
        }

        #region macOS Native Interop

        [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
        private static extern IntPtr objc_getClass(string className);

        [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
        private static extern IntPtr sel_registerName(string selectorName);

        [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, string arg1);

        private static IntPtr CreateNSString(string str)
        {
            var nsStringClass = objc_getClass("NSString");
            var allocSelector = sel_registerName("alloc");
            var initSelector = sel_registerName("initWithUTF8String:");
            
            var nsString = objc_msgSend(nsStringClass, allocSelector);
            return objc_msgSend(nsString, initSelector, str);
        }

        private static IntPtr CreateNSArray(string[] strings)
        {
            var nsArrayClass = objc_getClass("NSMutableArray");
            var allocSelector = sel_registerName("alloc");
            var initSelector = sel_registerName("init");
            var addObjectSelector = sel_registerName("addObject:");
            
            var nsArray = objc_msgSend(nsArrayClass, allocSelector);
            nsArray = objc_msgSend(nsArray, initSelector);
            
            foreach (var str in strings)
            {
                var nsString = CreateNSString(str);
                objc_msgSend(nsArray, addObjectSelector, nsString);
                objc_msgSend(nsString, sel_registerName("release"));
            }
            
            return nsArray;
        }

        #endregion

        public void SetApplicationIcon()
        {
            try
            {
                // Look for AppIcon.icns in the application bundle or base directory
                string[] possiblePaths = {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppIcon.icns"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Resources", "AppIcon.icns"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "AppIcon.icns")
                };

                string iconPath = null;
                foreach (string path in possiblePaths)
                {
                    Console.WriteLine($"Checking icon path: {path}");
                    if (File.Exists(path))
                    {
                        iconPath = path;
                        Console.WriteLine($"Found icon at: {iconPath}");
                        break;
                    }
                }

                if (iconPath != null)
                {
                    // Check if we can access Cocoa APIs (might not be available in all contexts)
                    var nsImageClass = objc_getClass("NSImage");
                    if (nsImageClass == IntPtr.Zero)
                    {
                        Console.WriteLine("NSImage class not available, skipping programmatic icon setting");
                        return;
                    }

                    // Load the icon using NSImage
                    var allocSelector = sel_registerName("alloc");
                    var initWithContentsOfFileSelector = sel_registerName("initWithContentsOfFile:");
                    
                    var nsImage = objc_msgSend(nsImageClass, allocSelector);
                    if (nsImage == IntPtr.Zero)
                    {
                        Console.WriteLine("Failed to allocate NSImage");
                        return;
                    }

                    var iconPathNSString = CreateNSString(iconPath);
                    if (iconPathNSString == IntPtr.Zero)
                    {
                        Console.WriteLine("Failed to create NSString");
                        objc_msgSend(nsImage, sel_registerName("release"));
                        return;
                    }

                    nsImage = objc_msgSend(nsImage, initWithContentsOfFileSelector, iconPathNSString);
                    objc_msgSend(iconPathNSString, sel_registerName("release"));
                    
                    if (nsImage != IntPtr.Zero)
                    {
                        // Set the icon on NSApplication - but only if we have an application instance
                        var nsAppClass = objc_getClass("NSApplication");
                        if (nsAppClass != IntPtr.Zero)
                        {
                            var sharedApplicationSelector = sel_registerName("sharedApplication");
                            var setApplicationIconImageSelector = sel_registerName("setApplicationIconImage:");
                            
                            var nsApp = objc_msgSend(nsAppClass, sharedApplicationSelector);
                            if (nsApp != IntPtr.Zero)
                            {
                                objc_msgSend(nsApp, setApplicationIconImageSelector, nsImage);
                                Console.WriteLine($"Application icon set from: {iconPath}");
                                
                                // Also try to set it as the dock tile image
                                try
                                {
                                    var dockTileSelector = sel_registerName("dockTile");
                                    var setContentViewSelector = sel_registerName("setContentView:");
                                    var dockTile = objc_msgSend(nsApp, dockTileSelector);
                                    if (dockTile != IntPtr.Zero)
                                    {
                                        // Create an NSImageView with our icon
                                        var nsImageViewClass = objc_getClass("NSImageView");
                                        if (nsImageViewClass != IntPtr.Zero)
                                        {
                                            var imageView = objc_msgSend(nsImageViewClass, allocSelector);
                                            var initSelector = sel_registerName("init");
                                            imageView = objc_msgSend(imageView, initSelector);
                                            
                                            var setImageSelector = sel_registerName("setImage:");
                                            objc_msgSend(imageView, setImageSelector, nsImage);
                                            objc_msgSend(dockTile, setContentViewSelector, imageView);
                                            
                                            var displaySelector = sel_registerName("display");
                                            objc_msgSend(dockTile, displaySelector);
                                            
                                            objc_msgSend(imageView, sel_registerName("release"));
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Failed to set dock tile: {ex.Message}");
                                }
                            }
                            else
                            {
                                Console.WriteLine("NSApplication instance not available");
                            }
                        }
                        
                        objc_msgSend(nsImage, sel_registerName("release"));
                    }
                    else
                    {
                        Console.WriteLine($"Failed to load icon from: {iconPath}");
                    }
                }
                else
                {
                    Console.WriteLine("AppIcon.icns not found in any expected location");
                    foreach (string path in possiblePaths)
                    {
                        Console.WriteLine($"  Checked: {path}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting application icon: {ex.Message}");
                // Don't log to Logger.AllLog here as it might not be initialized yet
            }
        }
    }
}

