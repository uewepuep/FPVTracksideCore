using Composition;
using Composition.Layers;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RaceLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Timing;
using Timing.ImmersionRC;
using UI;
using UI.Nodes;
using Tools;
using WindowsPlatform;
using System.Windows.Forms;
using System.Drawing;

namespace FPVTracksideCore
{

    public class FPVTracksideCoreGame :
        UI.BaseGame
    {
        public FPVTracksideCoreGame()
            : base(new WindowsPlatformTools())
        {
            WindowsPlatformTools windowsPlatformTools = PlatformTools as WindowsPlatformTools;

            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetAssembly(typeof(FPVTracksideCoreGame));
            using (Stream resourceStream = assembly.GetManifestResourceStream("FPVTracksideCore.Icon.ico"))
            {
                if (resourceStream != null)
                {
                    windowsPlatformTools.SetGameWindow(Window, new Icon(resourceStream));
                }
                else
                {
                    string[] names = assembly.GetManifestResourceNames();
                }
            }
        }


        protected override void LoadContent()
        {

            DirectoryInfo eventDir = new DirectoryInfo(ApplicationProfileSettings.Instance.EventStorageLocation);
            DatabaseFactory.Init(new DB.DatabaseFactory(Data, eventDir));

            Theme.Initialise(PlatformTools.WorkingDirectory, "Dark");

            Form form = (Form)Form.FromHandle(Window.Handle);

            if (form != null)
            {
                form.MinimumSize = new System.Drawing.Size(600, 400);
            }

            base.LoadContent();
        }

    }
}
