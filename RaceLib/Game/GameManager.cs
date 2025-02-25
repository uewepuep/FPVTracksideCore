using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
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

        public int GetTeam(Channel channel)
        {
            if (GameType == null || GameType.PilotsPerTeam == 0)
                return -1;

            int index = EventManager.Channels.GetChannelGroupIndex(channel);

            index = index / GameType.PilotsPerTeam;

            return index;
        }

        public Color GetTeamColor(Channel channel)
        {
            int team = GetTeam(channel);

            Channel[] group = EventManager.Channels.GetChannelGroup(team);

            if (group != null)
            {
                return EventManager.GetChannelColor(group.FirstOrDefault());
            }

            return EventManager.GetChannelColor(channel);
        }
        public void AddGamePoint(Pilot pilot, Channel channel, DateTime time)
        {
            Race race = CurrentRace;
            if (race != null)
            {
                GamePoint gp = race.AddGamePoint(pilot, channel, time);
                OnGamePointChanged?.Invoke(gp);
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
            int team = GetTeam(channel);

            Channel[] channels = EventManager.Channels.Where(c => team == GetTeam(c)).ToArray();

            return GetGamePoints(race, gp => channels.Contains(gp.Channel) && gp.Time <= time);
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
    }
}
