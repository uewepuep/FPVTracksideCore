using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Tools
{
    public class Spline3D: Curve3D
    {
        private Vector3 lastPoint = Vector3.Zero;

        public Spline3D(): base()
        {            
        }

        public void AddPoint(Vector3 point)
        {
            float length = 0;
            // calculate distance from the previous point.
            if (Keys.Count > 0)
            {
                length = (point - lastPoint).Length() + Length;
            }
            lastPoint = point;

            AddSafe(length, point);
        }

        public float GetDistance(Vector3 point)
        {
            Vector3 bestDistance = new Vector3(10000);
            float best = float.MaxValue;

            for (float f = 0; f < Length; f += 0.1f)
            {
                Vector3 check = Evaluate(f);
                Vector3 distance = point - check;

                if (distance.LengthSquared() < bestDistance.LengthSquared())
                {
                    bestDistance = distance;
                    best = f;
                }
            }

            return best;
        }

        public void DebugDraw(GraphicsDevice graphicsDevice, int resolution = 5)
        {
            int pointCount = (int)(Length / resolution);

            VertexPositionColor[] vertices = new VertexPositionColor[pointCount];
            for (int i = 0; i < vertices.Length; i++)
            {
                VertexPositionColor vertex = new VertexPositionColor();
                vertex.Position = Evaluate(resolution * i);
                vertex.Color = Color.Red;
                vertices[i] = vertex;
            }


            Int16[] lineIndices = new Int16[(vertices.Length - 1) * 2];
            for (Int16 i = 0; i < vertices.Length - 1; i++)
            {
                int index = (i * 2);
                lineIndices[index] = i;
                lineIndices[index + 1] = (Int16)(i + 1);
            }

            graphicsDevice.DrawUserIndexedPrimitives<VertexPositionColor>(PrimitiveType.LineList, vertices, 0, vertices.Length, lineIndices, 0, lineIndices.Length / 2, VertexPositionColor.VertexDeclaration);
        }

        public void AddPoints(Vector3[] points)
        {
            foreach (Vector3 point in points)
            {
                AddPoint(point);    
            }
        }
    }
}
