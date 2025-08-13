using System;
using System.Threading.Tasks;

namespace FfmpegMediaPlatform
{
    /// <summary>
    /// Global FFmpeg initializer to front-load binding initialization at application startup
    /// </summary>
    public static class FfmpegGlobalInitializer
    {
        private static bool _initialized = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// Initialize FFmpeg bindings early to avoid delays when first accessing replay functionality
        /// </summary>
        public static void InitializeAsync()
        {
            if (_initialized) return;

            // Run initialization in background thread to not block application startup
            Task.Run(() =>
            {
                lock (_lock)
                {
                    if (_initialized) return;

                    try
                    {
                        Console.WriteLine("FfmpegGlobalInitializer: Pre-loading FFmpeg bindings...");
                        FfmpegNativeLoader.EnsureRegistered();
                        _initialized = true;
                        Console.WriteLine("FfmpegGlobalInitializer: FFmpeg bindings pre-loaded successfully");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"FfmpegGlobalInitializer: Failed to pre-load FFmpeg bindings: {ex.Message}");
                        // Don't set initialized to true if it failed, so it can be retried later
                    }
                }
            });
        }

        /// <summary>
        /// Synchronous initialization for when immediate initialization is needed
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                try
                {
                    FfmpegNativeLoader.EnsureRegistered();
                    _initialized = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FfmpegGlobalInitializer: Failed to initialize FFmpeg bindings: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Check if FFmpeg bindings are already initialized
        /// </summary>
        public static bool IsInitialized => _initialized;
    }
}