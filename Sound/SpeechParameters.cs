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
            speed
        }

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

            if (type == Types.position && value is int)
            {
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

            Add(type, time);
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
            parameters.Add(Types.pilots, string.Join(", ", pilotNames.Randomise().Take(4)));
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
