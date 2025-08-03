using Composition;
using Composition.Text;
using Microsoft.Xna.Framework;
using UI;
using RaceLib;

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
            Theme.Initialise(GraphicsDevice, PlatformTools.WorkingDirectory, "Dark");
            DirectoryInfo eventDir = new DirectoryInfo(ApplicationProfileSettings.Instance.EventStorageLocation);
            DatabaseFactory.Init(new DB.DatabaseFactory(Data, eventDir));


            base.LoadContent();
            BitmapFontLibrary.Init(PlatformTools.WorkingDirectory);
            
            // Set application icon after everything is loaded
            Platform.SetApplicationIcon();
        }

        protected override void BeginRun()
        {
            base.BeginRun();
            // Also try setting the icon after the game window is fully created
            Platform.SetApplicationIcon();
        }

        protected override void Initialize()
        {
            // Set icon before MonoGame initializes its window
            Platform.SetApplicationIcon();
            base.Initialize();
        }
    }

    
}
