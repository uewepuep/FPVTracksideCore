using Composition.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using Microsoft.Xna.Framework;

namespace UI.Widgets
{
    public class ThrottleBatteryWidget : Widget
    {
        private GaugeNode throttle;
        private GaugeNode batteryCapacity;


        private float lowVoltage = 3.0f;
        private float highVoltage = 4.2f;

        public ThrottleBatteryWidget()
        {
            Node gaugeContainer = new Node();
            gaugeContainer.RelativeBounds = new RectangleF(0.7f, 0, 0.3f, 1);
            AddChild(gaugeContainer);

            throttle = new GaugeNode("img/throttle.png", Color.Orange, Color.Black);
            throttle.RelativeBounds = new RectangleF(0, 0, 1, 0.5f);
            gaugeContainer.AddChild(throttle);

            batteryCapacity = new GaugeNode("img/throttle.png", Color.Purple, Color.Black);
            batteryCapacity.RelativeBounds = new RectangleF(0, 0.5f, 1, 0.5f);
            gaugeContainer.AddChild(batteryCapacity);

            Node textContainer = new Node();
            textContainer.RelativeBounds = new RectangleF(0.6f, 0, 0.1f, 1);
            AddChild(textContainer);

            TextNode throttleText = new TextNode("Throttle", Theme.Current.PilotViewTheme.PilotOverlayText.XNA);
            throttleText.RelativeBounds = new RectangleF(0, 0, 1, 0.5f);
            throttleText.Alignment = RectangleAlignment.CenterRight;
            throttleText.Style.Italic = true;
            throttleText.Style.Border = true;
            textContainer.AddChild(throttleText);

            TextNode batteryText = new TextNode("Battery", Theme.Current.PilotViewTheme.PilotOverlayText.XNA);
            batteryText.RelativeBounds = new RectangleF(0, 0.5f, 1, 0.5f);
            batteryText.Alignment = RectangleAlignment.CenterRight;
            batteryText.Style.Border = true;
            batteryText.Style.Italic = true;
            textContainer.AddChild(batteryText);
        }

        public void SetThrottle(float factor)
        {
            throttle.SetValue(factor);
        }

        public void SetVoltage(float voltage)
        {
            float factor = (voltage - lowVoltage) / (highVoltage - lowVoltage);
            batteryCapacity.SetValue(factor);
        }
    }
}
