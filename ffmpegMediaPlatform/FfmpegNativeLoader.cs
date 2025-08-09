using System;
using System.Collections.Generic;
using System.IO;
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
            "/opt/homebrew/Cellar/ffmpeg/7.1.1_3/lib", // user-provided versioned path
            "/opt/homebrew/opt/ffmpeg/lib"             // stable symlink
        };

        public static void EnsureRegistered()
        {
            if (registered) return;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                registered = true;
                return;
            }

            // Hint AutoGen's internal resolver as well
            foreach (var root in rootCandidates)
            {
                if (Directory.Exists(root))
                {
                    ffmpeg.RootPath = root;
                    break;
                }
            }

            NativeLibrary.SetDllImportResolver(typeof(ffmpeg).Assembly, (name, assembly, path) =>
            {
                try
                {
                    // Ensure dependencies are preloaded so dyld can resolve chains
                    PreloadDependencies();

                    if (name.Contains("avutil", StringComparison.OrdinalIgnoreCase))
                        return LoadAny("libavutil", new[] { "59", "58", "57" });
                    if (name.Contains("avcodec", StringComparison.OrdinalIgnoreCase))
                        return LoadAny("libavcodec", new[] { "61", "60", "59" });
                    if (name.Contains("avformat", StringComparison.OrdinalIgnoreCase))
                        return LoadAny("libavformat", new[] { "61", "60", "59" });
                    if (name.Contains("swscale", StringComparison.OrdinalIgnoreCase))
                        return LoadAny("libswscale", new[] { "8", "7" });
                    if (name.Contains("swresample", StringComparison.OrdinalIgnoreCase))
                        return LoadAny("libswresample", new[] { "5", "4" });
                    if (name.Contains("avfilter", StringComparison.OrdinalIgnoreCase))
                        return LoadAny("libavfilter", new[] { "10", "9", "8" });
                    if (name.Contains("postproc", StringComparison.OrdinalIgnoreCase))
                        return LoadAny("libpostproc", new[] { "58", "57" });
                }
                catch (Exception)
                {
                    // Fallthrough to default resolver
                }
                return IntPtr.Zero;
            });

            registered = true;
        }

        private static void PreloadDependencies()
        {
            if (handleCache.Count > 0) return;
            // Load in order of lower-level to higher-level to satisfy dependencies
            TryLoadIntoCache("libavutil", new[] { "59", "58", "57" });
            TryLoadIntoCache("libswresample", new[] { "5", "4" });
            TryLoadIntoCache("libswscale", new[] { "8", "7" });
            TryLoadIntoCache("libavcodec", new[] { "61", "60", "59" });
            TryLoadIntoCache("libavformat", new[] { "61", "60", "59" });
            TryLoadIntoCache("libavfilter", new[] { "10", "9", "8" });
            TryLoadIntoCache("libpostproc", new[] { "58", "57" });
        }

        private static void TryLoadIntoCache(string baseName, string[] versions)
        {
            if (handleCache.ContainsKey(baseName)) return;
            IntPtr h = LoadAny(baseName, versions);
            if (h != IntPtr.Zero)
            {
                handleCache[baseName] = h;
            }
        }

        private static IntPtr LoadAny(string baseName, string[] versions)
        {
            // Prefer versioned dylibs present on disk
            foreach (string root in rootCandidates)
            {
                foreach (string v in versions)
                {
                    string candidate = Path.Combine(root, $"{baseName}.{v}.dylib");
                    if (File.Exists(candidate))
                    {
                        try
                        {
                            return NativeLibrary.Load(candidate);
                        }
                        catch
                        {
                            // try next
                        }
                    }
                }
            }

            // As a last resort, try unversioned name in default paths
            try { return NativeLibrary.Load(baseName); } catch { }
            return IntPtr.Zero;
        }
    }
} 