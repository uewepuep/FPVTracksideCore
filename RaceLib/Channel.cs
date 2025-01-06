using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Tools;

namespace RaceLib
{
    public enum Band
    {
        None = 0,
        Fatshark = 1,
        Raceband = 2,
        A = 3,
        B = 4,
        E = 5,
        DJIFPVHD = 6,
        SharkByte = 7,
        HDZero = 7,
        LowBand = 8,
        Diatone = 9,

        DJIO3 = 10
    }

    public enum BandType
    {
        Analogue,
        DJIDigital,
        HDZeroDigital
    }

    public class Channel : BaseObject
    {
        [ReadOnly(true)]
        public int Number { get; set; }

        [ReadOnly(true)]
        public Band Band { get; set; }

        [Browsable(false)]
        public char ChannelPrefix { get; set; }

        [ReadOnly(true)]
        public int Frequency { get; set; }

        public Channel()
        {
            Number = 1;
            Band = Band.None;
            ChannelPrefix = char.MinValue;
            Frequency = 0;
        }

        public Channel(int id, int number, Band band)
            :this(id, char.MinValue, number, band)
        {
        }

        public Channel(int id, char prefix, int number, Band band)
        {
            byte[] bytes = new byte[16];
            BitConverter.GetBytes(id).CopyTo(bytes, 0);
            ID = new Guid(bytes);
            Number = number;
            Band = band;
            ChannelPrefix = prefix;
            Frequency = FrequencyLookup(Band, ChannelPrefix, Number);
        }

        public static IEnumerable<Band> GetBands()
        {
            List<Band> bands = new List<Band>();

            foreach (Band band in Enum.GetValues(typeof(Band)))
            {
                switch (band)
                {
                    case Band.None:
                        break;
                    default:
                        bands.Add(band);
                        break;
                }
            }

            return bands.Distinct();
        }

        public string GetBandChannelText()
        {
            if (Band == Band.None) return "--";

            if (ChannelPrefix != char.MinValue)
            {
                string outa = Band.GetCharacter() + ChannelPrefix + Number.ToString();
                return outa;
            }

            return Band.GetCharacter() + Number.ToString();
        }

        public string GetFrequencyText()
        {
            return Frequency + "MHz";
        }

        public override string ToString()
        {
            if (Band == Band.None)
            {
                return "No Channel";
            }

            return ToStringShort() + " (" + GetFrequencyText() + ")";
        }

        public string ToStringShort()
        {
            if (Band == Band.None)
            {
                return "None";
            }

            if (ChannelPrefix != char.MinValue)
            {
                return Band.ToString() + " " + ChannelPrefix + Number.ToString();
            }

            return Band.ToString() + " " + Number.ToString();
        }

        public string GetSpokenBandLetter()
        {
            if (ChannelPrefix != char.MinValue)
            {
                return ChannelPrefix.ToString();
            }

            return Band.GetCharacter();
        }

        public bool InterferesWith(Channel channel)
        {
            if (channel == null)
                return false;

            const int range = 15;
            return Math.Abs(Frequency - channel.Frequency) < range;
        }

        public bool InterferesWith(IEnumerable<Channel> channels)
        {
            foreach (Channel channel in channels)
            {
                if (this.InterferesWith(channel))
                {
                    return true;
                }
            }
            return false;
        }

        public static Channel None = new Channel(0, 0, Band.None);

        public static Channel[] Fatshark = new Channel[] {
            new Channel(1, 1, Band.Fatshark),
            new Channel(2, 2, Band.Fatshark),
            new Channel(3, 3, Band.Fatshark),
            new Channel(4, 4, Band.Fatshark),
            new Channel(5, 5, Band.Fatshark),
            new Channel(6, 6, Band.Fatshark),
            new Channel(7, 7, Band.Fatshark),
            new Channel(8, 8, Band.Fatshark)
        };

        public static Channel[] RaceBand = new Channel[] {
            new Channel(9, 1, Band.Raceband),
            new Channel(10, 2, Band.Raceband),
            new Channel(11, 3, Band.Raceband),
            new Channel(12, 4, Band.Raceband),
            new Channel(13, 5, Band.Raceband),
            new Channel(14, 6, Band.Raceband),
            new Channel(15, 7, Band.Raceband),
            new Channel(16, 8, Band.Raceband)
        };

        public static Channel[] BoscamA = new Channel[] {
            new Channel(17, 1, Band.A),
            new Channel(18, 2, Band.A),
            new Channel(19, 3, Band.A),
            new Channel(20, 4, Band.A),
            new Channel(21, 5, Band.A),
            new Channel(22, 6, Band.A),
            new Channel(23, 7, Band.A),
            new Channel(24, 8, Band.A)
        };

        public static Channel[] BoscamB = new Channel[] {
            new Channel(25, 1, Band.B),
            new Channel(26, 2, Band.B),
            new Channel(27, 3, Band.B),
            new Channel(28, 4, Band.B),
            new Channel(29, 5, Band.B),
            new Channel(30, 6, Band.B),
            new Channel(31, 7, Band.B),
            new Channel(32, 8, Band.B)
        };

        public static Channel[] DJIFPVHD = new Channel[] {
            new Channel(33, 1, Band.DJIFPVHD),
            new Channel(34, 2, Band.DJIFPVHD),
            new Channel(35, 3, Band.DJIFPVHD),
            new Channel(36, 4, Band.DJIFPVHD),
            new Channel(37, 5, Band.DJIFPVHD),
            new Channel(38, 6, Band.DJIFPVHD),
            new Channel(39, 7, Band.DJIFPVHD),
            new Channel(40, 8, Band.DJIFPVHD)
        };

        public static Channel[] E = new Channel[] {
            new Channel(41, 1, Band.E),
            new Channel(42, 2, Band.E),
            new Channel(43, 3, Band.E),
            new Channel(44, 4, Band.E),
            new Channel(45, 5, Band.E),
            new Channel(46, 6, Band.E),
            new Channel(47, 7, Band.E),
            new Channel(48, 8, Band.E)
        };

        public static Channel[] HDZero = new Channel[] {
            new Channel(49, 'R', 1, Band.HDZero),
            new Channel(50, 'R', 2, Band.HDZero),
            new Channel(51, 'R', 3, Band.HDZero),
            new Channel(52, 'R', 4, Band.HDZero),
            new Channel(53, 'R', 5, Band.HDZero),
            new Channel(54, 'R', 6, Band.HDZero),
            new Channel(55, 'R', 7, Band.HDZero),
            new Channel(56, 'R', 8, Band.HDZero),
            new Channel(57, 'F', 2, Band.HDZero),
            new Channel(58, 'F', 4, Band.HDZero)
        };

        public static Channel[] HDZeroIMD6C = new Channel[] {

            HDZero[0],
            HDZero[1],

            HDZero[8],
            HDZero[9],

            HDZero[6],
            HDZero[7],
        };

        public static Channel[] LowBand = new Channel[] {
            new Channel(59, 1, Band.LowBand),
            new Channel(60, 2, Band.LowBand),
            new Channel(61, 3, Band.LowBand),
            new Channel(62, 4, Band.LowBand),
            new Channel(63, 5, Band.LowBand),
            new Channel(64, 6, Band.LowBand),
            new Channel(65, 7, Band.LowBand),
            new Channel(66, 8, Band.LowBand)
        };

        public static Channel[] Diatone = new Channel[] {
            new Channel(67, 1, Band.Diatone),
            new Channel(68, 2, Band.Diatone),
            new Channel(69, 3, Band.Diatone),
            new Channel(70, 4, Band.Diatone),
            new Channel(71, 5, Band.Diatone),
            new Channel(72, 6, Band.Diatone),
            new Channel(73, 7, Band.Diatone),
            new Channel(74, 8, Band.Diatone)
        };


        public static Channel[] DJIO3 = new Channel[] {
            new Channel(75, 1, Band.DJIO3),
            new Channel(76, 2, Band.DJIO3),
            new Channel(77, 3, Band.DJIO3),
            new Channel(78, 4, Band.DJIO3),
            new Channel(79, 5, Band.DJIO3),
            new Channel(80, 6, Band.DJIO3),
            new Channel(81, 7, Band.DJIO3),
        };

        public static Channel[] IMD6C = new Channel[] {
            RaceBand[0],
            RaceBand[1],

            Fatshark[1],
            Fatshark[3],

            RaceBand[6],
            RaceBand[7],
        };

        private static int FrequencyLookup(Band band, char prefix, int channel)
        {
            switch (band)
            {
                case Band.Fatshark:
                    return 5740 + ((channel - 1) * 20);

                case Band.Raceband:
                    return 5658 + ((channel - 1) * 37);

                case Band.A:
                    return 5865 + ((channel - 1) * -20);

                case Band.B:
                    return 5733 + ((channel - 1) * 19);

                case Band.DJIFPVHD:
                    switch (channel)
                    {
                        case 1: return 5660;
                        case 2: return 5695;
                        case 3: return 5735;
                        case 4: return 5770;
                        case 5: return 5805;
                        case 8: return 5839;
                        case 6: return 5878;
                        case 7: return 5914;
                    }
                    break;
                case Band.DJIO3:
                    switch (channel)
                    {
                        case 1: return 5669;
                        case 2: return 5705;
                        case 3: return 5768;
                        case 4: return 5804;
                        case 5: return 5839;
                        case 6: return 5876;
                        case 7: return 5912;
                    }
                    break;

                case Band.E:
                    switch (channel)
                    {
                        case 1: return 5705;
                        case 2: return 5685;
                        case 3: return 5665;
                        case 4: return 5645;
                        case 5: return 5885;
                        case 6: return 5905;
                        case 7: return 5925;
                        case 8: return 5945;
                    }
                    break;

                case Band.HDZero:
                    switch (prefix)
                    {
                        case 'R':
                            return FrequencyLookup(Band.Raceband, prefix, channel);
                        case 'F': 
                            return FrequencyLookup(Band.Fatshark, prefix, channel);
                    }
                    break;

                case Band.LowBand:
                    return 5333 + ((channel - 1) * 40);
                case Band.Diatone:
                    return 5362 + ((channel - 1) * 37);


            }
            return 0;
        }

        private static Channel[] allChannels;

        [System.ComponentModel.Browsable(false)]
        public static Channel[] AllChannels
        {
            get
            {
                if (allChannels == null)
                {
                    allChannels = AllChannelsUnmodified;
                }
                return allChannels;
            }
        }

        public static void LoadCustom(Profile profile)
        {

        }


        public static Channel[] AllChannelsUnmodified
        {
            get
            {
                return Fatshark.Union(RaceBand).Union(BoscamA).Union(BoscamB).Union(DJIFPVHD).Union(E).Union(HDZero).Union(LowBand).Union(Diatone).Union(DJIO3).ToArray();
            }
        }

        public static Channel GetChannel(Band band, int number, char prefix)
        {
            return AllChannels.FirstOrDefault(c => c.Band == band && c.Number == number && c.ChannelPrefix == prefix);
        }

        public override bool Equals(object obj)
        {
            if (obj is Channel)
            {
                Channel b = (Channel)obj;
                if (b.Band == this.Band && b.Number == this.Number && b.Frequency == this.Frequency && this.ChannelPrefix == b.ChannelPrefix)
                    return true;
                else
                    return false;
            }


            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return Number.GetHashCode() + Band.GetHashCode() + Frequency.GetHashCode() + ChannelPrefix.GetHashCode();
        }

        public static bool operator ==(Channel a, Channel b)
        {
            if (ReferenceEquals(a, null))
                return ReferenceEquals(a, b);

            return a.Equals(b);
        }

        public static bool operator !=(Channel a, Channel b)
        {
            if (ReferenceEquals(a, null))
                return !ReferenceEquals(a, b);

            return !a.Equals(b);
        }


        public IEnumerable<Channel> GetInterferringChannels(IEnumerable<Channel> pool)
        {
            return pool.Where(c => InterferesWith(c));
        }

        private const string filename = "Channels.xml";
        public static Channel[] Read(Profile profile)
        {
            try
            {
                Channel[] s = null;
                try
                {
                    s = Tools.IOTools.Read<SimpleChannel>(profile, filename).Select(c => c.GetChannel()).Where(c => c != null).ToArray();
                }
                catch
                {
                }

                if (s == null)
                {
                    s = new Channel[0];
                }

                if (!s.Any())
                {
                    s = IMD6C;
                }

                for (int i = 0; i < s.Length; i++)
                {
                    s[i] = AllChannels.FirstOrDefault(ch => ch.Equals(s[i]));
                }

                Write(profile, s);

                return s;
            }
            catch
            {
                return new Channel[0];
            }
        }

        public static void Write(Profile profile, Channel[] s)
        {
            Tools.IOTools.Write(profile, filename, s.Select(c => new SimpleChannel(c)).ToArray());
        }

        public class SimpleChannel
        {
            public int Number { get; set; }
            public Band Band { get; set; }

            public char Prefix { get; set; }

            public SimpleChannel() { }

            public SimpleChannel(Channel channel)
            {
                Band = channel.Band;
                Number = channel.Number;
                Prefix = channel.ChannelPrefix;
            }

            public Channel GetChannel()
            {
                Channel channel = Channel.GetChannel(Band, Number, Prefix);
                return channel;
            }
        }
    }
}
