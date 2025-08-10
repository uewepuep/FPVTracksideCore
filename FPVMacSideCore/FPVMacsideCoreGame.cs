using Composition;
using Composition.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
            // Mac-specific graphics configuration
            GraphicsDeviceManager.PreferredBackBufferWidth = 1842;
            GraphicsDeviceManager.PreferredBackBufferHeight = 1000;
            GraphicsDeviceManager.GraphicsProfile = GraphicsProfile.HiDef;
            GraphicsDeviceManager.SynchronizeWithVerticalRetrace = false; // Disable VSync on Mac for better performance
            GraphicsDeviceManager.ApplyChanges();
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
            
            // macOS Black Screen Fix: Force initial draw to ensure graphics device is properly initialized
            if (GraphicsDevice != null)
            {
                this.GraphicsDevice.Clear(ClearColor);
                this.Draw(new GameTime());
                this.GraphicsDevice.Present();
            }
        }
    }

    
}
