using FPVTuxsideCore;

namespace FPVTuxsideCore
{
    public static class Program
    {
        [MTAThread]
        static void Main()
        {
#if !DEBUG
            try
#endif
            {
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

                using (var game = new FPVTuxsideCoreGame())
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
