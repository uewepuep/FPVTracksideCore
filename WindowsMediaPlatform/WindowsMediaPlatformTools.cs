using ImageServer;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WindowsPlatform;

namespace WindowsMediaPlatform
{
    public class WindowsMediaPlatformTools : WindowsPlatformTools
    {
        public WindowsMediaPlatformTools()
        {
            
        }

        public IEnumerable<VideoFrameWork> InitWindowsNativeVideoFrameworks(Microsoft.Xna.Framework.Graphics.GraphicsDevice device)
        {
            yield return (new MediaFoundation.MediaFoundationFramework(device));
            yield return (new DirectShow.DirectShowFramework());
        }

        public override bool Check(string toCheck)
        {
            if (toCheck == "GMFBridge")
            {
                try
                {
                    var obj = (DirectShowLib.GDCL.IGMFBridgeController)new DirectShowLib.GDCL.GMFBridgeController();
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return base.Check(toCheck);
        }

    }
}
