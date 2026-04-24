using Composition;
using Composition.Text;
using Microsoft.Xna.Framework;
using UI;
using RaceLib;
using ImageServer;

namespace FPVTuxsideCore
{
    public class FPVTuxsideCoreGame : UI.BaseGame
    {
        public TuxPlatformTools Platform => (TuxPlatformTools)PlatformTools;

        public FPVTuxsideCoreGame()
            : base(new TuxPlatformTools())
        {
        }

        protected override void Update(GameTime gameTime)
        {
            Platform.Do();
            base.Update(gameTime);
        }

        protected override void LoadContent()
        {
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
