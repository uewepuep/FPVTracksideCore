using Composition;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using Tools;

namespace UI.Nodes.Aruco
{
    // The visible half of ArucoWaveformGraph's time zoom. The wheel and a right-drag do the same
    // job, but nothing on screen said so or showed how much of the race was off to the sides.
    //
    // Behaves as a range slider rather than a plain scrollbar: dragging the middle of the thumb
    // pans, dragging either end moves that end alone, which is what zooms. The whole track is
    // the race; the thumb is what the graph above is showing.
    public class WaveformRangeBarNode : Node
    {
        /// <summary>Fired with the new window (seconds) while the user drags.</summary>
        public event Action<float, float> RangeChanged;

        private readonly ColorNode track;
        private readonly ColorNode thumb;

        private float fullMin;
        private float fullMax;
        private float viewMin;
        private float viewMax;

        private enum Grab { None, Pan, LeftEdge, RightEdge }

        private Grab grab;
        private float grabOffsetSeconds;

        /// <summary>Pixels at each end of the thumb that resize instead of pan.</summary>
        private const int EdgeGrip = 7;

        /// <summary>A thumb narrower than this is impossible to grab, so it stops shrinking.</summary>
        private const int MinThumbPixels = 3 * EdgeGrip;

        public WaveformRangeBarNode(Color trackColor, Color thumbColor)
        {
            track = new ColorNode(trackColor);
            track.RelativeBounds = new RectangleF(0, 0.25f, 1, 0.5f);
            AddChild(track);

            thumb = new ColorNode(thumbColor);
            AddChild(thumb);
        }

        /// <summary>
        /// Point the bar at the graph's current state. Cheap enough to call every frame, which
        /// is how a wheel zoom on the graph gets reflected here without an extra event.
        /// </summary>
        public void SetRange(float fullMin, float fullMax, float viewMin, float viewMax)
        {
            this.fullMin = fullMin;
            this.fullMax = fullMax;
            this.viewMin = viewMin;
            this.viewMax = viewMax;
        }

        private float FullSpan => fullMax - fullMin;

        private float SecondsPerPixel => Bounds.Width > 0 ? FullSpan / Bounds.Width : 0;

        private float TimeAt(int x)
        {
            return Bounds.Width > 0
                ? fullMin + (x - Bounds.X) * SecondsPerPixel
                : fullMin;
        }

        private int PixelAt(float seconds)
        {
            return FullSpan > 0
                ? Bounds.X + (int)((seconds - fullMin) / FullSpan * Bounds.Width)
                : Bounds.X;
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (FullSpan <= 0 || Bounds.Width == 0 || Bounds.Height == 0)
                return;

            int left = PixelAt(viewMin);
            int right = PixelAt(viewMax);

            // A window that is a sliver of a long race still needs something to grab.
            if (right - left < MinThumbPixels)
            {
                right = left + MinThumbPixels;
                if (right > Bounds.Right)
                {
                    right = Bounds.Right;
                    left = right - MinThumbPixels;
                }
            }

            thumb.BoundsF = new RectangleF(left, Bounds.Y, right - left, Bounds.Height);

            base.Draw(id, parentAlpha);
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (FullSpan <= 0)
                return base.OnMouseInput(mouseInputEvent);

            if (mouseInputEvent.Button == MouseButtons.Left)
            {
                if (mouseInputEvent.ButtonState == ButtonStates.Pressed && Contains(mouseInputEvent.Position))
                {
                    StartGrab(mouseInputEvent.Position);
                    return true;
                }

                if (mouseInputEvent.ButtonState == ButtonStates.Released)
                {
                    grab = Grab.None;
                }
            }

            if (grab != Grab.None)
            {
                if (Mouse.GetState().LeftButton == ButtonState.Pressed)
                {
                    Drag(mouseInputEvent.Position);
                    return true;
                }

                grab = Grab.None;
            }

            return base.OnMouseInput(mouseInputEvent);
        }

        private void StartGrab(Point position)
        {
            int left = PixelAt(viewMin);
            int right = PixelAt(viewMax);
            int x = position.X;

            if (Math.Abs(x - left) <= EdgeGrip)
            {
                grab = Grab.LeftEdge;
            }
            else if (Math.Abs(x - right) <= EdgeGrip)
            {
                grab = Grab.RightEdge;
            }
            else if (x > left && x < right)
            {
                grab = Grab.Pan;
                grabOffsetSeconds = TimeAt(x) - viewMin;
            }
            else
            {
                // Clicking bare track jumps the window to there and keeps panning from it, so a
                // single drag from anywhere on the bar goes where the user pointed.
                grab = Grab.Pan;
                grabOffsetSeconds = (viewMax - viewMin) / 2;
                Drag(position);
            }
        }

        private void Drag(Point position)
        {
            float time = TimeAt(position.X);

            switch (grab)
            {
                case Grab.Pan:
                    {
                        float width = viewMax - viewMin;
                        float min = Math.Clamp(time - grabOffsetSeconds, fullMin, fullMax - width);
                        Raise(min, min + width);
                        break;
                    }

                case Grab.LeftEdge:
                    Raise(Math.Clamp(time, fullMin, viewMax), viewMax);
                    break;

                case Grab.RightEdge:
                    Raise(viewMin, Math.Clamp(time, viewMin, fullMax));
                    break;
            }
        }

        // The graph owns the clamping (minimum window, never past either end), so hand it the
        // raw request and let SetRange come back with whatever it settled on.
        private void Raise(float min, float max)
        {
            RangeChanged?.Invoke(min, max);
        }
    }
}
