using Composition.Nodes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;

namespace UI.Widgets
{
    public class GForceWidget : Widget
    {
        private GaugeNode gauge;

        private List<float> gforces;
        public int SamplesToKeep { get; set; }

        public int MaxGeforce { get; set; }

        private TextNode textNode;

        public GForceWidget()
        {
            gforces = new List<float>();
            SamplesToKeep = 1;
            MaxGeforce = 20;

            textNode = new TextNode("0g", new Color(239, 189, 0));
            textNode.RelativeBounds = new RectangleF(0.3f, 0.3f, 0.6f, 0.6f);
            textNode.Alignment = RectangleAlignment.BottomRight;
            AddChild(textNode);

            gauge = new GaugeNode("img/gforce.png", Color.Yellow, Color.Black);
            gauge.KeepAspectRatio = false;
            AddChild(gauge);
        }

        public void SetValue(Vector3 geForce)
        {
            float length = geForce.Length();
            gforces.Add(length);

            float current = gforces.Max();

            while (gforces.Count > SamplesToKeep)
            {
                gforces.RemoveAt(0);
            }


            gauge.SetValue(current / MaxGeforce);
            textNode.Text = (int)Math.Round(current) + "g";
        }
    }
}
