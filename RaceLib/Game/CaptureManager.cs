using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib.Game
{
    public class CaptureManager
    {
        private List<TeamCapture> timingSystemCaptures;

        private GameManager gameManager;

        public CaptureManager(GameManager gameManager)
        {
            this.gameManager = gameManager;
            timingSystemCaptures = new List<TeamCapture>();
        }

        public void AddDetection(Detection d)
        {
            Team t = gameManager.GetTeam(d.Channel);

            TeamCapture teamcapture = GetCreateTeamCapture(t, d.TimingSystemIndex);
            teamcapture.AddDetection(d);
        }

        private TeamCapture GetCreateTeamCapture(Team team, int timingSystemIndex)
        {
            lock (timingSystemCaptures)
            {
                TeamCapture teamCapture = timingSystemCaptures.FirstOrDefault(tc => tc.TimingSystemIndex == timingSystemIndex && team == tc.Team);
                if (teamCapture == null)
                {
                    teamCapture = new TeamCapture(team, timingSystemIndex);
                }

                return teamCapture;
            }
        }

        public void Clear()
        {
            lock (timingSystemCaptures)
            {
                timingSystemCaptures.Clear();
            }
        }
    }

    public class TeamCapture
    {
        public Team Team { get; private set; }

        public int Detections { get; private set; }

        public int TimingSystemIndex { get; private set; }

        public TeamCapture(Team team, int timingSystemIndex)
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
