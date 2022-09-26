using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Widgets
{
    public class TelemetryPacket
    {
        public float Throttle { get; set; }
        public int DroneNumber { get; set; }
        public Vector3 GeForce { get; set; }
        public int Speed { get; set; }
        public float Voltage { get; set; }
        public float Current { get; set; }

        public TelemetryPacket()
        {
        }

        private static Random r;

        public static TelemetryPacket GenerateRandom()
        {
            if (r == null)
            {
                r = new Random();
            }
            TelemetryPacket t = new TelemetryPacket();
            t.Throttle = r.Next(0, 100) / 100.0f;
            t.DroneNumber = 1;
            t.GeForce = new Vector3(0, r.Next(0, 20), 0);
            t.Speed = r.Next(0, 200);
            t.Voltage = r.Next(370, 420) / 100.0f;
            t.Current = r.Next(0, 200);
            return t;
        }
    }
}
