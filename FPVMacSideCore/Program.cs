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
                // Pre-load FFmpeg bindings in background to avoid delays when entering replay mode
                FfmpegMediaPlatform.FfmpegGlobalInitializer.InitializeAsync();
                
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


