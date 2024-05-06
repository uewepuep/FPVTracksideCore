using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Composition.Nodes;
using Tools;

namespace Composition.Layers
{
    public class MouseLayer : CompositorLayer
    {
        private ImageNode cursor;

        public TimeSpan VisibilityTimeOut { get; set; }

        private DateTime lastMove;

        public MouseLayer(GraphicsDevice device, string mouseFilename)
            : base(device)
        {
            VisibilityTimeOut = TimeSpan.FromSeconds(1);

            cursor = new ImageNode(mouseFilename);
            Root.AddChild(cursor);
        }

        protected override void OnUpdate(GameTime gameTime)
        {
            MouseState newState = Mouse.GetState();
           
            if (cursor.Bounds.X != newState.X || cursor.Bounds.Y != newState.Y)
            {
                if (cursor.Texture != null)
                {
                    cursor.BoundsF = new RectangleF(newState.X, newState.Y, cursor.Texture.Width, cursor.Texture.Height);
                }
                lastMove = DateTime.Now;
                cursor.Visible = true;
            }

            if (DateTime.Now > lastMove + VisibilityTimeOut)
            {
                cursor.Visible = false;
            }
        }
    }
}
