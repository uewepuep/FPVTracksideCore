using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections;
using System.Collections.Generic;
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

        public TrackFlightPath(Track track)
            :this()
        {
            if (track == null)
                return;

            SetTrackElements(track.TrackElements);
        }

        public TrackFlightPath()
        {
            Spline = new Spline3D();
            Sectors = new Sector[0];
        }

        public void SetTrackElements(IEnumerable<TrackElement> trackElements)
        {
            trackElements = trackElements.Where(e => e != null && !e.Decorative);

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
                if (i > 0)
                    start = GetDistance(sector.Start.Position);

                float end = Length;
                if (i < Sectors.Length - 1)
                {
                    end = GetDistance(sector.End.Position);
                }

                sector.Length = end - start;

                sector.Color = sectorColors[count];

                count = (count + 1) % sectorColors.Length;
            }
        }

        private IEnumerable<Sector> CreateSectors(IEnumerable<TrackElement> trackElements)
        {
            if (trackElements == null || !trackElements.Any())
                yield break;

            TrackElement start = trackElements.First();

            Sector current = new Sector();
            current.Start = start;
            current.Number = 1;

            foreach (TrackElement sectorEnd in trackElements.Where(g => g.SplitEnd))
            {
                int number = current.Number;
                current.End = sectorEnd;
                yield return current;
                current = new Sector();
                current.Number = number + 1;
                current.Start = sectorEnd;
            }

            current.End = start;
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
    }

    public class Sector
    {
        public TrackElement Start { get; set; }
        public TrackElement End { get; set; }

        public float Length { get; set; }

        public Color Color { get; set; }

        public int Number { get; set; }

        public Sector()
        {
        }

        public string ToString(Units units)
        {
            return "S" + Number + " " + LengthHuman(units);
        }

        public string LengthHuman(Units units)
        {
            return LengthHuman(units, Length);
        }

        public static string LengthHuman(Units units, float length)
        {
            if (units == RaceLib.Units.Imperial)
            {
                length = length * 3.28f;

                return length.ToString("0.0") + "ft";
            }
            else
            {
                return length.ToString("0.0") + "m";
            }
        }
    }
}
