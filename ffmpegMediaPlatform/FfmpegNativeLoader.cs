using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace FfmpegMediaPlatform
{
    internal static class FfmpegNativeLoader
    {
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        private static bool registered;
        private static readonly Dictionary<string, IntPtr> handleCache = new();
        private static readonly string[] rootCandidates = new[]
        {
            GetBundledLibraryPath(),                   // bundled libraries first
            "/opt/homebrew/Cellar/ffmpeg/7.1.1_3/lib", // user-provided versioned path
            "/opt/homebrew/opt/ffmpeg/lib"             // stable symlink
        };

        private static string GetBundledLibraryPath()
        {
            var assemblyLocation = typeof(FfmpegNativeLoader).Assembly.Location;
            string appDirectory;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On Windows, use the main application's base directory instead of assembly location
                // This ensures we look where the FFmpeg libraries are actually copied (next to the main executable)
                appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                Console.WriteLine($"FfmpegNativeLoader.GetBundledLibraryPath: Windows - Using app base directory: {appDirectory}");
            }
            else
            {
                // On Mac/Linux, keep original behavior using assembly location
                appDirectory = Path.GetDirectoryName(assemblyLocation);
                Console.WriteLine($"FfmpegNativeLoader.GetBundledLibraryPath: Mac/Linux - Using assembly directory: {appDirectory}");
            }

            Console.WriteLine($"FfmpegNativeLoader.GetBundledLibraryPath: Assembly location: {assemblyLocation}");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Use unified macOS directory (ARM64 compatible libraries)
                var path = Path.Combine(appDirectory, "ffmpeg-libs", "macos");
                Console.WriteLine($"FfmpegNativeLoader.GetBundledLibraryPath: macOS path: {path} (Architecture: {RuntimeInformation.OSArchitecture})");
                Console.WriteLine($"FfmpegNativeLoader.GetBundledLibraryPath: Directory exists: {Directory.Exists(path)}");
                if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path, "*.dylib");
                    Console.WriteLine($"FfmpegNativeLoader.GetBundledLibraryPath: Found {files.Length} dylib files");
                }
                return path;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var path = Path.Combine(appDirectory, "ffmpeg-libs", "windows");
                Console.WriteLine($"FfmpegNativeLoader.GetBundledLibraryPath: Windows path: {path}");
                return path;
            }

            Console.WriteLine("FfmpegNativeLoader.GetBundledLibraryPath: No platform-specific path found");
            return null;
        }

        public static void EnsureRegistered()
        {
            if (registered) return;

            Console.WriteLine("FfmpegNativeLoader.EnsureRegistered: Starting registration...");
            Console.WriteLine($"FfmpegNativeLoader.EnsureRegistered: Platform = {RuntimeInformation.OSDescription}");
            Console.WriteLine($"FfmpegNativeLoader.EnsureRegistered: Architecture = {RuntimeInformation.OSArchitecture}");

            // IMPORTANT: Set the bundled library path for FFmpeg.AutoGen BEFORE any FFmpeg functions are called
            var bundledPath = GetBundledLibraryPath();
            Console.WriteLine($"FfmpegNativeLoader.EnsureRegistered: Bundled path: {bundledPath}");

            if (bundledPath != null && Directory.Exists(bundledPath))
            {
                Console.WriteLine($"FfmpegNativeLoader.EnsureRegistered: Directory exists - Setting ffmpeg.RootPath to: {bundledPath}");
                Console.WriteLine($"Current process architecture: {RuntimeInformation.ProcessArchitecture}");
                Console.WriteLine($"Current OS architecture: {RuntimeInformation.OSArchitecture}");
                
                // List files in the directory for debugging
                var files = Directory.GetFiles(bundledPath, "*.dylib");
                Console.WriteLine($"Found {files.Length} dylib files in directory:");
                foreach (var file in files.Take(5)) // Show first 5 files
                {
                    Console.WriteLine($"  - {Path.GetFileName(file)}");
                }
                
                ffmpeg.RootPath = bundledPath;
                Console.WriteLine($"FFmpeg native libraries loaded from: {bundledPath}");

                // Check if essential libraries exist (platform-specific)
                string[] requiredLibs;
                string[] dependencyLibs = new string[0]; // Initialize empty for non-Windows

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    requiredLibs = new[] { "avcodec-61.dll", "avformat-61.dll", "avutil-59.dll", "swscale-8.dll", "swresample-5.dll" };
                    dependencyLibs = new[] { "libiconv-2.dll", "libwinpthread-1.dll", "zlib1.dll" };
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    requiredLibs = new[] { "libavcodec.dylib", "libavformat.dylib", "libavutil.dylib", "libswscale.dylib", "libswresample.dylib" };
                }
                else
                {
                    requiredLibs = new[] { "libavcodec.so", "libavformat.so", "libavutil.so", "libswscale.so", "libswresample.so" };
                }

                bool allLibsExist = true;
                foreach (var lib in requiredLibs)
                {
                    var libPath = Path.Combine(bundledPath, lib);
                    bool exists = File.Exists(libPath);
                    Console.WriteLine($"  {lib}: {(exists ? "EXISTS" : "MISSING")}");
                    if (!exists) allLibsExist = false;
                }

                // Check dependency libraries (Windows only)
                foreach (var lib in dependencyLibs)
                {
                    var libPath = Path.Combine(bundledPath, lib);
                    bool exists = File.Exists(libPath);
                    Console.WriteLine($"  {lib} (dependency): {(exists ? "EXISTS" : "MISSING")}");
                    if (!exists) allLibsExist = false;
                }

                if (!allLibsExist)
                {
                    throw new FileNotFoundException("Required FFmpeg library dependencies are missing. Please ensure all FFmpeg libraries and dependencies are present.");
                }

                // Set FFmpeg.AutoGen root path explicitly
                ffmpeg.RootPath = bundledPath;
                Console.WriteLine($"Set ffmpeg.RootPath to: {ffmpeg.RootPath}");

                // Add bundled path to PATH environment variable for dependency resolution
                var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (!currentPath.Contains(bundledPath))
                {
                    string pathSeparator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ";" : ":";
                    Environment.SetEnvironmentVariable("PATH", bundledPath + pathSeparator + currentPath);
                    Console.WriteLine($"Added to PATH: {bundledPath}");
                }

                // Pre-load FFmpeg libraries in dependency order to avoid issues (Windows only)
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        Console.WriteLine("Pre-loading FFmpeg libraries in dependency order (Windows)...");

                        // Load libraries in dependency order
                        var libPaths = new[]
                        {
                            Path.Combine(bundledPath, "avutil-59.dll"),
                            Path.Combine(bundledPath, "swresample-5.dll"),
                            Path.Combine(bundledPath, "swscale-8.dll"),
                            Path.Combine(bundledPath, "avcodec-61.dll"),
                            Path.Combine(bundledPath, "avformat-61.dll"),
                            Path.Combine(bundledPath, "avfilter-10.dll"),
                            Path.Combine(bundledPath, "avdevice-61.dll")
                        };

                        foreach (var libPath in libPaths)
                        {
                            if (File.Exists(libPath))
                            {
                                try
                                {
                                    var handle = LoadLibrary(libPath);
                                    Console.WriteLine($"Pre-loaded: {Path.GetFileName(libPath)} -> Handle: {handle}");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Failed to pre-load {Path.GetFileName(libPath)}: {ex.Message}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Pre-loading libraries failed: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("Skipping library pre-loading on macOS (not needed with FFmpeg.AutoGen)");
                }

                // Force FFmpeg.AutoGen to initialize with the new path
                try
                {
                    Console.WriteLine("Testing FFmpeg.AutoGen initialization...");
                    Console.WriteLine($"ffmpeg.RootPath is set to: {ffmpeg.RootPath}");

                    ffmpeg.av_log_set_level(ffmpeg.AV_LOG_INFO);

                    // Call a simple FFmpeg function to trigger initialization
                    var version = ffmpeg.av_version_info();
                    Console.WriteLine($"FFmpeg version: {version}");

                    // Test a few more functions to verify bindings
                    var codecVersion = ffmpeg.avcodec_version();
                    var formatVersion = ffmpeg.avformat_version();
                    Console.WriteLine($"Codec version: {codecVersion}");
                    Console.WriteLine($"Format version: {formatVersion}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FFmpeg initialization test failed: {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    throw new NotSupportedException($"FFmpeg initialization failed. The libraries may be incompatible or dependencies are missing: {ex.Message}", ex);
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Console.WriteLine($"FfmpegNativeLoader.EnsureRegistered: Bundled path not found or doesn't exist");
                Console.WriteLine($"FfmpegNativeLoader.EnsureRegistered: bundledPath = {bundledPath}");
                Console.WriteLine($"FfmpegNativeLoader.EnsureRegistered: Directory.Exists = {(bundledPath != null ? Directory.Exists(bundledPath) : "bundledPath is null")}");
                
                // Fallback to system paths for Mac
                Console.WriteLine("FfmpegNativeLoader.EnsureRegistered: Trying system paths...");
                foreach (var root in rootCandidates.Skip(1)) // Skip the bundled path
                {
                    Console.WriteLine($"FfmpegNativeLoader.EnsureRegistered: Checking system path: {root}");
                    if (Directory.Exists(root))
                    {
                        ffmpeg.RootPath = root;
                        Console.WriteLine($"FFmpeg native libraries loaded from system path: {root}");
                        break;
                    }
                    else
                    {
                        Console.WriteLine($"FfmpegNativeLoader.EnsureRegistered: System path does not exist: {root}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"FfmpegNativeLoader.EnsureRegistered: No bundled FFmpeg libraries found for platform: {RuntimeInformation.OSDescription}");
                throw new PlatformNotSupportedException("FFmpeg libraries not found. Please ensure the appropriate FFmpeg libraries are included in the application package.");
            }

            registered = true;
            Console.WriteLine("FfmpegNativeLoader.EnsureRegistered: Registration completed");
        }
    }
} 