using Composition;
using Composition.Text;
using Microsoft.Xna.Framework;
using UI;
using RaceLib;
using ImageServer;

namespace FPVMacsideCore
{

    public class FPVMacsideCoreGame : UI.BaseGame
    {

        public MacPlatformTools Platform
        {
            get 
            {
                return (MacPlatformTools)PlatformTools;
            }
        }

        public FPVMacsideCoreGame()
            :base(new MacPlatformTools())
        {
          
        }

        protected override void Update(GameTime gameTime)
        {
            Platform.Do();
            base.Update(gameTime);
        }

        protected override void LoadContent()
        {
            // Pre-load FFmpeg bindings in background to avoid delays when entering replay mode
            FfmpegMediaPlatform.FfmpegGlobalInitializer.Initialize();

            Theme.Initialise(GraphicsDevice, PlatformTools.WorkingDirectory, "Dark");
            DirectoryInfo eventDir = new DirectoryInfo(ApplicationProfileSettings.Instance.EventStorageLocation);
            DatabaseFactory.Init(new DB.DatabaseFactory(Data, eventDir));


            base.LoadContent();
            BitmapFontLibrary.Init(PlatformTools.WorkingDirectory);

            VideoFrameWorks.Available.Add(new FfmpegMediaPlatform.FfmpegMediaFramework());
        }
    }

    
}
