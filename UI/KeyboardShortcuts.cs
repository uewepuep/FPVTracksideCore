using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Input;

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

        [Category("Replay")]
        public ShortcutKey ReplayPlayStop { get; set; }
        [Category("Replay")]
        public ShortcutKey ReplayNextFrame { get; set; }
        [Category("Replay")]
        public ShortcutKey ReplayPrevFrame { get; set; }

        private static string filename = @"data/Keys.xml";

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
            ScenePostRace = new ShortcutKey(Keys.F3);
            SceneEventStatus = new ShortcutKey(Keys.F4);
            SceneCommentators = new ShortcutKey(Keys.F5);

            ReplayPlayStop = new ShortcutKey(Keys.Space);
            ReplayPrevFrame = new ShortcutKey(Keys.OemComma);
            ReplayNextFrame = new ShortcutKey(Keys.OemPeriod);

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
        }


        public static KeyboardShortcuts Read()
        {
            KeyboardShortcuts s = null;
            try
            {
                s = Tools.IOTools.Read<KeyboardShortcuts>(filename).FirstOrDefault();
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

        public static void Write(KeyboardShortcuts sources)
        {
            Tools.IOTools.Write(filename, sources);
        }
    }

    public class ShortcutKey
    {
        public bool ControlKey { get; set; }
        public bool AltKey { get; set; }
        public bool ShiftKey { get; set; }

        public Keys Key { get; set; }
       
        public ShortcutKey()
        {
            ControlKey = false;
            AltKey = false;
            ShiftKey = false;
        }


        public ShortcutKey(Keys key, bool ctrl = false, bool alt = false, bool shift = false)
        {
            Key = key;
            ControlKey = ctrl;
            AltKey = alt;
            ShiftKey = shift;
        }

        public ShortcutKey(KeyboardInputEvent copy)
        {
            Key = copy.Key;
            ControlKey = copy.Ctrl;
            AltKey = copy.Alt;
            ShiftKey = copy.Shift;
        }

        public bool Match(KeyboardInputEvent keyboardInputEvent)
        {
            return Match(keyboardInputEvent.Key, keyboardInputEvent.Ctrl, keyboardInputEvent.Alt, keyboardInputEvent.Shift);
        }

        public bool Match(Keys key, bool ctrl, bool alt, bool shift)
        {
            if (ControlKey != ctrl) return false;
            if (ShiftKey != shift) return false;
            if (AltKey != alt) return false;

            return key == Key;
        }

        public override string ToString()
        {
            List<string> texts = new List<string>();

            if (ControlKey) texts.Add("Ctrl");
            if (AltKey) texts.Add("Alt");
            if (ShiftKey) texts.Add("Shift");

            texts.Add(Key.ToString());

            return string.Join(" + ", texts);
        }
    }
}
