using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition.Text
{
    public class Style
    {
        public Color TextColor { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Border { get; set; }
        public string Font { get; set; }
        public static string DefaultFont = "Roboto";

        public Style()
        {
            TextColor = Color.White;
            Bold = false;
            Italic = false;
            Border = false;
            Font = DefaultFont;
        }

        public override bool Equals(object obj)
        {
            Style other = (Style)obj;
            if (other != null)
            {
                return other.Font == Font
                    //&& other.Bold == Bold
                    //&& other.TextColor == TextColor
                    //&& other.Italic == Italic
                    && other.Border == Border
                    ;
            }

            return false;
        }
        public override string ToString()
        {
            string str = Font;
            //str += " " + TextColor.PackedValue.ToString("X");

            //if (Bold) str += " Bold";
            //if (Italic) str += " Italic";
            if (Border) str += " Border";

            return str;
        }

        public override int GetHashCode()
        {
            int hashCode = -480836381;
            //hashCode = hashCode * -1521134295 + TextColor.GetHashCode();
            //hashCode = hashCode * -1521134295 + Bold.GetHashCode();
            //hashCode = hashCode * -1521134295 + Italic.GetHashCode();
            hashCode = hashCode * -1521134295 + Border.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Font);
            return hashCode;
        }
    }
}
