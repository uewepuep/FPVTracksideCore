using Composition.Layers;
using Composition;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsPlatform.GDI
{
    public class GDILayerStack : LayerStack
    {
        public GDILayerStack(PlatformTools platformTools) : base(null, (GameWindow)null, platformTools)
        {
        }

        public void Draw(System.Drawing.Graphics graphics)
        {

        }
    }
}
