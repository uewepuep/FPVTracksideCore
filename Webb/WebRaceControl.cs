using RaceLib;
using Sound;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Webb
{
    public class WebRaceControl
    {
        public EventManager EventManager { get; private set; }
        public SoundManager SoundManager { get; private set; }
        public IRaceControl RaceControl { get; private set; }

        public RaceManager RaceManager { get { return EventManager.RaceManager; } }

        public WebRaceControl(EventManager eventManager, SoundManager soundManager, IRaceControl raceControl)
        {
            EventManager = eventManager;
            SoundManager = soundManager;
            RaceControl = raceControl;
        }

        public string GetHTML()
        {
            string output = "<h1>Race Controls</h2>";
            output += "<form method=\"POST\">";
            output += RaceControls();

            Race race = RaceManager.CurrentRace;
            if (race != null)
            {
                output += RaceInfo(race);
                output += LapControls(race);

                output += WebbRounds.RaceBox(EventManager, race);
            }


            output += "</form>";

            return output;
        }

        private string RaceInfo(Race race)
        {
            string output = "<h2>" + race.ToString() + "</h2>";

            output += "<table>";
            foreach (var pc in race.PilotChannelsSafe.OrderBy(a => a.Channel.Frequency))
            {
                output += "<tr><td>" + pc.Channel.ToString() + "</td><td>" + pc.Pilot.Name + "</td></tr>";
            }
            output += "</table>";

            return output;
        }

        private string LapControls(Race race)
        {
            string output = "";

            Lap[] laps = race.GetLaps();

             output += "<table>";
            foreach (Lap lap in laps.Where(l => l.Detection.Valid).OrderBy(l => l.Detection.Time))
            {
                output += "<tr>";
                output += "<td>" + lap.Pilot.Name + "</td>";
                output += "<td>" + lap.Detection.Time + "</td>";
                output += "<td> Lap " + lap.Number + "</td>";
                output += "<td> " + lap.Length.TotalSeconds.ToString("0.00") + "</td>";
                output += "<td><button name=\"disqualify_" + lap.ID +"\">Disqualify</button></td>";

                output += "</tr>";
            }
            output += "</table>";

            return output;
        }

        private string RaceControls()
        {
            string output = "";
            bool start = RaceManager.CanRunRace && !RaceManager.PreRaceStartDelay;
            bool stop = RaceManager.RaceRunning || RaceManager.PreRaceStartDelay;
            bool clear = (RaceManager.RaceFinished || RaceManager.CanRunRace) && !RaceManager.PreRaceStartDelay;
            bool reset = RaceManager.RaceFinished;

            Race nextRace = RaceManager.GetNextRace(true);
            bool next = !RaceManager.RaceRunning && !RaceManager.PreRaceStartDelay && nextRace != null && nextRace != RaceManager.CurrentRace;

            if (start) output += "<button name=\"start\">Start Race</button>";
            if (stop) output += "<button name=\"stop\">Stop Race</button>";
            if (clear) output += "<button name=\"clear\">Clear Race</button>";
            if (reset) output += "<button name=\"reset\">Reset Race</button>";
            if (next) output += "<button name=\"next\">Next Race</button>";

            return output;
        }

        public void HandleInput(NameValueCollection nameValueCollection)
        {
            foreach (string key in nameValueCollection.AllKeys)
            {
                if (key == "start")
                {
                    RaceControl.StartRace();
                }

                if (key == "stop")
                    RaceManager.EndRace();

                if (key == "clear")
                    RaceManager.ClearRace();

                if (key == "reset")
                    RaceManager.ResetRace();

                if (key == "next")
                    RaceManager.NextRace(true);

                Race race = RaceManager.CurrentRace;
                if (key.StartsWith("disqualify") && race != null)
                {
                    string[] split = key.Split('_');
                    if (split.Length == 2)
                    {
                        Guid lapId;
                        if (Guid.TryParse(split[1], out lapId))
                        {
                            Lap lap = race.GetLaps().FirstOrDefault(l => l.ID == lapId);
                            if (lap != null)
                            {
                                EventManager.RaceManager.SetLapValidity(lap, false);
                            }
                        }
                    }
                }
            }
        }
    }
}
