using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace Composition.Nodes
{
    public class PasswordNode : TextEditNode
    {
        public override string DrawingText
        {
            get
            {
                string hidden = "";
                for (int i = 0; i < text.Length; i++)
                {
                    hidden += "*";
                }

                return hidden;
            }
        }

        public PasswordNode(string text, Color textColor) 
            : base(text, textColor)
        {
        }
    }
}
