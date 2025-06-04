using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace RaceLib
{
    public class Pilot : BaseObject
    {
        public delegate void OnPilotEvent(Pilot pilot);

        [Category("Name")]
        [DisplayName("Pilot Name")]
        public string Name { get; set; }

        private string phonetic;
        [Category("Name")]
        [DisplayName("Phonetic (for Text To Speech)")]
        public string Phonetic
        {
            get
            {
                if (phonetic == null && Name != null)
                {
                    AutoPhonetic(Name);
                }
                return phonetic;
            }
            set
            {
                phonetic = value;
            }
        }

        [Category("Name")]
        public string FirstName { get; set; }
        
        [Category("Name")]
        public string LastName { get; set; }

        [Category("Name")]
        public string SillyName { get; set; }

        [Category("Name")]
        public string DiscordID { get; set; }

        [Category("Profile")]
        public string Aircraft { get; set; }
        
        [Category("Profile")]
        public string CatchPhrase { get; set; }
        
        [Category("Profile")]
        public string BestResult { get; set; }
        
        [Category("Per Pilot Timing Settings (LapRF)")]
        public int TimingSensitivityPercent { get; set; }

        [Category("Advanced")]
        public bool PracticePilot { get; set; }

        [Category("Advanced")]
        public string PhotoPath { get; set; }

        [Category("Advanced")]

        public int MultiGP_ID
        {
            get
            {
                return ExternalID;
            }
            set
            {
                ExternalID = value;
            }
        }

        public static Pilot CreateFromName(string name)
        {
            Pilot pilot = new Pilot() { Name = name };
            pilot.AutoPhonetic(name);
            return pilot;
        }

        private void AutoPhonetic(string name)
        {
            name = System.Text.RegularExpressions.Regex.Replace(name, "[^a-zA-Z0-9 ]", " ", System.Text.RegularExpressions.RegexOptions.Compiled);
            phonetic = name.Trim();
        }

        public Pilot()
        {
            PracticePilot = false;
            TimingSensitivityPercent = 100;
        }

        public bool HasFinished(EventManager eventManager)
        {
            if (eventManager == null)
                return false;

            Race currentRace = eventManager.RaceManager.CurrentRace;
            return HasFinished(eventManager, currentRace);
        }
        
        public bool HasFinished(EventManager eventManager, Race currentRace)
        { 
            if (currentRace == null)
                return false;

            if (!currentRace.HasPilot(this))
                return false;

            if (currentRace.Running)
            {
                if (eventManager.RaceManager.TimesUp && currentRace.Type == EventTypes.AggregateLaps)
                {
                    IEnumerable<Lap> lastLap = currentRace.GetValidLaps(this, false).Where(l => l.EndRaceTime > eventManager.Event.RaceLength);
                    return lastLap.Any();
                }

                if (eventManager.RaceManager.TimesUp && currentRace.Type == EventTypes.TimeTrial)
                {
                    DateTime timeEnd = currentRace.Start + eventManager.Event.RaceLength;
                    if (currentRace.GetValidLaps(this, true).Any(l => l.Detection.Time > timeEnd))
                    {
                        return true;
                    }
                }

                if (currentRace.Type != EventTypes.Race)
                    return false;

                if (currentRace.GetValidLapsCount(this, false) >= currentRace.TargetLaps)
                {
                    return true;
                }
                return false;
            }

            if (currentRace.Ended)
            {
                return true;
            }
            return false;
        }
        
        public override string ToString()
        {
            return Name;
        }

        public int CountChannelChanges(IEnumerable<Race> races)
        {
            int channelChanges = 0;
            Channel prevChannel = Channel.None;

            foreach (Race r in races.Where(r => r.Pilots.Contains(this)))
            {
                Channel ca = r.GetChannel(this);
                if (ca != prevChannel)
                {
                    prevChannel = ca;
                    channelChanges++;
                }
            }

            return channelChanges;
        }

        public Race GetRaceInRound(IEnumerable<Race> races, Round round)
        {
            return races.FirstOrDefault(r => r.Pilots.Contains(this) && r.Round == round);
        }

        public Channel GetChannelInRound(IEnumerable<Race> races, Round round)
        {
            Race r = GetRaceInRound(races, round);
            if (r != null)
            {
                return r.GetChannel(this);
            }
            return Channel.None;
        }
    }

    
}
