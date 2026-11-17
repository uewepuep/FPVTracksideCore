using ImageServer;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace UI.Video
{
    /// <summary>
    /// Keeps the latest frame of each watched <see cref="FrameSource"/>, copied on the capture
    /// thread through <see cref="FrameSource.FrameCaptured"/>, so detection sees every frame the
    /// camera delivers rather than the ones the display happens to draw.
    ///
    /// The copy is taken before the marker overlay draws on the frame, and is stamped with the
    /// time it arrived. A frame not taken before the next one arrives is replaced by it.
    /// </summary>
    public sealed class CapturedFrames : IDisposable
    {
        public sealed class Frame
        {
            public byte[] Pixels;
            public int Width;
            public int Height;
            public SurfaceFormat Format;
            public DateTime Time;
        }

        // Per source: the frame waiting to be taken, and a spare for the next copy, so a
        // source never holds more than three frames (waiting, spare and one being detected on).
        private sealed class Slot
        {
            public Frame Waiting;
            public Frame Spare;
        }

        private readonly Dictionary<FrameSource, Slot> slots;
        private readonly AutoResetEvent arrived;
        private bool disposed;

        public CapturedFrames()
        {
            slots = new Dictionary<FrameSource, Slot>();
            arrived = new AutoResetEvent(false);
            FrameSource.FrameCaptured += OnFrameCaptured;
        }

        public void Dispose()
        {
            FrameSource.FrameCaptured -= OnFrameCaptured;
            lock (slots)
            {
                slots.Clear();
                disposed = true;
            }
            arrived.Dispose();
        }

        /// <summary>Copies frames of these sources from now on, and stops copying any others.</summary>
        public void Watch(IEnumerable<FrameSource> sources)
        {
            lock (slots)
            {
                var wanted = new HashSet<FrameSource>(sources.Where(s => s != null));
                foreach (FrameSource gone in slots.Keys.Where(s => !wanted.Contains(s)).ToArray())
                    slots.Remove(gone);
                foreach (FrameSource source in wanted)
                {
                    if (!slots.ContainsKey(source))
                        slots.Add(source, new Slot());
                }
            }
        }

        /// <summary>Waits until a watched source has delivered a frame, or the timeout passes.</summary>
        public bool Wait(int timeoutMs)
        {
            return arrived.WaitOne(timeoutMs);
        }

        /// <summary>The waiting frame of each source that has one. Give them back with <see cref="Return"/>.</summary>
        public Dictionary<FrameSource, Frame> Take()
        {
            var frames = new Dictionary<FrameSource, Frame>();
            lock (slots)
            {
                foreach (var pair in slots)
                {
                    if (pair.Value.Waiting == null) continue;
                    frames.Add(pair.Key, pair.Value.Waiting);
                    pair.Value.Waiting = null;
                }
            }
            return frames;
        }

        /// <summary>Hands taken frames back, to be copied into again.</summary>
        public void Return(Dictionary<FrameSource, Frame> frames)
        {
            lock (slots)
            {
                foreach (var pair in frames)
                {
                    if (slots.TryGetValue(pair.Key, out Slot slot) && slot.Spare == null)
                        slot.Spare = pair.Value;
                }
            }
        }

        private void OnFrameCaptured(FrameSource source, IntPtr pixels, int length)
        {
            DateTime time = DateTime.Now;
            int width = source.FrameWidth;
            int height = source.FrameHeight;
            int size = width * height * 4;
            if (pixels == IntPtr.Zero || size <= 0 || length < size)
                return;

            Frame frame;
            lock (slots)
            {
                if (disposed || !slots.TryGetValue(source, out Slot slot))
                    return;
                frame = slot.Spare ?? new Frame();
                slot.Spare = null;
            }

            if (frame.Pixels == null || frame.Pixels.Length != size)
                frame.Pixels = new byte[size];
            Marshal.Copy(pixels, frame.Pixels, 0, size);
            frame.Width = width;
            frame.Height = height;
            frame.Format = source.FrameFormat;
            frame.Time = time;

            lock (slots)
            {
                if (disposed || !slots.TryGetValue(source, out Slot slot))
                    return;
                if (slot.Waiting != null && slot.Spare == null)
                    slot.Spare = slot.Waiting;
                slot.Waiting = frame;

                // Under the lock, so Dispose cannot close the event in between.
                arrived.Set();
            }
        }
    }
}
