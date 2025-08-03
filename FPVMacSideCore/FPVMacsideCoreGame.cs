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

        private bool iconSetAfterLoad = false;
        private int framesSinceLoad = 0;
        
        protected override void Update(GameTime gameTime)
        {
            Platform.Do();
            
            // Set icon a few frames after the game has fully loaded
            // This ensures MonoGame has finished all its initialization
            if (!iconSetAfterLoad)
            {
                framesSinceLoad++;
                if (framesSinceLoad > 5) // Wait 5 frames after LoadContent
                {
                    Platform.SetApplicationIcon();
                    iconSetAfterLoad = true;
                }
            }
            
            base.Update(gameTime);
        }

        protected override void LoadContent()
        {
            Theme.Initialise(GraphicsDevice, PlatformTools.WorkingDirectory, "Dark");
            DirectoryInfo eventDir = new DirectoryInfo(ApplicationProfileSettings.Instance.EventStorageLocation);
            DatabaseFactory.Init(new DB.DatabaseFactory(Data, eventDir));


            base.LoadContent();
            BitmapFontLibrary.Init(PlatformTools.WorkingDirectory);
        }

        protected override void BeginRun()
        {
            base.BeginRun();
        }

        protected override void Initialize()
        {
            base.Initialize();
        }
    }

    
}
