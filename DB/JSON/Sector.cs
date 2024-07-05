using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class Sector
    {
        public int TrackElementStartIndex { get; set; }

        public int TrackElementEndIndex { get; set; }

        public float Length { get; set; }

        [Browsable(false)]
        public Color Color { get; set; }

        [Browsable(false)]
        public int Number { get; set; }

        public bool CalculateSpeed { get; set; }
    }
}
