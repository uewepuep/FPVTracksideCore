using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace ImageServer
{
    public interface IHasModes
    {
        IEnumerable<Mode> GetModes();
        VideoConfig VideoConfig { get; }
    }

    public enum FrameWork
    {
        Undecided,
        DirectShow,
        MediaFoundation
    }

    public class Mode
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public float FrameRate { get; set; }
        public string Format { get; set; }
        public int Index { get; set; }
        public FrameWork FrameWork { get; set; }

        public Mode()
        {
            Index = -1;
            Width = 640;
            Height = 480;
            FrameRate = 25;
            Format = "";
            FrameWork = FrameWork.Undecided;
        }

        public override string ToString()
        {
            if (Index < 0)
                return "Default resolution / frame rate";

            double frameRate = Math.Round(FrameRate, 2);

            string framework = FrameWork.ToString();
            if (FrameWork == FrameWork.DirectShow)
            {
                framework += "(Legacy)";
            }

            return Width + " x " + Height + " " + frameRate + "hz " + framework + " " + Format;
        }

        public override int GetHashCode()
        {
            return ("" + Width + Height + FrameRate + Format + FrameWork).GetHashCode();
        }

        public override bool Equals(object obj)
        {
            Mode other = obj as Mode;
            if (other == null)
                return false;

            if (other.Width != Width) return false;
            if (other.Height != Height) return false;
            if (other.FrameRate != FrameRate) return false;
            if (other.Format != Format) return false;
            if (other.FrameWork != FrameWork) return false;

            return true;
        }
    }
}
