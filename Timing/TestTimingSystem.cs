using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timing
{
    public class TestTimingSystem : ITimingSystem
    {
        public TimingSystemType Type => TimingSystemType.Test;

        public bool Connected => true;

        public TimingSystemSettings Settings { get; set; }

        public event DetectionEventDelegate OnDetectionEvent;
        public int MaxPilots { get { return int.MaxValue; } }
        public IEnumerable<StatusItem> Status { get { return new StatusItem[0]; } }

        public string Name
        {
            get
            {
                return "Test";
            }
        }

        public bool Connect()
        {
            return true;
        }

        public bool Disconnect()
        {
            return true;
        }

        public void Dispose()
        {
        }

        public bool EndDetection()
        {
            return true;
        }

        public bool SetListeningFrequencies(IEnumerable<ListeningFrequency> newFrequencies)
        {
            return true;
        }

        public bool StartDetection(ref DateTime time, Guid raceId)
        {
            return true;
        }

        public bool StartScanner()
        {
            return true;
        }

        public bool StopScanner()
        {
            return true;
        }

        public void Trigger(int frequency, DateTime time)
        {
            OnDetectionEvent?.Invoke(this, frequency, time, 800);
        }

        public bool GetVoltage(out float voltage)
        {
            voltage = 0.0f;
            return true;
        }
    }
}
