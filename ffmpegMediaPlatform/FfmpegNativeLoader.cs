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
            var appDirectory = Path.GetDirectoryName(assemblyLocation);
            
            Console.WriteLine($"FfmpegNativeLoader.GetBundledLibraryPath: Assembly location: {assemblyLocation}");
            Console.WriteLine($"FfmpegNativeLoader.GetBundledLibraryPath: App directory: {appDirectory}");
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
                {
                    var path = Path.Combine(appDirectory, "ffmpeg-libs", "arm64");
                    Console.WriteLine($"FfmpegNativeLoader.GetBundledLibraryPath: macOS ARM64 path: {path}");
                    return path;
                }
                else
                {
                    var path = Path.Combine(appDirectory, "ffmpeg-libs", "intel");
                    Console.WriteLine($"FfmpegNativeLoader.GetBundledLibraryPath: macOS Intel path: {path}");
                    return path;
                }
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
            
            // IMPORTANT: Set the bundled library path for FFmpeg.AutoGen BEFORE any FFmpeg functions are called
            var bundledPath = GetBundledLibraryPath();
            Console.WriteLine($"FfmpegNativeLoader.EnsureRegistered: Bundled path: {bundledPath}");
            
            if (bundledPath != null && Directory.Exists(bundledPath))
            {
                Console.WriteLine($"FfmpegNativeLoader.EnsureRegistered: Setting ffmpeg.RootPath to: {bundledPath}");
                Console.WriteLine($"Current process architecture: {RuntimeInformation.ProcessArchitecture}");
                Console.WriteLine($"Current OS architecture: {RuntimeInformation.OSArchitecture}");
                ffmpeg.RootPath = bundledPath;
                Console.WriteLine($"FFmpeg native libraries loaded from: {bundledPath}");
                
                // Check if essential DLLs exist
                string[] requiredDlls = { "avcodec-61.dll", "avformat-61.dll", "avutil-59.dll", "swscale-8.dll", "swresample-5.dll" };
                bool allDllsExist = true;
                foreach (var dll in requiredDlls)
                {
                    var dllPath = Path.Combine(bundledPath, dll);
                    bool exists = File.Exists(dllPath);
                    Console.WriteLine($"  {dll}: {(exists ? "EXISTS" : "MISSING")}");
                    if (!exists) allDllsExist = false;
                }
                
                // Check dependency DLLs that are critical for Windows
                string[] dependencyDlls = { "libiconv-2.dll", "libwinpthread-1.dll", "zlib1.dll" };
                foreach (var dll in dependencyDlls)
                {
                    var dllPath = Path.Combine(bundledPath, dll);
                    bool exists = File.Exists(dllPath);
                    Console.WriteLine($"  {dll} (dependency): {(exists ? "EXISTS" : "MISSING")}");
                    if (!exists) allDllsExist = false;
                }
                
                if (!allDllsExist)
                {
                    throw new FileNotFoundException("Required FFmpeg library dependencies are missing. Please ensure all FFmpeg DLLs and dependencies are present.");
                }
                
                // Add bundled path to PATH environment variable for dependency resolution
                var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (!currentPath.Contains(bundledPath))
                {
                    Environment.SetEnvironmentVariable("PATH", bundledPath + ";" + currentPath);
                    Console.WriteLine($"Added to PATH: {bundledPath}");
                }
                
                // Force FFmpeg.AutoGen to initialize with the new path
                try
                {
                    // Call a simple FFmpeg function to trigger initialization
                    var version = ffmpeg.av_version_info();
                    Console.WriteLine($"FFmpeg version: {version}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FFmpeg initialization test failed: {ex.Message}");
                    throw new NotSupportedException($"FFmpeg initialization failed. The libraries may be incompatible or dependencies are missing: {ex.Message}", ex);
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Fallback to system paths for Mac
                foreach (var root in rootCandidates.Skip(1)) // Skip the bundled path
                {
                    if (Directory.Exists(root))
                    {
                        ffmpeg.RootPath = root;
                        Console.WriteLine($"FFmpeg native libraries loaded from system path: {root}");
                        break;
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