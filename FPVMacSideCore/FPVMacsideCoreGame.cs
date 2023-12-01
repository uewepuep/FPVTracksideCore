using Composition;
using Composition.Text;
using Microsoft.Xna.Framework;
using UI;

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

            base.LoadContent();
            BitmapFontLibrary.Init(PlatformTools.WorkingDirectory);
        }
    }

    
}
