using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UI.Video
{
    public enum MotionState
    {
        Unknown,
        ActiveMotion,
        ActiveNoMotion,
        InactiveMotion,
        InactiveNoMotion,
    }

    public class MotionDetector
    {
        private Queue<TimeMotionContainer> lastFrames;

        public TimeSpan QueueLength { get; set; }

        // Saturation (colour spread per pixel) — static/no-signal feeds read ~0.3, active FPV feeds ~50+
        private const float LowThreshold = 4f;   // below this → inactive
        private const float HighThreshold = 20f; // must exceed this to recover from inactive state

        // Frame-diff (mean per-channel pixel delta) — truly static feeds read ~0.1, flying drones ~9-12
        private const float DiffLowThreshold = 3f;  // below this → no motion
        private const float DiffHighThreshold = 8f; // must exceed this to recover from no-motion state

        private bool inLowState;     // hysteresis state for saturation
        private bool diffInLowState; // hysteresis state for frame-diff
        private Color[] previousFrame;

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
            diffInLowState = false;
            previousFrame = null;
        }

        public void AddFrame(Color[] frameData)
        {
            float saturation = ProcessSaturation(frameData);
            float diff = ProcessDiff(frameData, previousFrame);

            previousFrame = (Color[])frameData.Clone();

            lastFrames.Enqueue(new TimeMotionContainer() { Saturation = saturation, Diff = diff, Time = DateTime.Now });

            TimeMotionContainer oldest = lastFrames.FirstOrDefault();
            while (oldest != null && DateTime.Now - oldest.Time > QueueLength)
            {
                lastFrames.Dequeue();
                oldest = lastFrames.FirstOrDefault();
            }
        }

        private float ProcessSaturation(Color[] frameData)
        {
            float sum = 0;
            foreach (Color pixel in frameData)
            {
                byte min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                byte max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                sum += max - min;
            }

            sum /= frameData.Length;

            if (float.IsNaN(sum))
                sum = 0;

            return sum;
        }

        private float ProcessDiff(Color[] frameData, Color[] prevFrameData)
        {
            if (prevFrameData == null || prevFrameData.Length != frameData.Length)
                return 0;

            float sum = 0;
            for (int i = 0; i < frameData.Length; i++)
            {
                sum += Math.Abs(frameData[i].R - prevFrameData[i].R)
                     + Math.Abs(frameData[i].G - prevFrameData[i].G)
                     + Math.Abs(frameData[i].B - prevFrameData[i].B);
            }

            return sum / (frameData.Length * 3);
        }

        public bool HasMotionData()
        {
            TimeMotionContainer t = lastFrames.FirstOrDefault();
            if (t != null)
            {
                TimeSpan sinceOldestFrame = DateTime.Now - t.Time;
                return sinceOldestFrame.TotalSeconds > QueueLength.TotalSeconds * 0.9;
            }
            return false;
        }

        public MotionState DetectMotion(out float saturationValue, out float diffValue)
        {
            saturationValue = 0;
            diffValue = 0;

            if (!HasMotionData())
                return MotionState.Unknown;

            saturationValue = lastFrames.Select(t => t.Saturation).Average();
            diffValue = lastFrames.Select(t => t.Diff).Average();

            bool active;
            if (inLowState)
            {
                active = saturationValue > HighThreshold;
                if (active)
                    inLowState = false;
            }
            else
            {
                active = saturationValue > LowThreshold;
                if (!active)
                    inLowState = true;
            }

            bool motion;
            if (diffInLowState)
            {
                motion = diffValue > DiffHighThreshold;
                if (motion)
                    diffInLowState = false;
            }
            else
            {
                motion = diffValue > DiffLowThreshold;
                if (!motion)
                    diffInLowState = true;
            }

            if (active && motion) return MotionState.ActiveMotion;
            if (active && !motion) return MotionState.ActiveNoMotion;
            if (!active && motion) return MotionState.InactiveMotion;
            return MotionState.InactiveNoMotion;
        }

        private class TimeMotionContainer
        {
            public DateTime Time;
            public float Saturation;
            public float Diff;
        }

    }
}
