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

#if !DEBUG
            try
#endif
            {
                // Add exit handlers to ensure proper cleanup
                Console.CancelKeyPress += (sender, e) =>
                {
                    Console.WriteLine("Application interrupted - performing cleanup...");
                    FfmpegMediaPlatform.FfmpegMediaFramework.PerformCleanup();
                    Environment.Exit(0);
                };

                AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
                {
                    Console.WriteLine("Application exiting - performing cleanup...");
                    FfmpegMediaPlatform.FfmpegMediaFramework.PerformCleanup();
                };

                using (var game = new FPVMacsideCoreGame())
                {
                   game.Run();
                }
            }
#if !DEBUG
            catch (Exception ex)
            {
                Tools.Logger.UI.LogException(ex, ex);
            }
#endif
        }
    }
}


