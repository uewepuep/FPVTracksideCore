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
                Console.WriteLine("Application interrupted - performing cleanup...");
                PerformCleanup();
                Environment.Exit(0);
            };
            
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                Console.WriteLine("Application exiting - performing cleanup...");
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
#if !DEBUG
            }
            catch (Exception ex)
            {
                Tools.Logger.UI.LogException(ex, ex);
            }
            finally
            {
                Console.WriteLine("Application cleanup starting...");
                PerformCleanup();
            }
#else
            Console.WriteLine("Application cleanup starting...");
            PerformCleanup();
#endif
        }
        
        private static void PerformCleanup()
        {
            try
            {
                Console.WriteLine("Starting application cleanup...");
                
                // Give the application a moment to finish current operations
                System.Threading.Thread.Sleep(500);
                
                Console.WriteLine("Cleaning up FFmpeg processes...");
                
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
                                Console.WriteLine($"Killing {processName} process {proc.Id}");
                                proc.Kill();
                                
                                // Wait for process to exit, but don't wait too long
                                if (!proc.WaitForExit(3000))
                                {
                                    Console.WriteLine($"Process {proc.Id} did not exit gracefully within 3 seconds");
                                }
                                else
                                {
                                    Console.WriteLine($"Process {proc.Id} exited successfully");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"{processName} process {proc.Id} already exited");
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
                
                Console.WriteLine("FFmpeg process cleanup completed");
                
                // Force garbage collection before cleaning up native libraries
                Console.WriteLine("Running garbage collection...");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                // Cleanup native FFmpeg bindings
                FfmpegMediaPlatform.FfmpegGlobalInitializer.Cleanup();
                
                Console.WriteLine("Application cleanup completed successfully");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }
    }
}


