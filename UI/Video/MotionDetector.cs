using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Video
{
    public class MotionDetector
    {
        private Queue<TimeMotionContainer> lastFrames;

        public TimeSpan QueueLength { get; set; }

        public float LowThreshold { get { return ApplicationProfileSettings.Instance.CrashThreshold; } }
        public float HighThreshold { get { return ApplicationProfileSettings.Instance.ReactivateThreshold; } }

        private bool inLowState;
        public Channel Channel { get; private set; }

        public MotionDetector(Channel channel)
        {
            Channel = channel;
            QueueLength = TimeSpan.FromSeconds(4);
            lastFrames = new Queue<TimeMotionContainer>();
        }

        public void Clear()
        {
            lastFrames.Clear();
            inLowState = false;
        }

        public void AddFrame(Color[] frameData)
        {
            float next = Process(frameData);

            //Logger.VideoLog.Log(this, Channel.ToStringShort() + " : " + next);  

            lastFrames.Enqueue(new TimeMotionContainer() { Motion = next, Time = DateTime.Now });

            // remove old lastframes
            while (lastFrames.FirstOrDefault() != null && DateTime.Now - lastFrames.FirstOrDefault().Time > QueueLength)
            {
                lastFrames.Dequeue();
            }
        }

        private float Process(Color[] frameData)
        {
            float sum = 0;
            foreach (Color pixel in frameData)
            {
                byte min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                byte max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));

                int diff = max - min;

                sum += diff;
            }

            sum /= frameData.Length;

            if (float.IsNaN(sum))
            {
                sum = 0;
            }

            return sum;
        }

        public bool HasMotionData()
        {
            TimeMotionContainer t = lastFrames.FirstOrDefault();
            if (t != null)
            {
                TimeSpan sinceOldestFrame = DateTime.Now - t.Time;

                // Queue needs to be a certain amount full before we say yes, we have motion data.
                return sinceOldestFrame.TotalSeconds > QueueLength.TotalSeconds * 0.9;
            }
            return false;
        }

        public float GetCurrentMotionValue()
        {
            if (!HasMotionData())
            {
                return 0;
            }

            TimeMotionContainer tmc = lastFrames.LastOrDefault();
            if (tmc == null)
                return 0;
            return tmc.Motion;
        }

        public void DetectMotion(out float motionValue, out bool motion)
        {
            motionValue = 0;
            motion = true;

            if (!HasMotionData())
            {
                return;
            }

            motionValue = lastFrames.Select(t => t.Motion).Average();

            if (inLowState)
            {
                motion = motionValue > HighThreshold;
                if (motion)
                    inLowState = false;
            }
            else
            {
                motion = motionValue > LowThreshold;
                if (!motion)
                    inLowState = true;
            }

        }

        private class TimeMotionContainer
        {
            public DateTime Time;
            public float Motion;
        }

    }
}
