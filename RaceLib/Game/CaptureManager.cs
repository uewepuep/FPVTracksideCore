using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib.Game
{
    public class CaptureManager
    {
        private List<Capturing> timingSystemCaptures;
        private List<Captured> currentlyCaptured;

        private GameManager gameManager;

        public event Action<Captured> OnCapture;

        public CaptureManager(GameManager gameManager)
        {
            this.gameManager = gameManager;
            timingSystemCaptures = new List<Capturing>();
            currentlyCaptured = new List<Captured>();
        }

        public void AddDetection(Detection d)
        {
            Team t = gameManager.GetTeam(d.Channel);

            Capturing teamcapture = GetCreateTeamCapture(t, d.TimingSystemIndex);
            teamcapture.AddDetection(d);

            if (teamcapture.Detections > gameManager.GameType.DetectionsForCapture)
            {
                Captured tt = new Captured(d.Pilot, d.Channel, t, d.Time, d.TimingSystemIndex);

                lock (currentlyCaptured)
                {
                    currentlyCaptured.RemoveAll(ta => ta.TimingSystemIndex == d.TimingSystemIndex);
                    currentlyCaptured.Add(tt);
                }

                ClearTimingSystem(d.TimingSystemIndex);

                OnCapture?.Invoke(tt);
            }
        }

        private Capturing GetCreateTeamCapture(Team team, int timingSystemIndex)
        {
            lock (timingSystemCaptures)
            {
                Capturing teamCapture = timingSystemCaptures.FirstOrDefault(tc => tc.TimingSystemIndex == timingSystemIndex && team == tc.Team);
                if (teamCapture == null)
                {
                    teamCapture = new Capturing(team, timingSystemIndex);
                    timingSystemCaptures.Add(teamCapture);
                }

                return teamCapture;
            }
        }

        public Captured GetCapturer(int timingSystemIndex)
        {
            lock (currentlyCaptured)
            {
                Captured c = currentlyCaptured.FirstOrDefault(t => t.TimingSystemIndex == timingSystemIndex);
                if (c != null)
                {
                    return c;
                }
            }

            return null;
        }

        public void ClearTimingSystem(int timingSystemIndex)
        {
            lock (timingSystemCaptures)
            {
                timingSystemCaptures.RemoveAll(t => t.TimingSystemIndex == timingSystemIndex);
            }
        }

        public void Clear()
        {
            lock (timingSystemCaptures)
            {
                timingSystemCaptures.Clear();
            }


            lock (currentlyCaptured)
            {
                currentlyCaptured.Clear();
            }
        }

    }

    public class Captured
    {
        public Team Team { get; private set; }
        public DateTime CaptureTime { get; private set; }
        public int TimingSystemIndex { get; private set; }

        public Pilot Pilot { get; private set; }
        public Channel Channel { get; private set; }

        public Captured(Pilot pilot, Channel channel, Team team, DateTime captureTime, int timingSystemIndex)
        {
            Pilot = pilot;
            Channel = channel;
            Team = team;
            CaptureTime = captureTime;
            TimingSystemIndex = timingSystemIndex;
        }
    }

    public class Capturing
    {
        public Team Team { get; private set; }

        public int Detections { get; private set; }

        public int TimingSystemIndex { get; private set; }

        public Capturing(Team team, int timingSystemIndex)
        {
            Team = team;
            TimingSystemIndex = timingSystemIndex;
        }

        public void AddDetection(Detection d)
        {
            Detections++;
        }

        public void Clear()
        {
            Detections = 0;
        }
    }
}
