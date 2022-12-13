using Composition;
using Composition.Text;
using Microsoft.Xna.Framework;
using UI;

namespace FPVMacsideCore
{

    public class FPVMacsideGame : UI.BaseGame
    {

        public MacPlatform Platform
        {
            get 
            {
                return (MacPlatform)PlatformTools;
            }
        }

        public FPVMacsideGame()
            :base(new MacPlatform())
        {
          
        }

        protected override void Update(GameTime gameTime)
        {
            Platform.Do();
            base.Update(gameTime);
        }

        protected override void LoadContent()
        {
            GeneralSettings.Initialise();
            Theme.Initialise(PlatformTools.WorkingDirectory, "Dark");

            base.LoadContent();
            BitmapFontLibrary.Init(PlatformTools.WorkingDirectory);
        }
    }

    
}
