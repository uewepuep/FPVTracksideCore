using Composition.Nodes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Widgets
{
    public class WidgetManagerNode : Node, IUpdateableNode
    {
        private GForceWidget gForceWidget;
        private SpeedoWidget speedoWidget;
        private ThrottleBatteryWidget throttleVoltage;

        public WidgetManagerNode()
        {
            gForceWidget = new GForceWidget();
            gForceWidget.RelativeBounds = new RectangleF(0.0f, 0.8f, 0.3f, 0.2f);
            AddChild(gForceWidget);

            speedoWidget = new SpeedoWidget();
            speedoWidget.RelativeBounds = new RectangleF(0.7f, 0.65f, 0.3f, 0.2f);
            AddChild(speedoWidget);

            throttleVoltage = new ThrottleBatteryWidget();
            throttleVoltage.RelativeBounds = new RectangleF(0, 0.85f, 1, 0.1f);
            AddChild(throttleVoltage);

            Visible = false;
        }

        private int count;

        public void Update(GameTime gameTime)
        {
            if (count % 20 == 0)
            {
                TelemetryPacket telemetryPacket = TelemetryPacket.GenerateRandom();
                SetTelemetry(telemetryPacket);
            }
            count++;
        }

        public void SetTelemetry(TelemetryPacket telemetryPacket)
        {
            Visible = true;

            gForceWidget.SetValue(telemetryPacket.GeForce);
            speedoWidget.SetSpeedKPH(telemetryPacket.Speed);
            throttleVoltage.SetThrottle(telemetryPacket.Throttle);
            throttleVoltage.SetVoltage(telemetryPacket.Voltage);
        }
    }
}
