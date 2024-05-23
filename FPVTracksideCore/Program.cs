using System;
using System.IO;
using System.Windows.Forms;

namespace FPVTracksideCore
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
                using (var game = new FPVTracksideCoreGame())
                {
                    game.Run();
                }
            }
#if !DEBUG
            catch (Exception ex)
            {
                Tools.Logger.UI.LogException(ex, ex);
                MessageBox.Show(ex.ToString(), ex.GetType().Name);
            }
#endif
        }
    }
}


