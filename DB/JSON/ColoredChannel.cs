using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class ColoredChannel : Channel
    {
        public string Color { get; set; }

        public ColoredChannel(RaceLib.Channel channel, string color) 
        {
            Copy(channel, this);
            Color = color;
        }

        public ColoredChannel(Channel channel, string color)
        {
            Copy(channel, this);
            Color = color;
        }
    }
}
