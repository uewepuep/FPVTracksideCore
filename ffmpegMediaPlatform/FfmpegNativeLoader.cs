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
            
            if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
            {
                return Path.Combine(appDirectory, "ffmpeg-libs", "arm64");
            }
            else
            {
                return Path.Combine(appDirectory, "ffmpeg-libs", "intel");
            }
        }

        public static void EnsureRegistered()
        {
            if (registered) return;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                registered = true;
                return;
            }

            // Set the bundled library path for FFmpeg.AutoGen first (for non-Mac platforms)
            var bundledPath = GetBundledLibraryPath();
            
            if (Directory.Exists(bundledPath))
            {
                ffmpeg.RootPath = bundledPath;
            }
            else
            {
                // Fallback to system paths
                foreach (var root in rootCandidates.Skip(1)) // Skip the bundled path
                {
                    if (Directory.Exists(root))
                    {
                        ffmpeg.RootPath = root;
                        break;
                    }
                }
            }

            registered = true;
        }

    }
} 