using Composition;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Sound
{

    public class SpeechParameters
    {
        public enum Types
        {
            pilot,
            pilots,
            pilotchannels,
            lapnumber,
            count,
            time,
            position,
            band,
            channel,
            round,
            race,
            bracket,
            type,
            laptime,
            lapstime,
            racetime,
            s,
            speedunits,
            speed,
            points,

            pos_raw,
            time_raw,
            laptime_raw,
            lapstime_raw,
            racetime_raw,
            time_min_raw,     time_sec_raw,     time_ms_raw,
            laptime_min_raw,  laptime_sec_raw,  laptime_ms_raw,
            lapstime_min_raw, lapstime_sec_raw, lapstime_ms_raw,
            racetime_min_raw, racetime_sec_raw, racetime_ms_raw,
        }

        public static int DecimalPlaces { get; set; } = 2;

        Dictionary<Types, string> parameters;

        public int Priority { get; set; }
        public double SecondsExpiry { get; set; }

        public bool Forced { get; set; }

        public SpeechParameters()
        {
            Priority = 1;
            SecondsExpiry = 5;
            parameters = new Dictionary<Types, string>();
        }

        public SpeechParameters(Types type, object value)
            : this()
        {
            Add(type, value);
        }

        public void AddTime(Types type, TimeSpan timeSpan)
        {
            double value;
            string unit;

            if (timeSpan.Hours > 1)
            {
                value = Math.Round(timeSpan.TotalHours, 1);
                unit = " hour";
            }
            else if (timeSpan.Minutes > 1)
            {
                value = Math.Round(timeSpan.TotalMinutes, 1);
                unit = " minute";
            }
            else
            {
                value = Math.Round(timeSpan.TotalSeconds, 0);
                unit = " second";
            }

            if (value > 1)
            {
                unit += "s";
            }

            PopulateRawTimeComponents(type, timeSpan);

            if ((int)value == value)
            {
                Add(type, value.ToString("0") + unit);
            }
            else
            {
                Add(type, value.ToString("0.0") + unit);
            }
        }

        public void Add(Types type, object value)
        {
            if (value == null)
                return;

            if (value is TimeSpan)
            {
                TimeSpan timeSpan = (TimeSpan)value;
                AddTime(type, timeSpan);
                return;
            }

            if (type == Types.pilot && value is Pilot pilot)
            {
                value = pilot.Phonetic;
            }


            if (type == Types.position && value is int)
            {
                parameters[Types.pos_raw] = ((int)value).ToString();
                value = PositionToString((int)value);
            }

            if (parameters.ContainsKey(type))
            {
                parameters[type] = value.ToString();
            }
            else
            {
                parameters.Add(type, value.ToString());
            }
        }

        public void AddRaceTime(Types type, TimeSpan timeSpan)
        {
            string time;

            double two = Math.Round(timeSpan.TotalSeconds, 2);
            double one = Math.Round(timeSpan.TotalSeconds, 1);

            // If its one decimal place..
            if (one == two)
            {
                time = one.ToString("0.0");
            }
            else
            {
                time = two.ToString("0.00");
            }

            PopulateRawTimeComponents(type, timeSpan);

            Add(type, time);
        }

        private void PopulateRawTimeComponents(Types baseType, TimeSpan timeSpan)
        {
            Types raw, minRaw, secRaw, msRaw;
            switch (baseType)
            {
                case Types.time:
                    raw = Types.time_raw; minRaw = Types.time_min_raw;
                    secRaw = Types.time_sec_raw; msRaw = Types.time_ms_raw; break;
                case Types.laptime:
                    raw = Types.laptime_raw; minRaw = Types.laptime_min_raw;
                    secRaw = Types.laptime_sec_raw; msRaw = Types.laptime_ms_raw; break;
                case Types.lapstime:
                    raw = Types.lapstime_raw; minRaw = Types.lapstime_min_raw;
                    secRaw = Types.lapstime_sec_raw; msRaw = Types.lapstime_ms_raw; break;
                case Types.racetime:
                    raw = Types.racetime_raw; minRaw = Types.racetime_min_raw;
                    secRaw = Types.racetime_sec_raw; msRaw = Types.racetime_ms_raw; break;
                default:
                    return;
            }

            int decimals = Math.Max(0, DecimalPlaces);
            long divisor = (long)Math.Pow(10, decimals);
            long totalUnits = (long)Math.Round(timeSpan.TotalSeconds * divisor);
            long totalSec = decimals > 0 ? totalUnits / divisor : totalUnits;
            long fracUnits = decimals > 0 ? totalUnits - totalSec * divisor : 0;
            long min = totalSec / 60;
            long sec = totalSec % 60;

            parameters[raw]    = totalSec.ToString();
            parameters[minRaw] = min.ToString();
            parameters[secRaw] = sec.ToString();
            parameters[msRaw]  = decimals > 0 ? fracUnits.ToString().PadLeft(decimals, '0') : "";
        }

        public void AddSubParameters(Types type, Sound subSound, IEnumerable<SpeechParameters> subParameters)
        {
            string subs = string.Join("; ", subParameters.Select(s => CreateTextToSpeech(subSound.TextToSpeech, s)));
            Add(type, subs);
        }

        public string Apply(string input)
        {
            string output = input;

            foreach (var kvp in parameters)
            {
                string name = "{" + kvp.Key + "}";
                string value = kvp.Value;

                output = output.Replace(name, value);
            }

            return output;
        }

        public static string CreateTextToSpeech(string rawText, SpeechParameters parameters)
        {
            if (parameters == null)
                return rawText;

            return parameters.Apply(rawText);
        }

        public static string PositionToString(int position)
        {
            switch (position)
            {
                case 1: return "1st";
                case 2: return "2nd";
                case 3: return "3rd";
                default:
                    return position + "th";
            }
        }

        public static SpeechParameters Random(SoundManager soundManager)
        {
            Random r = new Random();

            int count = r.Next(1, 5);
            TimeSpan time = TimeSpan.FromSeconds(r.Next(1000, 3000) / 100.0);
            int round = r.Next(1, 5);
            int race = r.Next(1, 5);
            string s = count > 1 ? "s" : "";
            Band band = (new Band[] { Band.DJIFPVHD, Band.Fatshark, Band.Raceband, Band.HDZero }).Random();
            int channel = r.Next(1, 8);
            int speed = r.Next(1, 120);

            string[] pilotNames = new string[]
            {
               "Alfa",
               "Bravo",
               "Charlie",
               "Delta",
               "Echo",
               "Foxtrot",
               "Golf",
               "Hotel",
               "India",
               "Juliett",
               "Kilo",
               "Lima",
               "Mike",
               "November",
               "Oscar",
               "Papa",
               "Quebec",
               "Romeo",
               "Sierra",
               "Tango",
               "Uniform",
               "Victor",
               "Whiskey",
               "X-ray",
               "Yankee",
               "Zulu",
            };

            SpeechParameters parameters = new SpeechParameters();
            parameters.Add(Types.count, count);
            parameters.Add(Types.time, time);
            parameters.AddRaceTime(Types.laptime, time);
            parameters.AddRaceTime(Types.lapstime, time);
            parameters.AddRaceTime(Types.racetime, time);
            parameters.Add(Types.pilot, pilotNames.Random());
            parameters.Add(Types.pilots, string.Join(Translator.ListSeparator, pilotNames.Randomise().Take(4)));
            parameters.Add(Types.position, channel);
            parameters.Add(Types.round, round);
            parameters.Add(Types.race, race);
            parameters.Add(Types.s, s);
            parameters.Add(Types.type, "Race");
            parameters.Add(Types.bracket, "A");
            parameters.Add(Types.band, band.GetCharacter());
            parameters.Add(Types.channel, channel);
            parameters.Add(Types.lapnumber, count);
            parameters.Add(Types.speed, speed);
            parameters.Add(Types.speedunits, soundManager.UnitToWords());

            int pilotcount = r.Next(2, 6);

            List<SpeechParameters> soundParameters = new List<SpeechParameters>();
            for (int i = 0; i < pilotcount; i++)
            {
                band = (new Band[] { Band.DJIFPVHD, Band.Fatshark, Band.Raceband, Band.HDZero }).Random();
                channel = r.Next(1, 8);

                SpeechParameters pilotChannelParameters = new SpeechParameters();
                pilotChannelParameters.Add(SpeechParameters.Types.pilot, pilotNames.Random());
                pilotChannelParameters.Add(Types.band, band.GetCharacter());
                pilotChannelParameters.Add(Types.channel, channel);
                soundParameters.Add(pilotChannelParameters);
            }
            parameters.AddSubParameters(Types.pilotchannels, soundManager.GetSound(SoundKey.AnnouncePilotChannel), soundParameters);

            return parameters;
        }
    }
}
