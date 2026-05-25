using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;

namespace Composition.Nodes
{
    public class GraphNode : Node
    {
        private List<GraphSeries> series;

        private Dictionary<float, TextNode> xLabelNodes;
        private Dictionary<float, TextNode> yLabelNodes;

        public RectangleF View { get; set; }

        public GraphNode()
        {
            series = new List<GraphSeries>();
            xLabelNodes = new Dictionary<float, TextNode>();
            yLabelNodes = new Dictionary<float, TextNode>();
        }

        public void Clear()
        {
            lock (series)
            {
                series.Clear();
            }

            lock (xLabelNodes)
            {
                foreach (TextNode n in xLabelNodes.Values)
                {
                    RemoveChild(n);
                    n.Dispose();
                }
                xLabelNodes.Clear();
            }

            lock (yLabelNodes)
            {
                foreach (TextNode n in yLabelNodes.Values)
                {
                    RemoveChild(n);
                    n.Dispose();
                }
                yLabelNodes.Clear();
            }
        }

        public GraphSeries GetCreateSeries(string name, Color color)
        {
            lock (series)
            {
                GraphSeries got = series.FirstOrDefault(s => s.Name == name);
                if (got == null)
                {
                    got = new GraphSeries(this, name, color);
                    series.Add(got);
                }
                return got;
            }
        }

        public Vector2 ToPixel(Vector2 v)
        {
            Vector2 v2 = new Vector2(v.X - View.X, v.Y - View.Y);
            v2.X = (v2.X / View.Width) * Bounds.Width;
            v2.X += Bounds.X;

            v2.Y = (v2.Y / View.Height) * Bounds.Height;
            v2.Y += Bounds.Y;

            return v2;
        }

        private void DrawGrid(Drawer id)
        {
            Color color = Color.Gray;

            lock (xLabelNodes)
            {
                foreach (float value in xLabelNodes.Keys)
                {
                    float px = ToPixel(new Vector2(value)).X;
                    id.DrawLine(new Vector2(px, Bounds.Top), new Vector2(px, Bounds.Bottom), color);
                }
            }

            lock (yLabelNodes)
            {
                foreach (float value in yLabelNodes.Keys)
                {
                    float py = ToPixel(new Vector2(value)).Y;
                    id.DrawLine(new Vector2(Bounds.Left, py), new Vector2(Bounds.Right, py), color);
                }
            }
        }

        public void AddXLabel(float value, string label)
        {
            TextNode node = new TextNode(label, Color.White);
            node.Alignment = RectangleAlignment.TopCenter;
            AddChild(node);
            lock (xLabelNodes)
            {
                xLabelNodes[value] = node;
            }
        }

        public void AddYLabel(float value, string label)
        {
            TextNode node = new TextNode(label, Color.White);
            node.Alignment = RectangleAlignment.CenterLeft;
            AddChild(node);
            lock (yLabelNodes)
            {
                yLabelNodes[value] = node;
            }
        }

        private void PositionLabelNodes()
        {
            if (Bounds.Height == 0 || Bounds.Width == 0 || View.Width == 0 || View.Height == 0)
                return;

            float labelHeight = Bounds.Height * 0.08f;
            float labelWidth = Bounds.Width * 0.18f;

            lock (xLabelNodes)
            {
                foreach (KeyValuePair<float, TextNode> kvp in xLabelNodes)
                {
                    float px = ToPixel(new Vector2(kvp.Key, 0)).X;
                    kvp.Value.BoundsF = new RectangleF(px - labelWidth / 2, Bounds.Bottom - labelHeight, labelWidth, labelHeight);
                }
            }

            lock (yLabelNodes)
            {
                foreach (KeyValuePair<float, TextNode> kvp in yLabelNodes)
                {
                    float py = ToPixel(new Vector2(0, kvp.Key)).Y;
                    kvp.Value.BoundsF = new RectangleF(Bounds.Left, py - labelHeight / 2, labelWidth, labelHeight);
                }
            }
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            PositionLabelNodes();

            DrawGrid(id);

            lock (series)
            {
                foreach (GraphSeries s in series)
                {
                    s.Draw(id);
                }
            }

            base.Draw(id, parentAlpha);
        }
    }

    public class GraphSeries
    {
        public Color Color { get; private set; }

        public string Name { get; set; }

        public float Thickness { get; set; }

        public IEnumerable<Vector2> Points { get { return points; } }

        private List<Vector2> points;

        private Texture2D texture;

        private GraphNode graph;

        public GraphSeries(GraphNode graph, string name, Color color)
        {
            this.graph = graph;
            points = new List<Vector2>();
            Name = name;
            Color = color;
            Thickness = 2.5f;
        }

        public void Clear()
        {
            lock (points)
            {
                points.Clear();
            }
        }

        public void AddPoint(float x, float y)
        {
            AddPoint(new Vector2(x, y));
        }

        public void AddPoint(Vector2 point)
        {
            lock (points)
            {
                points.Add(point);
            }
        }

        public void Draw(Drawer id)
        {
            if (texture == null)
            {
                texture = id.TextureCache.GetTextureFromColor(Color);
            }

            if (!Points.Any())
                return;

            lock (points)
            {
                Vector2 last = graph.ToPixel(Points.First());
                foreach (Vector2 point in Points.Skip(1))
                {
                    Vector2 p = graph.ToPixel(point);

                    id.DrawLine(last, p, texture, Thickness);
                    last = p;
                }
            }
        }
    }
}
