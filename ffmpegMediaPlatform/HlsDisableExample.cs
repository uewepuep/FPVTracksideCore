using System;

namespace FfmpegMediaPlatform
{
    /// <summary>
    /// Example usage of HLS configuration and disabling functionality.
    /// This file demonstrates different ways to control HLS features.
    /// </summary>
    public static class HlsDisableExample
    {
        /// <summary>
        /// Example 1: Disable HLS using the configuration class (recommended)
        /// </summary>
        public static void DisableHlsExample1()
        {
            // Disable HLS completely
            HlsConfig.DisableHls();
            
            // Check status
            Console.WriteLine($"HLS Status: {HlsConfig.GetStatus()}");
            
            // HLS is now disabled - no HTTP server, no HLS file generation
            // Only RGBA live processing remains active
        }

        /// <summary>
        /// Example 2: Disable HLS using the direct property
        /// </summary>
        public static void DisableHlsExample2()
        {
            // Disable HLS directly
            HlsConfig.HlsEnabled = false;
            
            // Or disable directly on the frame source class
            FfmpegHlsLiveFrameSource.HlsEnabled = false;
        }

        /// <summary>
        /// Example 3: Conditionally disable HLS based on configuration
        /// </summary>
        public static void DisableHlsExample3()
        {
            // Check if HLS should be disabled based on some condition
            bool shouldDisableHls = CheckIfHlsShouldBeDisabled();
            
            if (shouldDisableHls)
            {
                HlsConfig.DisableHls();
                Console.WriteLine("HLS disabled based on configuration");
            }
            else
            {
                HlsConfig.EnableHls();
                Console.WriteLine("HLS enabled based on configuration");
            }
        }

        /// <summary>
        /// Example 4: Disable HLS at application startup
        /// </summary>
        public static void DisableHlsAtStartup()
        {
            // This should be called early in your application startup
            // before any video sources are created
            
            // Disable HLS for performance reasons
            HlsConfig.DisableHls();
            
            // Or disable based on environment variable
            string hlsEnvVar = Environment.GetEnvironmentVariable("DISABLE_HLS");
            if (!string.IsNullOrEmpty(hlsEnvVar) && hlsEnvVar.ToLower() == "true")
            {
                HlsConfig.DisableHls();
                Console.WriteLine("HLS disabled via environment variable DISABLE_HLS=true");
            }
        }

        /// <summary>
        /// Example 5: Runtime HLS control
        /// </summary>
        public static void RuntimeHlsControl()
        {
            // You can enable/disable HLS at runtime
            // Note: This will only affect NEW video sources, not existing ones
            
            Console.WriteLine($"Initial HLS Status: {HlsConfig.GetStatus()}");
            
            // Disable HLS
            HlsConfig.DisableHls();
            Console.WriteLine($"After Disable: {HlsConfig.GetStatus()}");
            
            // Re-enable HLS
            HlsConfig.EnableHls();
            Console.WriteLine($"After Enable: {HlsConfig.GetStatus()}");
        }

        /// <summary>
        /// Example 6: Check HLS status before operations
        /// </summary>
        public static void CheckHlsStatusBeforeOperations()
        {
            if (HlsConfig.HlsEnabled)
            {
                Console.WriteLine("HLS is enabled - HTTP streaming will be available");
                Console.WriteLine($"HLS will be available at: http://localhost:{HlsConfig.HttpPort}/hls/stream.m3u8");
            }
            else
            {
                Console.WriteLine("HLS is disabled - only RGBA live processing is active");
                Console.WriteLine("No HTTP server will be started");
                Console.WriteLine("No HLS files will be generated");
            }
        }

        private static bool CheckIfHlsShouldBeDisabled()
        {
            // Example logic - you can implement your own conditions
            // For example:
            // - Performance mode enabled
            // - Low memory conditions
            // - User preference
            // - Environment configuration
            
            return false; // Replace with your logic
        }
    }
}
