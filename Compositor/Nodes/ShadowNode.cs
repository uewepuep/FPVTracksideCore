using Composition.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class ShadowNode : ImageNode
    {
        public ShadowNode() 
            : base(@"img/shadow.png")
        {
            RelativeBounds = new RectangleF(-0.1f, -0.1f, 1.2f, 1.2f);
            KeepAspectRatio = false;
        }
#if DEBUG
        protected override void SetParent(Node node)
        {
            base.SetParent(node);
            System.Diagnostics.Debug.Assert(!ParentChain.Any(p => p is RenderTargetNode));
        }
#endif

    }
}
