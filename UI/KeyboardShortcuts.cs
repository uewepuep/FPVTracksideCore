using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Input;
using Tools;

namespace Composition.Input
{
    public class KeyboardShortcuts
    {
        [Category("Pilot List")]
        public ShortcutKey ShowPilotList { get; set; }
     
        [Category("Pilot List")]
        public ShortcutKey HidePilotList { get; set; }
        
        [Category("Live Video")]
        public ShortcutKey ShowMoreChannels { get; set; }
        [Category("Live Video")]
        public ShortcutKey ShowLessChannels { get; set; }

        [Category("Live Video")] 
        public ShortcutKey ShowLaps { get; set; }
        [Category("Live Video")]
        public ShortcutKey HideLaps { get; set; }
        [Category("Live Video")] 
        public ShortcutKey ReOrderChannelsNow { get; set; }

        [Category("Live Video")]
        public ShortcutKey ScenePreRace { get; set; }
        [Category("Live Video")]
        public ShortcutKey SceneRace { get; set; }

        [Category("Live Video")]
        public ShortcutKey SceneFinishLine { get; set; }
        
        [Category("Live Video")]
        public ShortcutKey ScenePostRace { get; set; }

        [Category("Live Video")]
        public ShortcutKey SceneCommentators { get; set; }

        [Category("Live Video")]
        public ShortcutKey SceneEventStatus { get; set; }

        [Category("Live Video")]
        public ShortcutKey ShowWorm { get; set; }

        [Category("Race Control")]
        public ShortcutKey StartStopRace { get; set; }
        [Category("Race Control")]
        public ShortcutKey ResumeRace { get; set; }
        [Category("Race Control")]
        public ShortcutKey NextRace { get; set; }
        [Category("Race Control")]
        public ShortcutKey PrevRace { get; set; }

        [Category("Laps")]
        public ShortcutKey AddLapChannelGroup1 { get; set; }
        [Category("Laps")]
        public ShortcutKey AddLapChannelGroup2 { get; set; }
        [Category("Laps")]
        public ShortcutKey AddLapChannelGroup3 { get; set; }
        [Category("Laps")]
        public ShortcutKey AddLapChannelGroup4 { get; set; }
        [Category("Laps")]
        public ShortcutKey AddLapChannelGroup5 { get; set; }
        [Category("Laps")]
        public ShortcutKey AddLapChannelGroup6 { get; set; }
        [Category("Laps")]
        public ShortcutKey AddLapChannelGroup7 { get; set; }
        [Category("Laps")]
        public ShortcutKey AddLapChannelGroup8 { get; set; }

        [Browsable(false)]
        public IEnumerable<ShortcutKey> AddLapChannelGroup
        {
            get
            {
                yield return AddLapChannelGroup1;
                yield return AddLapChannelGroup2;
                yield return AddLapChannelGroup3;
                yield return AddLapChannelGroup4;
                yield return AddLapChannelGroup5;
                yield return AddLapChannelGroup6;
                yield return AddLapChannelGroup7;
                yield return AddLapChannelGroup8;
            }
        }

        [Category("Laps")]
        public ShortcutKey RemoveLapChannelGroup1 { get; set; }
        [Category("Laps")]
        public ShortcutKey RemoveLapChannelGroup2 { get; set; }
        [Category("Laps")]
        public ShortcutKey RemoveLapChannelGroup3 { get; set; }
        [Category("Laps")]
        public ShortcutKey RemoveLapChannelGroup4 { get; set; }
        [Category("Laps")]
        public ShortcutKey RemoveLapChannelGroup5 { get; set; }
        [Category("Laps")]
        public ShortcutKey RemoveLapChannelGroup6 { get; set; }
        [Category("Laps")]
        public ShortcutKey RemoveLapChannelGroup7 { get; set; }
        [Category("Laps")]
        public ShortcutKey RemoveLapChannelGroup8 { get; set; }

        [Browsable(false)]
        public IEnumerable<ShortcutKey> RemoveLapChannelGroup
        {
            get
            {
                yield return RemoveLapChannelGroup1;
                yield return RemoveLapChannelGroup2;
                yield return RemoveLapChannelGroup3;
                yield return RemoveLapChannelGroup4;
                yield return RemoveLapChannelGroup5;
                yield return RemoveLapChannelGroup6;
                yield return RemoveLapChannelGroup7;
                yield return RemoveLapChannelGroup8;
            }
        }

        [Category("FPV View")]
        public ShortcutKey ToggleViewChannelGroup1 { get; set; }
        [Category("FPV View")]
        public ShortcutKey ToggleViewChannelGroup2 { get; set; }
        [Category("FPV View")]
        public ShortcutKey ToggleViewChannelGroup3 { get; set; }
        [Category("FPV View")]
        public ShortcutKey ToggleViewChannelGroup4 { get; set; }
        [Category("FPV View")]
        public ShortcutKey ToggleViewChannelGroup5 { get; set; }
        [Category("FPV View")]
        public ShortcutKey ToggleViewChannelGroup6 { get; set; }
        [Category("FPV View")]
        public ShortcutKey ToggleViewChannelGroup7 { get; set; }
        [Category("FPV View")]
        public ShortcutKey ToggleViewChannelGroup8 { get; set; }

        [Browsable(false)]
        public IEnumerable<ShortcutKey> ToggleView
        {
            get
            {
                yield return ToggleViewChannelGroup1;
                yield return ToggleViewChannelGroup2;
                yield return ToggleViewChannelGroup3;
                yield return ToggleViewChannelGroup4;
                yield return ToggleViewChannelGroup5;
                yield return ToggleViewChannelGroup6;
                yield return ToggleViewChannelGroup7;
                yield return ToggleViewChannelGroup8;
            }
        }



        [Category("Replay")]
        public ShortcutKey ReplayPlayStop { get; set; }
        [Category("Replay")]
        public ShortcutKey ReplayNextFrame { get; set; }
        [Category("Replay")]
        public ShortcutKey ReplayPrevFrame { get; set; }

        [Category("Replay")]
        public ShortcutKey ReplayPlus5Seconds { get; set; }
        [Category("Replay")]
        public ShortcutKey ReplayMinus5Seconds { get; set; }


        [Category("Sound")]
        public ShortcutKey StopSound { get; set; }

        [Category("Sound")]
        public ShortcutKey EnableWAVAudio { get; set; }

        [Category("Sound")]
        public ShortcutKey DisableWAVAudio { get; set; }


        [Category("Sound")]
        public ShortcutKey EnableTTSAudio { get; set; }

        [Category("Sound")]
        public ShortcutKey DisableTTSAudio { get; set; }


        [Category("Trigger Sound")]
        public ShortcutKey AnnounceRace { get; set; }

        [Category("Trigger Sound")]
        public ShortcutKey AnnounceRaceResults { get; set; }

        [Category("Trigger Sound")]
        public ShortcutKey HurryUpEveryone { get; set; }

        [Category("Trigger Sound")]
        public ShortcutKey UntilRaceStart { get; set; }

        [Category("Trigger Sound")]
        public ShortcutKey TimeRemaining { get; set; }

        [Category("Trigger Sound")]
        public ShortcutKey RaceOver { get; set; }

        [Category("Trigger Sound")]
        public ShortcutKey Custom1 { get; set; }
        [Category("Trigger Sound")]
        public ShortcutKey Custom2 { get; set; }
        [Category("Trigger Sound")]
        public ShortcutKey Custom3 { get; set; }
        [Category("Trigger Sound")]
        public ShortcutKey Custom4 { get; set; }
        [Category("Trigger Sound")]
        public ShortcutKey Custom5 { get; set; }


        [Category("Global / Unfocused")]
        public ShortcutKey GlobalStartStopRace { get; set; }

        [Category("Global / Unfocused")]
        public ShortcutKey GlobalNextRace { get; set; }

        [Category("Global / Unfocused")]
        public ShortcutKey GlobalPrevRace { get; set; }

        [Category("Global / Unfocused")]
        public ShortcutKey GlobalCopyResults { get; set; }

        private const string filename = "Keys.xml";

        public KeyboardShortcuts()
        {
            HidePilotList = new ShortcutKey(Keys.Left);
            ShowPilotList = new ShortcutKey(Keys.Right);
            
            ShowMoreChannels = new ShortcutKey(Keys.Up);
            ShowLessChannels = new ShortcutKey(Keys.Down);
            
            ShowLaps = new ShortcutKey(Keys.L);
            HideLaps = new ShortcutKey(Keys.K);
            
            ReOrderChannelsNow = new ShortcutKey(Keys.O);
            
            StartStopRace = new ShortcutKey(Keys.Space);
            ResumeRace = new ShortcutKey(Keys.Space, true);

            NextRace = new ShortcutKey(Keys.OemCloseBrackets);
            PrevRace = new ShortcutKey(Keys.OemOpenBrackets);

            ShowWorm = new ShortcutKey(Keys.W);

            ScenePreRace = new ShortcutKey(Keys.F1);
            SceneRace = new ShortcutKey(Keys.F2);
            SceneFinishLine = new ShortcutKey(Keys.F3);
            ScenePostRace = new ShortcutKey(Keys.F4);
            SceneCommentators = new ShortcutKey(Keys.F5);
            SceneEventStatus = new ShortcutKey(Keys.F6);

            ReplayPlayStop = new ShortcutKey(Keys.Space);
            ReplayPrevFrame = new ShortcutKey(Keys.Right, true);
            ReplayNextFrame = new ShortcutKey(Keys.Left, true);

            ReplayPlus5Seconds = new ShortcutKey(Keys.Right);
            ReplayMinus5Seconds = new ShortcutKey(Keys.Left);

            AddLapChannelGroup1 = new ShortcutKey(Keys.D1, false, true);
            AddLapChannelGroup2 = new ShortcutKey(Keys.D2, false, true);
            AddLapChannelGroup3 = new ShortcutKey(Keys.D3, false, true);
            AddLapChannelGroup4 = new ShortcutKey(Keys.D4, false, true);
            AddLapChannelGroup5 = new ShortcutKey(Keys.D5, false, true);
            AddLapChannelGroup6 = new ShortcutKey(Keys.D6, false, true);
            AddLapChannelGroup7 = new ShortcutKey(Keys.D7, false, true);
            AddLapChannelGroup8 = new ShortcutKey(Keys.D8, false, true);

            RemoveLapChannelGroup1 = new ShortcutKey(Keys.D1, true);
            RemoveLapChannelGroup2 = new ShortcutKey(Keys.D2, true);
            RemoveLapChannelGroup3 = new ShortcutKey(Keys.D3, true);
            RemoveLapChannelGroup4 = new ShortcutKey(Keys.D4, true);
            RemoveLapChannelGroup5 = new ShortcutKey(Keys.D5, true);
            RemoveLapChannelGroup6 = new ShortcutKey(Keys.D6, true);
            RemoveLapChannelGroup7 = new ShortcutKey(Keys.D7, true);
            RemoveLapChannelGroup8 = new ShortcutKey(Keys.D8, true);

            AnnounceRace = new ShortcutKey(Keys.A, true);
            AnnounceRaceResults = new ShortcutKey(Keys.R, true);
            HurryUpEveryone = new ShortcutKey(Keys.H, true);
            UntilRaceStart = new ShortcutKey(Keys.N, true);
            TimeRemaining = new ShortcutKey(Keys.T, true);
            RaceOver = new ShortcutKey(Keys.O, true);

            Custom1 = new ShortcutKey(Keys.D1, true, true);
            Custom2 = new ShortcutKey(Keys.D2, true, true);
            Custom3 = new ShortcutKey(Keys.D3, true, true);
            Custom4 = new ShortcutKey(Keys.D4, true, true);
            Custom5 = new ShortcutKey(Keys.D5, true, true);

            ToggleViewChannelGroup1 = new ShortcutKey(Keys.D1, true, true, true);
            ToggleViewChannelGroup2 = new ShortcutKey(Keys.D2, true, true, true);
            ToggleViewChannelGroup3 = new ShortcutKey(Keys.D3, true, true, true);
            ToggleViewChannelGroup4 = new ShortcutKey(Keys.D4, true, true, true);
            ToggleViewChannelGroup5 = new ShortcutKey(Keys.D5, true, true, true);
            ToggleViewChannelGroup6 = new ShortcutKey(Keys.D6, true, true, true);
            ToggleViewChannelGroup7 = new ShortcutKey(Keys.D7, true, true, true);
            ToggleViewChannelGroup8 = new ShortcutKey(Keys.D8, true, true, true);


            StopSound = new ShortcutKey(Keys.Escape);
            EnableTTSAudio = new ShortcutKey(Keys.T, true, true, false);
            DisableTTSAudio = new ShortcutKey(Keys.T, true, true, true);

            EnableWAVAudio = new ShortcutKey(Keys.W, true, true, false);
            DisableWAVAudio = new ShortcutKey(Keys.W, true, true, true);
        }


        public static KeyboardShortcuts Read(Profile profile)
        {
            KeyboardShortcuts s = null;
            try
            {
                s = Tools.IOTools.Read<KeyboardShortcuts>(profile,filename).FirstOrDefault();
                if (s == null)
                {
                    s = new KeyboardShortcuts();
                }

                return s;
            }
            catch
            {
                return new KeyboardShortcuts();
            }
        }

        public static void Write(Profile profile, KeyboardShortcuts sources)
        {
            Tools.IOTools.Write(profile, filename, sources);
        }
    }

    public static class ShortcutKeyExt
    {
        public static bool Match(this ShortcutKey key, KeyboardInputEvent keyboardInputEvent)
        {
            return key.Match(keyboardInputEvent.Key, keyboardInputEvent.Ctrl, keyboardInputEvent.Alt, keyboardInputEvent.Shift);
        }
    }
}
