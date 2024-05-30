using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Tools
{
    public class Curve3D
    {
        private Curve x;
        private Curve y;
        private Curve z;

        public bool HasComputedTangents { get; private set; }

        public float Length { get; private set; }

        private Dictionary<float, CurveKey3D> keyMap;

        public Dictionary<float, CurveKey3D>.ValueCollection Keys { get { return keyMap.Values; } }

        public Curve3D()
        {
            x = new Curve();
            y = new Curve();
            z = new Curve();
            x.PreLoop = CurveLoopType.Linear;
            y.PreLoop = CurveLoopType.Linear;
            z.PreLoop = CurveLoopType.Linear;
            x.PostLoop = CurveLoopType.Linear;
            y.PostLoop = CurveLoopType.Linear;
            z.PostLoop = CurveLoopType.Linear;
            Length = 0;
            HasComputedTangents = false;
            keyMap = new Dictionary<float, CurveKey3D>();
        }

        public void AddSafe(float position, Vector3 value)
        {
            CurveKey3D curveKey3D;
            if (keyMap.TryGetValue(position, out curveKey3D))
            {
                curveKey3D.Value = value;
            }
            else
            {
                curveKey3D = new CurveKey3D(position, value);
                keyMap.Add(position, curveKey3D);

                x.Keys.Add(curveKey3D.X);
                y.Keys.Add(curveKey3D.Y);
                z.Keys.Add(curveKey3D.Z);
            }

            Length = Math.Max(Length, position);
            HasComputedTangents = false;
        }

        public Vector3 GetTangent(float position, float nextPointIn = 0.001f)
        {
            Vector3 point = Evaluate(position);
            Vector3 nextPoint = Evaluate(position + nextPointIn);

            // If we're at the end, go backwards rather than forwards.
            if ((nextPoint - point).Length() == 0)
            {
                nextPoint = Evaluate(position - nextPointIn);
                return Vector3.Normalize(point - nextPoint);
            }

            return Vector3.Normalize(nextPoint - point);
        }

        public Vector3 Evaluate(float position)
        {
            if (!HasComputedTangents)
            {
                throw new Exception("Must call ComputeTangents before getting position from path");
            }

            Vector3 value = new Vector3();

            value.X = x.Evaluate(position);
            value.Y = y.Evaluate(position);
            value.Z = z.Evaluate(position);

            return value;
        }

        public void ComputeTangents(CurveTangent curveTangent = CurveTangent.Smooth)
        {
            HasComputedTangents = true;
            x.ComputeTangents(curveTangent);
            y.ComputeTangents(curveTangent);
            z.ComputeTangents(curveTangent);
        }

    }

    public class CurveKey3D
    {
        public CurveKey X { get; private set; }
        public CurveKey Y { get; private set; }
        public CurveKey Z { get; private set; }

        public float Position { get { return X.Position; } }
        public Vector3 Value
        {
            get
            {
                return new Vector3(X.Value, Y.Value, Z.Value);
            }
            set
            {
                X.Value = value.X;
                Y.Value = value.Y;
                Z.Value = value.Z;
            }
        }

        public CurveKey3D(float position, Vector3 value)
        {
            X = new CurveKey(position, value.X);
            Y = new CurveKey(position, value.Y);
            Z = new CurveKey(position, value.Z);
        }
    }
}
