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
            Theme.Initialise(PlatformTools.WorkingDirectory, "Dark");

            DirectoryInfo eventDir = new DirectoryInfo(GeneralSettings.Instance.EventStorageLocation);
            DatabaseFactory.Init(new DB.DatabaseFactory(Data, eventDir));


            base.LoadContent();
            BitmapFontLibrary.Init(PlatformTools.WorkingDirectory);
        }
    }

    
}
