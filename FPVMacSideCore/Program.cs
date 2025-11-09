using System;
using System.IO;

namespace FPVMacsideCore
{
    /// <summary>
    /// The main class.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        //[STAThread]
        [MTAThread]
        static void Main()
        {
            FPVMacsideCoreGame game = null;
            
            // Add exit handlers to ensure proper cleanup
            Console.CancelKeyPress += (sender, e) =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Application interrupted - performing cleanup...");
                PerformCleanup();
                Environment.Exit(0);
            };

            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Application exiting - performing cleanup...");
                PerformCleanup();
            };
            
#if !DEBUG
            try
            {
#endif
                // Pre-load FFmpeg bindings in background to avoid delays when entering replay mode
                FfmpegMediaPlatform.FfmpegGlobalInitializer.InitializeAsync();
                
                game = new FPVMacsideCoreGame();
                using (game)
                {
                    game.Run();
                }
                // Game disposed here - check if this takes time
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Game disposed");
#if !DEBUG
            }
            catch (Exception ex)
            {
                Tools.Logger.UI.LogException(ex, ex);
            }
            finally
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Application cleanup starting...");
                PerformCleanup();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Finally block completed, exiting Main...");
            }
#else
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Application cleanup starting...");
            PerformCleanup();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] DEBUG cleanup completed, exiting Main...");
#endif
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Main method exiting...");
        }
        
        private static bool _cleanupPerformed = false;

        private static void PerformCleanup()
        {
            // Prevent double cleanup
            if (_cleanupPerformed)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Cleanup already performed, skipping...");
                return;
            }
            _cleanupPerformed = true;

            try
            {
                var cleanupStartTime = DateTime.Now;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting application cleanup...");

                // Set immediate termination flag for faster exit
                FfmpegMediaPlatform.FfmpegFrameSource.ImmediateTerminationOnExit = true;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Set immediate termination flag for FFmpeg");

                // Give the application a brief moment to finish current operations
                // Reduced from 500ms since we're using immediate termination
                var sleepStart = DateTime.Now;
                System.Threading.Thread.Sleep(50);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Initial sleep completed ({(DateTime.Now - sleepStart).TotalMilliseconds:F0}ms)");

                var ffmpegCleanupStart = DateTime.Now;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Cleaning up FFmpeg processes...");
                
                // Kill any remaining FFmpeg processes (covers all variants)
                string[] ffmpegNames = { "ffmpeg", "ffmpeg-arm", "ffmpeg-intel" };
                
                foreach (string processName in ffmpegNames)
                {
                    var processes = System.Diagnostics.Process.GetProcessesByName(processName);
                    if (processes.Length > 0)
                    {
                        Console.WriteLine($"Found {processes.Length} {processName} process(es) to clean up");
                    }
                    
                    foreach (var proc in processes)
                    {
                        try
                        {
                            if (!proc.HasExited)
                            {
                                var killStart = DateTime.Now;
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Killing {processName} process {proc.Id}");
                                proc.Kill();

                                // With immediate termination flag, use minimal wait
                                if (!proc.WaitForExit(100)) // Reduced from 3000ms to 100ms
                                {
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Process {proc.Id} did not exit within 100ms ({(DateTime.Now - killStart).TotalMilliseconds:F0}ms total)");
                                }
                                else
                                {
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Process {proc.Id} exited successfully ({(DateTime.Now - killStart).TotalMilliseconds:F0}ms)");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {processName} process {proc.Id} already exited");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error killing {processName} process {proc.Id}: {ex.Message}");
                        }
                        finally
                        {
                            try
                            {
                                proc.Dispose();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error disposing {processName} process {proc.Id}: {ex.Message}");
                            }
                        }
                    }
                }
                
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] FFmpeg process cleanup completed ({(DateTime.Now - ffmpegCleanupStart).TotalMilliseconds:F0}ms)");

                // Force garbage collection before cleaning up native libraries
                var gcStart = DateTime.Now;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Running garbage collection...");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Garbage collection completed ({(DateTime.Now - gcStart).TotalMilliseconds:F0}ms)");

                // Cleanup native FFmpeg bindings
                var nativeCleanupStart = DateTime.Now;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Cleaning up FFmpeg native bindings...");
                FfmpegMediaPlatform.FfmpegGlobalInitializer.Cleanup();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] FFmpeg native cleanup completed ({(DateTime.Now - nativeCleanupStart).TotalMilliseconds:F0}ms)");

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Application cleanup completed successfully (Total: {(DateTime.Now - cleanupStartTime).TotalMilliseconds:F0}ms)");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }
    }
}


