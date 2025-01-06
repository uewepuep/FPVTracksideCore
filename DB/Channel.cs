using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Tools;
using RaceLib;
using System.ComponentModel;

namespace DB
{
    public class Channel : DatabaseObjectT<RaceLib.Channel>
    {
        public int Number { get; set; }
        public string Band { get; set; }
        public string ShortBand { get; set; }
        public char ChannelPrefix { get; set; }
        public int Frequency { get; set; }
        public string DisplayName { get; set; }

        public Channel() { }

        public Channel(RaceLib.Channel obj)
           : base(obj)
        {
            Band = obj.Band.ToString();
            ShortBand = obj.Band.GetCharacter();
        }
    }
}
