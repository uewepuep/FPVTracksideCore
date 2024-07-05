using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace RaceLib
{
    public class TrackFlightPath
    {
        public Spline3D Spline { get; private set; }
        public float Length { get { return Spline.Length; } }

        public Sector[] Sectors { get; private set; }

        public Track Track { get; private set; }

        public TrackFlightPath(Track track)
            :this()
        {
            if (track == null)
                return;

            Track = track;

            SetTrackElements(track.TrackElements);
        }

        public TrackFlightPath()
        {
            Spline = new Spline3D();
            Sectors = new Sector[0];
        }

        public void SetTrackElements(IEnumerable<TrackElement> trackElementsA)
        {
            TrackElement[] trackElements = trackElementsA.Where(e => e != null && !e.Decorative).ToArray();

            Spline = new Spline3D();

            if (!trackElements.Any())
            {
                return;
            }

            foreach (TrackElement trackElement in trackElements)
            {
                Spline.AddPoints(trackElement.GetFlightPath().ToArray());
            }

            // Loop
            Spline.AddPoint(trackElements.First().GetFlightPath().First());

            Spline.ComputeTangents();

            Color[] sectorColors = new Color[]
            {
                Color.Cyan,
                Color.Magenta,
                Color.Yellow
            };

            int count = 0;

            Sectors = CreateSectors(trackElements).ToArray();
            for (int i = 0; i < Sectors.Length; i++)
            {
                Sector sector = Sectors[i];

                float start = 0;
                if (i > 0 && trackElements.Length > sector.TrackElementStartIndex)
                {
                    TrackElement startElement = trackElements[sector.TrackElementStartIndex];
                    start = GetDistance(startElement.Position);
                }

                float end = Length;
                if (i < Sectors.Length - 1)
                {
                    TrackElement endElement = trackElements[sector.TrackElementEndIndex];
                    end = GetDistance(endElement.Position);
                }

                sector.Length = end - start;

                sector.Color = sectorColors[count];

                count = (count + 1) % sectorColors.Length;
            }
        }

        private IEnumerable<Sector> CreateSectors(IEnumerable<TrackElement> trackElementsa)
        {
            List<TrackElement> trackElements = trackElementsa.ToList();

            if (trackElements == null || !trackElements.Any())
                yield break;

            TrackElement start = trackElements.First();

            Sector current = new Sector();
            current.TrackElementStartIndex = 0;
            current.Number = 1;

            foreach (TrackElement sectorEnd in trackElements.Where(g => g.SplitEnd))
            {
                int number = current.Number;
                current.TrackElementEndIndex = trackElements.IndexOf(sectorEnd);
                yield return current;
                current = new Sector();
                current.Number = number + 1;
                current.TrackElementStartIndex = trackElements.IndexOf(sectorEnd);
            }

            current.TrackElementEndIndex = 0;
            yield return current;
        }

        public Vector3 GetPoint(float distance)
        {
            if (Spline.Length == 0)
            {
                return Vector3.Zero;
            }

            while (distance < 0)
            {
                distance += Spline.Length;
            }

            distance = distance % Spline.Length;
            return Spline.Evaluate(distance);
        }

        public Vector3 GetTangent(float distance)
        {
            if (Spline.Length == 0)
                return Vector3.Zero;

            distance = distance % Spline.Length;
            return Spline.GetTangent(distance, 0.3f);
        }

        public float GetDistance(Vector3 position)
        {
            return Spline.GetDistance(position);
        }

        public string LengthHuman(RaceLib.Units units)
        {
            return Sector.LengthHuman(units, Length);
        }

        public float EstimatedFlyThroughSpeed(float distance)
        {
            float next = distance + 1f;
            const int minSpeed = 4;

            Vector3 fromTangent = GetTangent(distance);
            Vector3 toTangent = GetTangent(next);

            float dot = Math.Abs(Vector3.Dot(fromTangent, toTangent));
            if (float.IsNaN(dot))
            {
                return minSpeed;
            }

            float lerpedDot = MathHelper.Lerp(dot, dot * dot, 0.5f);
            lerpedDot *= 9;

            return Math.Max(lerpedDot, minSpeed);
        }
    }
}
