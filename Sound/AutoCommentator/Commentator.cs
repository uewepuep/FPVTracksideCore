//using Microsoft.Xna.Framework;
//using RaceLib;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Speech.Synthesis;
//using System.Text;
//using System.Threading.Tasks;
//using Tools;

//namespace Sound.AutoCommentator
//{
//    public class Commentator : IDisposable
//    {
//        private EventManager eventManager;
//        private WorkQueue ttsQueue;

//        private SpeechSynthesizer speechSynthesizer;

//        public Commentator(EventManager em, string voice)
//        {
//            eventManager = em;
//            ttsQueue = new WorkQueue("Commentator");

//            speechSynthesizer = new SpeechSynthesizer();

//            var chosen = speechSynthesizer.GetInstalledVoices().FirstOrDefault(v => v.VoiceInfo.Name == voice);
//            if (chosen != null)
//            {
//                speechSynthesizer.SelectVoice(chosen.VoiceInfo.Name);
//            }
//        }

//        public void Dispose()
//        {
//            if (ttsQueue != null)
//                ttsQueue.Dispose();

//            if (speechSynthesizer != null)
//                speechSynthesizer.Dispose();
//        }

//        public void Update(GameTime gameTime)
//        {

//        }
//    }
//}
