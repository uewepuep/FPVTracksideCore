using System.Collections.Generic;
using Timing.Aruco;
using Xunit;

namespace Timing.Tests.Aruco
{
    public class ChannelDetectorsTests
    {
        private const int Width = 4;
        private const int Height = 2;

        /// <summary>Reports one marker with a fixed id per frame and counts calls.</summary>
        private class StubDetector : IMarkerDetector
        {
            private readonly int id;
            public int Frames, Resets, Disposes;
            public DetectionFrame LastFrame;

            public StubDetector(int id) { this.id = id; }

            public void Detect(DetectionFrame frame, MarkerDetectorOptions options, List<MarkerDetection> into)
            {
                Frames++;
                LastFrame = frame;
                into.Add(new MarkerDetection { Id = id });
            }

            public void Reset() { Resets++; }
            public void Dispose() { Disposes++; }
        }

        private static byte[] Frame() => new byte[Width * Height * 4];

        [Fact]
        public void Detect_RunsEveryDetectorOnOneFrame_InOrder()
        {
            var a = new StubDetector(1);
            var b = new StubDetector(2);
            var channel = new ChannelDetectors(null, new IMarkerDetector[] { a, b });

            var found = channel.Detect(Frame(), Width, Height, new MarkerDetectorOptions());

            Assert.Equal(new[] { 1, 2 }, found.ConvertAll(d => d.Id));
            Assert.Same(a.LastFrame, b.LastFrame);
            Assert.False(a.LastFrame.CanCorrect);
        }

        [Fact]
        public void SetFeed_ResetsOnlyWhenTheFeedChanges()
        {
            var a = new StubDetector(1);
            var channel = new ChannelDetectors(null, new IMarkerDetector[] { a });
            object source1 = new object(), source2 = new object();

            channel.SetFeed(source1, 5800);   // first feed: nothing to forget yet
            channel.SetFeed(source1, 5800);
            Assert.Equal(0, a.Resets);

            channel.SetFeed(source1, 5740);   // frequency changed
            Assert.Equal(1, a.Resets);

            channel.SetFeed(source2, 5740);   // video source changed
            Assert.Equal(2, a.Resets);
        }

        [Fact]
        public void Dispose_DisposesEveryDetector()
        {
            var a = new StubDetector(1);
            var b = new StubDetector(2);
            var channel = new ChannelDetectors(null, new IMarkerDetector[] { a, b });

            channel.Dispose();

            Assert.Equal(1, a.Disposes);
            Assert.Equal(1, b.Disposes);
        }
    }
}
