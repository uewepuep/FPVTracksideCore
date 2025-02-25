using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Tools;

namespace RaceLib.Game
{
    public class GameManager
    {
        public GameType GameType { get; private set; }

        public GameType[] GameTypes { get; private set; }

        public EventManager EventManager { get; private set; }
        public event Action<GamePoint> OnGamePointChanged;
        public event Action<Pilot[], Team, int> OnGamePointsRemaining;
        public event Action<Pilot[], Team> OnGamePointsReached;

        public Race CurrentRace
        {
            get
            {
                return EventManager.RaceManager.CurrentRace;
            }
        }

        public GameManager(EventManager eventManager)
        {
            EventManager = eventManager;
            eventManager.RaceManager.OnRaceChanged += RaceManager_OnRaceChanged;
        }

        private void RaceManager_OnRaceChanged(Race race)
        {
            if (race == null)
                return;

            Round round = race.Round;
            if (round == null)
                return;

            if (string.IsNullOrEmpty(round.GameTypeName))
                return;

            GameType gt = GetByName(round.GameTypeName);
            SetGameType(gt);
        }

        public void SetGameType(GameType gameType)
        {
            GameType = gameType;
        }

        public Team GetTeam(Channel channel)
        {
            if (GameType == null || GameType.PilotsPerTeam == 0)
                return Team.None;

            int index = EventManager.Channels.GetChannelGroupIndex(channel);

            index = index / GameType.PilotsPerTeam;

            return (Team)index;
        }

        public IEnumerable<Team> GetTeams()
        {
            int index = 0;
            foreach (var group in EventManager.Channels.GetChannelGroups())
            {
                yield return (Team)index;
                index++;
            }
        }

        public Color GetTeamColor(Channel channel)
        {
            Team team = GetTeam(channel);

            Channel[] group = EventManager.Channels.GetChannelGroup((int)team);

            if (group != null)
            {
                return EventManager.GetChannelColor(group.FirstOrDefault());
            }

            return EventManager.GetChannelColor(channel);
        }

        public void AddGamePoint(Pilot pilot, Channel channel, DateTime time)
        {
            Race race = CurrentRace;
            if (race == null)
                return;

            GamePoint gp = race.AddGamePoint(pilot, channel, time);
            OnGamePointChanged?.Invoke(gp);

            int totalPoints = GetCurrentGamePoints(channel);
            int remaining = Math.Max(0, GameType.TargetPoints - totalPoints);

            if (GameType != null && GameType.PointsRemainingWarning != null &&
                GameType.PointsRemainingWarning.Contains(remaining) && OnGamePointsRemaining != null)
            {
                Team t = GetTeam(channel);

                Pilot[] pilots = GetPilots(t).ToArray();

                OnGamePointsRemaining?.Invoke(pilots, t, remaining);
            }

            if (remaining == 0 && GetTeams().Count(t => HasWon(t)) == 1)
            {
                Team t = GetTeam(channel);

                Pilot[] pilots = GetPilots(t).ToArray();
                OnGamePointsReached(pilots, t);
            }
        }

        public void RemoveGamePoint(Channel channel)
        {
            Race race = CurrentRace;
            if (race != null)
            {
                race.RemoveGamePoint(channel);
                OnGamePointChanged?.Invoke(null);
            }
        }

        public int GetGamePoints(Race race, Func<GamePoint, bool> predicate)
        {
            if (race != null)
            {
                GamePoint[] gps = race.GetValidGamePoints(predicate);
                return gps.Length;
            }

            return 0;
        }

        public bool HasWon(Team team)
        {
            if (GameType == null)
                return false;

            return GetGamePoints(team) > GameType.TargetPoints;
        }

        public int GetCurrentGamePoints(Channel channel)
        {
            return GetGamePoints(CurrentRace, channel);
        }

        public int GetGamePoints(Race race, Channel channel)
        {
            return GetGamePoints(race, channel, DateTime.MaxValue);
        }

        public int GetGamePoints(Race race, Channel channel, DateTime time)
        {
            Team team = GetTeam(channel);

            Channel[] channels = EventManager.Channels.Where(c => team == GetTeam(c)).ToArray();

            return GetGamePoints(race, gp => channels.Contains(gp.Channel) && gp.Time <= time);
        }

        public int GetGamePoints(Team team)
        {
            Channel[] channels = EventManager.Channels.Where(c => team == GetTeam(c)).ToArray();

            return GetGamePoints(CurrentRace, gp => channels.Contains(gp.Channel));
        }

        public IEnumerable<Pilot> GetPilots(Team t)
        {
            IEnumerable<Channel> channels = EventManager.Channels.Where(c => t == GetTeam(c));
            foreach (Channel channel in channels)
            {
                Pilot pilot = EventManager.RaceManager.GetPilot(channel);
                if (pilot != null)
                {
                    yield return pilot;
                }
            }
        }

        public int GetTargetGamePoints(Pilot pilot)
        {
            if (GameType == null)
            {
                return 0;
            }

            return GameType.TargetPoints;
        }

        public void OnDetection(Detection d)
        {
            if (GameType == null)
                return;

            switch (GameType.TimingSystemPointMode)
            {
                case TimingSystemPointMode.PointForDetection:
                    AddGamePoint(d.Pilot, d.Channel, d.Time);
                    break;
            }
        }

        public void LoadGameTypes(Profile profile)
        {
            GameTypes = GameType.Read(profile);
        }

        public bool SetByName(string name)
        {
            GameType gt = GetByName(name);

            SetGameType(gt);

            return gt != null;
        }

        public GameType GetByName(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                return GameTypes.FirstOrDefault(gt => gt.Name == name);
            }
            return null;
        }

        public void ClearRace(Race race)
        {
            race.GamePoints.Clear();
        }
    }
}
