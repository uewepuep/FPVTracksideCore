using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timing;

namespace RaceLib
{
    public class Race : BaseObject
    {
        public delegate void OnRaceEvent(Race race);

        public List<Lap> Laps { get; set; }

        public List<Detection> Detections { get; set; }

        [Category("Times")]
        public DateTime Start { get; set; }
        [Category("Times")]
        public DateTime End { get; set; }

        [Category("Times")]
        public TimeSpan TotalPausedTime { get; set; }

        [System.ComponentModel.Browsable(false)]
        public List<PilotChannel> PilotChannels { get; set; }
       
        [System.ComponentModel.Browsable(false)]
        
        public PilotChannel[] PilotChannelsSafe 
        { 
            get 
            {
                lock (PilotChannels)
                {
                    return PilotChannels.ToArray();
                }
            }
        }
        public int PilotCount
        {
            get
            {
                lock (PilotChannels)
                {
                    return PilotChannels.Count;
                }
            }
        }

        [Category("Editable Details")]
        public int RaceNumber { get; set; }

        [System.ComponentModel.Browsable(false)]
        public Round Round { get; set; }

        [Category("Editable Details")]
        
        public int RoundNumber
        {
            get
            {
                if (Round == null)
                    return 1;

                return Round.RoundNumber;
            }
            set
            {
                Round newRound = Event.Rounds.FirstOrDefault(r => r.RoundNumber == value);
                if (newRound != null)
                {
                    Round = newRound;
                }
            }
        }

        [Category("Editable Details")]
        public int TargetLaps { get; set; }

        [Category("Editable Details")]
        
        public EventTypes Type
        {
            get
            {
                if (Round == null)
                    return Event.EventType;

                return Round.EventType;
            }
        }

        [Category("Editable Details")]
        public PrimaryTimingSystemLocation PrimaryTimingSystemLocation { get; set; }

        [Category("Editable Details")]
        public bool Valid { get; set; }

        [System.ComponentModel.Browsable(false)]
        public bool AutoAssignNumbers { get; set; }

        [System.ComponentModel.Browsable(false)]
        public Event Event { get; set; }

        [System.ComponentModel.Browsable(false)]
        
        public bool Running
        {
            get
            {
                return Started && End == default(DateTime);
            }
        }

        [System.ComponentModel.Browsable(false)]
        
        public Pilot[] Pilots
        {
            get
            {
                lock (PilotChannels)
                {
                    return PilotChannels.Select(p => p.Pilot).ToArray();
                }
            }
        }
        
        [System.ComponentModel.Browsable(false)]
        
        public Channel[] Channels
        {
            get
            {
                lock (PilotChannels)
                {
                    return PilotChannels.Select(p => p.Channel).ToArray();
                }
            }
        }

        
        [Category("Pilots")]
        public string PilotNames { get { return string.Join(", ", Pilots.Select(p => p.Name)); } }

        
        [Category("Pilots")]
        public string ChannelNames { get { return string.Join(", ", Channels.Select(c => c.GetBandChannelText())); } }

        [System.ComponentModel.Browsable(false)]
        
        public bool Started
        {
            get
            {
                return Start != default(DateTime);
            }
        }

        [System.ComponentModel.Browsable(false)]
        
        public bool Ended
        {
            get
            {
                return End != default(DateTime);
            }
        }

        [System.ComponentModel.Browsable(false)]
        public int LeadLap
        {
            get
            {
                lock (Laps)
                {
                    IEnumerable<Lap> laps = Laps.Where(l => l.Detection != null && l.Detection.Valid);
                    if (laps.Any())
                    {
                        return laps.Select(l => l.Number).Max();
                    }
                }
                return 0;
            }
        }

        
        [System.ComponentModel.Browsable(false)]
        public int RaceOrder
        {
            get
            {
                return Round.Order + RaceNumber;
            }
        }

        
        [System.ComponentModel.Browsable(false)]
        public string RoundRaceNumber
        {
            get
            {
                return RoundNumber + "-" + RaceNumber;
            }
        }

        [System.ComponentModel.Browsable(false)]
        public int TotalLaps
        {
            get
            {
                return Laps.Count;
            }
        }

        [System.ComponentModel.Browsable(false)]
        public TimeSpan Length
        {
            get
            {
                if (Ended)
                {
                    return End - Start;
                }
                else
                {
                    return DateTime.Now - Start;
                }
            }
        }

        [System.ComponentModel.Browsable(false)]
        public bool Uploaded { get; set; }

        public enum Brackets
        {
            None,
            Winners,
            Losers,
            
            A,B,C,D,E,F,G,
            H,I,J,K,L,M,N, 
            O,P,Q,R,S,T,U, 
            V,W,X,Y,Z
        }

        public Brackets Bracket { get; set; }

        
        public string RaceName
        {
            get
            {
                if (Bracket == Brackets.None)
                {
                    return RaceStringFormatter.Instance.GetEventTypeText(Type) + " " + RoundRaceNumber;
                }
                else
                {
                    return RaceStringFormatter.Instance.GetEventTypeText(Type) + " " + RoundRaceNumber + " (" + Bracket + ")";
                }
            }
        }

        public Race()
        {
            Bracket = Brackets.None;
            Valid = true;
            PrimaryTimingSystemLocation = PrimaryTimingSystemLocation.EndOfLap;
            Laps = new List<Lap>();
            PilotChannels = new List<PilotChannel>();
            Detections = new List<Detection>();
            AutoAssignNumbers = false;
            TargetLaps = 0;
        }

        public Race Clone()
        {
            Race clone = new Race();

            clone.AutoAssignNumbers = AutoAssignNumbers;
            clone.PilotChannels = PilotChannels.Clone().ToList();
            clone.Event = Event;
            clone.Round = Round;
            clone.Bracket = Bracket;
            clone.RaceNumber = RaceNumber;

            return clone;
        }

        public bool HasPilot(Pilot p)
        {
            lock (PilotChannels)
            {
                if (PilotChannels.Contains(p))
                {
                    return true;
                }
            }
            

            return false;
        }


        public PilotChannel GetPilotChannel(Channel c)
        {
            lock (PilotChannels)
            {
                return PilotChannels.Get(c);
            }
        }

        public PilotChannel GetPilotChannel(Pilot p)
        {
            lock (PilotChannels)
            {
                return PilotChannels.Get(p);
            }
        }

        public bool HasLap(Pilot p)
        {
            return Laps.Any(l => l.Pilot == p && l.Detection.Valid);
        }

        public PilotChannel SetPilot(IDatabase db, Channel channel, Pilot p)
        {
            if (channel == null || p == null)
                return null;

            if (Pilots.Contains(p))
                return null;

            if (Ended)
                return null;

            if (!IsFrequencyFree(channel))
                return null;

            PilotChannel pc = new PilotChannel(p, channel);
            lock (PilotChannels)
            {
                PilotChannels.Add(pc);
            }

            db.Insert(pc);
            db.Update(this);
            db.Update(p);

            return pc;
        }

        public PilotChannel ClearChannel(IDatabase db, Channel channel)
        {
            lock (PilotChannels)
            {
                PilotChannel pc = PilotChannels.Get(channel);
                if (pc != null)
                {
                    PilotChannels.Remove(pc);
                    db.Delete(pc);
                    return pc;
                }
            }
            return null;
        }

        public PilotChannel RemovePilot(IDatabase db, Pilot pilot)
        {
            if (Ended)
            {
                return null;
            }

            lock (PilotChannels)
            {
                PilotChannel pc = GetPilotChannel(pilot);
                if (pc != null)
                {
                    PilotChannels.Remove(pc);
                    db.Delete(pc);
                    return pc;
                }
            }
            return null;
        }

        public Channel GetChannel(Pilot pilot)
        {
            PilotChannel pc = GetPilotChannel(pilot);
            if (pc != null)
            {
                return pc.Channel;
            }
            return null;
        }

        // Typically used with 'lanes'
        public Pilot GetPilot(IEnumerable<Channel> channels)
        {
            lock (PilotChannels)
            {
                PilotChannel pc = PilotChannels.FirstOrDefault(pac => pac.Channel != null && channels.Contains(pac.Channel));
                if (pc != null)
                {
                    return pc.Pilot;
                }
            }
            return null;
        }

        public Pilot GetPilot(Channel channel)
        {
            lock (PilotChannels)
            {
                PilotChannel pc = PilotChannels.FirstOrDefault(pac => pac.Channel != null && pac.Channel == channel);
                if (pc != null)
                {
                    return pc.Pilot;
                }
            }
            return null;
        }

        public PilotChannel GetPilotChannel(int freq)
        {
            lock (PilotChannels)
            {
                PilotChannel pc = PilotChannels.FirstOrDefault(pac => pac.Channel != null && pac.Channel.Frequency == freq);
                if (pc != null)
                {
                    return pc;
                }
            }
            return null;
        }

        public Pilot GetPilot(int freq)
        {
            PilotChannel pc = GetPilotChannel(freq);
            if (pc != null)
            {
                return pc.Pilot;
            }
            return null;
        }

        public Lap RecordLap(IDatabase db, Detection detection)
        {
            if (!detection.IsLapEnd)
            {
                return null;
            }

            DateTime lapStart = GetRaceStartTime(detection.Pilot);

            // Manual override holeshot stuff.
            if (detection.Time < lapStart && detection.TimingSystemType == TimingSystemType.Manual)
            {
                lapStart = Start;
            }

            Lap prevLap = GetValidLaps(detection.Pilot, true).Where(l => l.Detection.Time < detection.Time).OrderBy(l => l.End).LastOrDefault();
            if (prevLap != null)
            {
                lapStart = prevLap.End;
            }

            // ignore impossibly short laps
            if ((detection.Time - lapStart).TotalMilliseconds < 1)
            {
                detection.Valid = false;
            }

            Lap lap = new Lap(this, lapStart, detection);
            lock (Laps)
            {
                Laps.Add(lap);
            }

            db.Insert(detection);
            lock (Detections)
            {
                Detections.Add(detection);
            }
            db.Insert(lap);
            db.Update(this);

            return lap;
        }

        public Lap[] GetValidLaps(Pilot pilot, bool includeHoleshot)
        {
            if (!Valid)
                return new Lap[0];

            lock (Laps)
            {
                if (includeHoleshot)
                {
                    return Laps.Where(l => l.Pilot == pilot && l.Detection != null && l.Detection.Valid).ToArray();
                }
                else
                {
                    return Laps.Where(l => l.Pilot == pilot && l.Detection != null && l.Detection.Valid && !l.Detection.IsHoleshot).ToArray();
                }
            }
        }

        public bool ClearPilots(IDatabase db)
        {
            if (Ended)
                return false;

            PilotChannel[] toDelete = PilotChannelsSafe;
            PilotChannels.Clear();
            foreach (var channel in toDelete)
            {
                db.Delete(channel);
            }
            return true;
        }

        public Lap GetLastValidLap(Pilot p)
        {
            lock (Laps)
            {
                return Laps.Where(l => l.Pilot == p && l.Detection.Valid && l.Detection.LapNumber <= TargetLaps).OrderByDescending(l => l.End).FirstOrDefault();
            }
        }

        public Detection[] GetValidDetections(Pilot pilot)
        {
            lock (Detections)
            {
                return Detections.Where(d => d.Pilot == pilot && d.Valid).ToArray();
            }
        }

        public Detection GetLastValidDetection(Pilot pilot, int lapNumber)
        {
            lock (Detections)
            {
                return Detections.Where(d => d.Pilot == pilot && d.LapNumber == lapNumber && d.Valid).OrderByDescending(d => d.TimingSystemIndex).FirstOrDefault();
            }
        }

        public Lap GetHoleshot(Pilot pilot)
        {
            if (PrimaryTimingSystemLocation == PrimaryTimingSystemLocation.EndOfLap)
                return null;

            lock (Laps)
            {
                return Laps.FirstOrDefault(l => l.Pilot == pilot && l.Detection.IsHoleshot);
            }
        }

        public int GetValidLapsCount(Pilot pilot, bool includeHoleshot)
        {
            lock (Laps)
            {
                IEnumerable<Lap> laps = Laps.Where(l => l.Pilot == pilot && l.Detection.Valid).Distinct();
                if (!includeHoleshot)
                {
                    laps = laps.Where(l => !l.Detection.IsHoleshot);
                }

                return laps.Count();
            }
        }

        public Lap[] GetValidLapsLast(Pilot pilot, int lapCount)
        {
            Lap[] laps = GetValidLaps(pilot, false);

            if (laps.Length < lapCount)
            {
                return laps;
            }

            return laps.Skip(laps.Length - lapCount).ToArray();
        }

        public Lap[] GetValidLapsInRace(Pilot pilot)
        {
            lock (Laps)
            {
                IEnumerable<Lap> laps = Laps.Where(l => l.Pilot == pilot && l.Detection.Valid && !l.Detection.IsHoleshot);

                if (Type == EventTypes.Race)
                {
                    laps = laps.Where(l => l.Number <= TargetLaps);
                }

                return laps.ToArray();
            }
        }

        public Lap[] GetLaps(Func<Lap, bool> predicate)
        {
            lock (Laps)
            {
                return Laps.Where(predicate).ToArray();
            }
        }

        public Split GetSplit(Detection detection)
        {
            Detection[] detections;

            lock (Detections)
            {
                detections = Detections.Where(d => d.Pilot == detection.Pilot && d.Valid).ToArray();
            }

            DateTime from = GetRaceStartTime(detection.Pilot);
            foreach (Detection d in detections)
            {
                if (d == detection)
                {
                    TimeSpan time = detection.Time - from;
                    if (time > TimeSpan.Zero)
                    {
                        return new Split(this, detection, time);
                    }
                }
                from = d.Time;
            }

            return null;
        }

        public IEnumerable<Split> GetSplits(Pilot pilot)
        {
            Detection[] detections;

            lock (Detections)
            {
                detections = Detections.Where(d => d.Pilot == pilot && d.Valid).ToArray();
            }

            DateTime from = GetRaceStartTime(pilot);
            foreach (Detection detection in detections)
            {
                TimeSpan time = detection.Time - from;
                if (time > TimeSpan.Zero)
                {
                    yield return new Split(this, detection, time);
                }

                from = detection.Time;
            }
        }


        public void ResetRace(IDatabase db)
        {
            lock (Laps)
            {
                Laps.Clear();
            }

            lock (Detections)
            {
                foreach (Detection d in Detections)
                {
                    d.Valid = false;
                    d.ValidityType = Detection.ValidityTypes.ManualOverride;
                }
                Detections.Clear();
            }

            db.Update(this);

            Start = default(DateTime);
            End = default(DateTime);
        }

        public DateTime GetRaceStartTime(Pilot p)
        {
            if (PrimaryTimingSystemLocation == PrimaryTimingSystemLocation.Holeshot)
            {
                Lap lap = GetHoleshot(p);
                if (lap != null)
                {
                    return lap.End;
                }
            }

            return Start;
        }

        public int GetPosition(Pilot pilot)
        {
            int position;
            Pilot behindWho;
            TimeSpan behind;

            GetPosition(pilot, out position, out behindWho, out behind);
            return position;
        }

        public bool GetPosition(Pilot pilot, out int position, out Pilot behindWho, out TimeSpan behind)
        {
            position = PilotChannels.Count;
            behindWho = null;
            behind = TimeSpan.Zero;
            Detection prevDetection = null;
            Detection latestThisPilotDetection = null;

            if (Detections.Count == 0)
            {
                return false;
            }

            int lastValidSector = int.MaxValue;
            if (Type == EventTypes.Race)
            {
                lastValidSector = Detection.RaceSectorCalculator(TargetLaps, 0);
            }

            Detection[] detections;
            lock (Detections)
            {
                detections = Detections.Where(d => d.Valid && d.RaceSector <= lastValidSector).OrderByDescending(d => d.RaceSector).ThenBy(d => d.Time).ToArray();
            }

            List<Pilot> pilotsAhead = new List<Pilot>();
            foreach (Detection detection in detections)
            {
                if (detection.Pilot == pilot)
                {
                    latestThisPilotDetection = detection;
                    break;
                }
                else
                {
                    if (!pilotsAhead.Contains(detection.Pilot))
                    {
                        pilotsAhead.Add(detection.Pilot);
                    }
                    behindWho = detection.Pilot;
                    prevDetection = detection;
                }
            }

            position = pilotsAhead.Count + 1;

            if (prevDetection == null || latestThisPilotDetection == null)
                behind = TimeSpan.Zero;
            else
                behind = latestThisPilotDetection.Time - prevDetection.Time;


            // If we're both on the same time, its a dead heat and we both share the same position
            if (behind == TimeSpan.Zero && position > 1 && latestThisPilotDetection != null)
            {
                position--;
            }

            if (Type == EventTypes.AggregateLaps)
            {
                behind = TimeSpan.Zero;
                behindWho = null;
            }

            return true;
        }


        public void ReCalculateLaps(IDatabase db, Pilot pilot)
        {
            int i = 0;

            if (PrimaryTimingSystemLocation == PrimaryTimingSystemLocation.EndOfLap)
                i = 1;

            IEnumerable<Detection> detections = GetValidDetections(pilot).OrderBy(l => l.Time);
            foreach (Detection d in detections)
            {
                if (d.LapNumber != i)
                {
                    d.LapNumber = i;
                    db.Update(d);
                }

                if (d.IsLapEnd)
                {
                    i++;
                }
            }

            IEnumerable<Lap> laps = GetValidLaps(pilot, true).OrderBy(l => l.End);
            DateTime lapStart = Start;
            foreach (Lap l in laps)
            {
                l.Start = lapStart;
                lapStart = l.End;

                db.Update(l);
            }
        }

        public bool IsFrequencyFree(Channel c)
        {
            if (c == null)
                return false;

            IEnumerable<Channel> interferring = c.GetInterferringChannels(Channels);
            return !interferring.Any();
        }

        public IEnumerable<Channel> GetFreeFrequencies(IEnumerable<Channel> frequencyOptions)
        {
            foreach (Channel fchanne in frequencyOptions)
            {
                if (IsFrequencyFree(fchanne))
                {
                    yield return fchanne;
                }
            }
        }

        public void SwapPilots(IDatabase db, Pilot pilot, Pilot otherPilot)
        {
            Channel channel = GetChannel(pilot);
            Channel otherChannel = GetChannel(otherPilot);

            RemovePilot(db, pilot);
            RemovePilot(db, otherPilot);

            SetPilot(db, otherChannel, pilot);
            SetPilot(db, channel, otherPilot);
        }

        public bool SwapPilots(IDatabase db, Pilot newPilot, Channel newChannel, Race oldRace)
        {
            PilotChannel existingPilotChannel = GetPilotChannel(newChannel.Frequency);
            Pilot existingPilot = null;
            PilotChannel oldPilotChannel = oldRace.GetPilotChannel(newPilot);
            Channel oldChannel = Channel.None;

            if (existingPilotChannel != null)
            {
                if (existingPilotChannel.Pilot != null)
                    existingPilot = existingPilotChannel.Pilot;
            }

            if (oldPilotChannel != null)
            {
                if (oldPilotChannel.Channel != null)
                    oldChannel = oldPilotChannel.Channel;
            }


            if (existingPilot == newPilot)
                existingPilot = null;

            if (newPilot == null)
                return false;

            if (oldRace != null && !oldRace.Ended)
            {
                oldRace.RemovePilot(db, newPilot);
            }
            
            if (existingPilot != null)
            {
                RemovePilot(db, existingPilot);
            }

            if (oldRace != null && existingPilot != null && oldChannel != Channel.None)
            {
                oldRace.SetPilot(db, oldChannel, existingPilot);
            }

            return SetPilot(db, newChannel, newPilot) != null;
        }

        public void RefreshPilots(IEnumerable<Pilot> editedPilots)
        {
            if (editedPilots == null)
                return;

            foreach (PilotChannel pc in PilotChannels)
            {
                Pilot p = editedPilots.GetObject(pc.Pilot.ID);
                if (p != null)
                {
                    pc.Pilot = p;
                }
            }
        }

        public override string ToString()
        {
            return Type + " " + RaceNumber + " (Round " + RoundNumber +  ") with " + PilotChannels.Count + " pilots.";
        }
    }
}
