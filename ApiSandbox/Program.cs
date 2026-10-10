using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Timing;
using Tools;
using Webb;

namespace ApiSandbox
{
    // FPVTrackside's REST API without the race-director screen: the real
    // RaceLib event engine and the real web server, against a demo event that
    // is generated on first run (8 pilots, a finished time-trial round with
    // split-gate crossings, and a race round ready to run). Use it to try the
    // API and its console, or to test a client, without a venue setup.
    //
    //   dotnet run --project ApiSandbox -- [--port 8080] [--data DIR] [--fresh]
    //
    // A thread stands in for the game loop: it calls EventManager.Update and
    // runs every API call, exactly as the app's UI thread does.
    public static class Program
    {
        public static int Main(string[] args)
        {
            int port = 8080;
            string data = Path.Combine(Directory.GetCurrentDirectory(), "sandbox-data");
            bool fresh = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--port": port = int.Parse(args[++i]); break;
                    case "--data": data = Path.GetFullPath(args[++i]); break;
                    case "--fresh": fresh = true; break;
                    case "-h":
                    case "--help":
                        Console.WriteLine("ApiSandbox [--port 8080] [--data DIR] [--fresh]");
                        return 0;
                }
            }

            DirectoryInfo dataDir = new DirectoryInfo(data);
            if (fresh && dataDir.Exists)
                dataDir.Delete(true);
            dataDir.Create();

            IOTools.WorkingDirectory = dataDir;
            Logger.Init(dataDir.CreateSubdirectory("log"));
            Directory.SetCurrentDirectory(dataDir.FullName);
            DirectoryInfo eventDir = dataDir.CreateSubdirectory("events");
            dataDir.CreateSubdirectory("httpfiles");
            DatabaseFactory.Init(new DB.DatabaseFactory(dataDir, eventDir));

            Profile profile = new Profile(dataDir, "Sandbox");

            // FPVTrackside's built-in simulated timer: random laps around 22 s
            // during a race, with the occasional false read so there are real
            // mistakes to correct.
            TimingSystemSettings.Write(profile, new TimingSystemSettings[]
            {
                new DummySettings
                {
                    Role = TimingSystemRole.Primary,
                    GenerateRandomLaps = true,
                    TypicalLapTimeSeconds = 22,
                    RangeSeconds = 4,
                    FalseReadPercent = 5,
                    TestConnectionFailureRatePercent = 0,
                    GenerateFromKeyboardShortcuts = false,
                },
            });
            GameLoop loop = new GameLoop();

            Guid eventId = DemoEvent.FindOrCreate(eventDir);
            EventManager eventManager = loop.Run(() => new EventManager(profile));
            DemoEvent.Load(eventManager, eventId);
            loop.Run(() => { DemoEvent.FillIfEmpty(eventManager); return true; });

            loop.Every = () => eventManager.Update(new GameTime());
            UIEvents.Watch(eventManager);

            EventWebServer server = new EventWebServer(eventManager, null, new SandboxRaceControl(eventManager), new ToolColor[0], eventDir.FullName, loop.Invoke);
            server.Url = "http://localhost:" + port + "/";
            server.Start();

            Console.WriteLine("FPVTrackside API sandbox — event \"" + eventManager.Event.Name + "\"");
            Console.WriteLine("  Console:  http://localhost:" + port + "/api/v1/docs");
            Console.WriteLine("  API:      http://localhost:" + port + "/api/v1");
            Console.WriteLine("  Data:     " + dataDir.FullName + "   (--fresh to regenerate)");
            Console.WriteLine("Ctrl+C to stop.");

            ManualResetEvent stop = new ManualResetEvent(false);
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; stop.Set(); };
            stop.WaitOne();

            server.Stop();
            loop.Dispose();
            return 0;
        }
    }

    // Stands in for the app's UI thread.
    public class GameLoop : IDisposable
    {
        private readonly BlockingCollection<Action> todo = new BlockingCollection<Action>();
        private readonly Thread thread;
        private volatile bool running = true;

        public Action Every { get; set; }

        public GameLoop()
        {
            thread = new Thread(Loop) { Name = "Sandbox game loop", IsBackground = true };
            thread.Start();
        }

        public void Invoke(Action a)
        {
            todo.Add(a);
        }

        public T Run<T>(Func<T> f)
        {
            T result = default(T);
            Exception error = null;
            using (ManualResetEventSlim done = new ManualResetEventSlim())
            {
                Invoke(() => { try { result = f(); } catch (Exception e) { error = e; } done.Set(); });
                done.Wait();
            }
            if (error != null)
                throw new Exception(error.Message, error);
            return result;
        }

        private void Loop()
        {
            while (running)
            {
                Action a;
                if (todo.TryTake(out a, 16))
                {
                    try { a(); } catch (Exception e) { Console.WriteLine("game loop: " + e); }
                }
                try { Every?.Invoke(); } catch (Exception e) { Console.WriteLine("update: " + e.Message); }
            }
        }

        public void Dispose()
        {
            running = false;
            thread.Join(1000);
        }
    }

    // Race start for the sandbox: no countdown sounds or video, just the
    // engine's own start sequence.
    public class SandboxRaceControl : IRaceControl
    {
        private readonly EventManager eventManager;

        public SandboxRaceControl(EventManager eventManager)
        {
            this.eventManager = eventManager;
        }

        public bool StartRace()
        {
            RaceManager rm = eventManager.RaceManager;
            if (!rm.CanRunRace)
                return false;
            if (rm.PreRaceStart() && rm.StartRaceInLessThan(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)))
                return true;

            rm.CancelRaceStart(true);
            return false;
        }
    }

    // Prints each notification the race-director UI listens for, so you can
    // see which screens an API call would refresh in the real app.
    public static class UIEvents
    {
        public static void Watch(EventManager em)
        {
            RaceManager rm = em.RaceManager;
            Action<string> say = what => Console.WriteLine("[ui] " + what);
            Func<Race, string> name = r => r == null ? "(none)" : r.RaceName;

            em.OnEventChange += () => say("event settings changed");
            em.OnPilotRefresh += () => say("pilot list refreshed");
            em.OnPilotChangedChannels += pc => say("pilot channel changed: " + pc.Pilot?.Name + " -> " + pc.Channel?.GetBandChannelText());
            em.RoundManager.OnRoundAdded += r => say("round added: " + r);
            em.RoundManager.OnRoundRemoved += r => say("round removed: " + r);
            em.RoundManager.OnRoundRenamed += r => say("round renamed: " + r.Name);
            rm.OnRaceCreated += r => say("race created: " + name(r));
            rm.OnRaceRemoved += r => say("race removed: " + name(r));
            rm.OnRaceChanged += r => say("selected race: " + name(r));
            rm.OnRacePilotsSet += r => say("race line-up changed: " + name(r));
            rm.OnPilotAdded += pc => say("pilot seated in selected race: " + pc.Pilot?.Name + " on " + pc.Channel?.GetBandChannelText());
            rm.OnPilotRemoved += pc => say("pilot left selected race: " + pc.Pilot?.Name);
            rm.OnLapsRecalculated += r => say("laps recalculated: " + name(r));
            rm.OnLapDetected += l => say("lap: " + l.Pilot?.Name + " " + l.Length.TotalSeconds.ToString("0.000") + "s");
            rm.OnRaceReset += r => say("race reset: " + name(r));
            rm.OnRaceStart += r => say("race started: " + name(r));
            rm.OnRaceEnd += r => say("race ended: " + name(r));
            rm.OnRaceCancelled += (r, f) => say("race start cancelled: " + name(r));
        }
    }

    public static class DemoEvent
    {
        public const string Name = "API Sandbox Cup";

        private static readonly string[] Pilots = { "Wheelzfpv", "MaximalGoose", "SpicyMayo", "Pitstop", "heepsy", "Lucid", "thereyet", "Red2rotor" };

        public static Guid FindOrCreate(DirectoryInfo eventDir)
        {
            using (IDatabase db = DatabaseFactory.Open(Guid.Empty))
            {
                SimpleEvent existing = db.GetSimpleEvents().FirstOrDefault(e => e.Enabled && e.Name == Name);
                if (existing != null)
                    return existing.ID;

                Event ev = new Event
                {
                    Name = Name,
                    EventType = EventTypes.Race,
                    Laps = 3,
                    PBLaps = 3,
                    RaceLength = TimeSpan.FromMinutes(2),
                    MinStartDelay = TimeSpan.FromSeconds(1),
                    MaxStartDelay = TimeSpan.FromSeconds(2),
                    Channels = Channel.RaceBand.ToArray(),
                };
                db.Insert(ev);
                return ev.ID;
            }
        }

        public static void Load(EventManager em, Guid eventId)
        {
            using (WorkQueue queue = new WorkQueue("Sandbox load"))
            using (ManualResetEvent done = new ManualResetEvent(false))
            {
                WorkSet set = new WorkSet();
                Exception error = null;
                set.OnError += (wi, e) => { error = e; };
                em.LoadEvent(set, queue, eventId);
                em.LoadRaces(set, queue);
                queue.Enqueue(set, "done", () => done.Set());
                done.WaitOne();
                if (error != null)
                    throw new Exception("loading the event: " + error.Message, error);
            }
        }

        // First run only: pilots, a finished time-trial round with split-gate
        // crossings (so lap corrections have something to anchor to), and an
        // un-run race round.
        public static void FillIfEmpty(EventManager em)
        {
            if (em.Event.Pilots.Any())
                return;

            Random random = new Random(7);
            List<Pilot> pilots = new List<Pilot>();
            for (int i = 0; i < Pilots.Length; i++)
            {
                Pilot p = em.GetCreatePilot(Pilots[i]);
                em.AddPilot(p, em.Channels[i % em.Channels.Length]);
                pilots.Add(p);
            }

            RaceManager rm = em.RaceManager;
            Round tt = em.RoundManager.CreateEmptyRound(EventTypes.TimeTrial);
            DateTime start = DateTime.Now.AddMinutes(-40);
            for (int heat = 0; heat < 2; heat++)
            {
                Race race = rm.AddRaceToRound(tt);
                rm.SetRacePilots(race, pilots.Skip(heat * 4).Take(4).Select((p, i) => Tuple.Create(p, em.Channels[i * 2])), false);
                race.Start = start.AddMinutes(heat * 6);
                race.TargetLaps = 3;

                foreach (Pilot p in race.Pilots)
                {
                    double pace = 22 + random.NextDouble() * 6;
                    DateTime t = race.Start.AddSeconds(4 + random.NextDouble() * 2);
                    List<DateTime> lines = new List<DateTime> { t };
                    for (int lap = 0; lap < 4; lap++)
                    {
                        AddSplit(em, race, p, t.AddSeconds(pace * 0.45));
                        t = t.AddSeconds(pace + random.NextDouble() * 2 - 1);
                        lines.Add(t);
                    }
                    rm.AddLapsToRace(race, p, lines);
                }

                race.End = race.Start.AddMinutes(2.5);
                using (IDatabase db = DatabaseFactory.Open(em.EventId))
                {
                    db.Update(race);
                }
                em.ResultManager.SaveResults(race);
            }

            Round main = em.RoundManager.CreateEmptyRound(EventTypes.Race);
            for (int heat = 0; heat < 2; heat++)
            {
                Race race = rm.AddRaceToRound(main);
                rm.SetRacePilots(race, pilots.Skip(heat * 4).Take(4).Select((p, i) => Tuple.Create(p, em.Channels[i * 2])), false);
            }
            rm.SetRace(rm.GetRaces(main).First());
        }

        private static void AddSplit(EventManager em, Race race, Pilot p, DateTime time)
        {
            Detection d = new Detection(TimingSystemType.Dummy, 1, p, race.GetChannel(p), time, 0, false, 0);
            using (IDatabase db = DatabaseFactory.Open(em.EventId))
            {
                db.Insert(d);
            }
            lock (race.Detections)
            {
                race.Detections.Add(d);
            }
        }
    }
}
