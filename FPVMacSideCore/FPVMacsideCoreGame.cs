using Composition;
using Composition.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UI;
using RaceLib;
using System.IO;

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
            // Initialize database FIRST, before anything else
            InitializeDatabase();

            Theme.Initialise(GraphicsDevice, PlatformTools.WorkingDirectory, "Dark");
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

        private void InitializeDatabase()
        {
            // Settings must be loaded before this is called
            if (ApplicationProfileSettings.Instance == null)
            {
                throw new InvalidOperationException("ApplicationProfileSettings not initialized");
            }

            // On macOS, EventStorageLocation is the base directory, events go in /events/ subdirectory
            string baseLocation = ApplicationProfileSettings.Instance.EventStorageLocationExpanded;

            Tools.Logger.UI.LogCall(this, $"STARTUP: EventStorageLocation (raw): {ApplicationProfileSettings.Instance.EventStorageLocation}");
            Tools.Logger.UI.LogCall(this, $"STARTUP: EventStorageLocationExpanded: {baseLocation}");

            string eventDirPath;
            // Only add "events" if it's not already in the path
            if (baseLocation.TrimEnd('/').EndsWith("events"))
            {
                eventDirPath = baseLocation;
                Tools.Logger.UI.LogCall(this, $"STARTUP: Path already ends with 'events', using as-is: {eventDirPath}");
            }
            else
            {
                eventDirPath = Path.Combine(baseLocation, "events");
                Tools.Logger.UI.LogCall(this, $"STARTUP: Adding 'events' to path: {eventDirPath}");
            }
            DirectoryInfo eventDir = new DirectoryInfo(eventDirPath);

            // Store for global access in IOTools
            Tools.IOTools.EventsDirectoryPath = eventDirPath;
            Tools.Logger.UI.LogCall(this, $"STARTUP: Events directory set to: {Tools.IOTools.EventsDirectoryPath}");

            DatabaseFactory.Init(new DB.DatabaseFactory(Data, eventDir));
        }
    }

    
}
