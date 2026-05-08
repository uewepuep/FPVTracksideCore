using ImageServer;
using MediaFoundation;
using MediaFoundation.Misc;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;
using WindowsMediaPlatform.DirectShow;

namespace WindowsMediaPlatform.MediaFoundation
{
    // Captures frames via DirectShow (for DS-only devices such as OBS Virtual Camera),
    // but records through an IMFSinkWriter with MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS
    // so the pipeline uses NVENC/QSV/AMF rather than GMFBridge/WMV.
    //
    // Display path:  DS filter graph → color space converter → RGB32 → rawTextures (base class)
    // Recording path: BufferCB RGB32 → IMFSample → IMFSinkWriter → NVENC → H264/mp4
    public class MediaFoundationDSCaptureFrameSource : DirectShowDeviceFrameSource, ICaptureFrameSource
    {
        private MFSinkWriterRecorder recorder;

        public string Filename => recorder.Filename;
        public bool RecordNextFrameTime { get => recorder.RecordNextFrameTime; set => recorder.RecordNextFrameTime = value; }
        public bool ManualRecording { get => recorder.ManualRecording; set => recorder.ManualRecording = value; }
        public bool Finalising => recorder.Finalising;
        public MFSinkWriterRecorder.FileFormats FileFormat { get => recorder.FileFormat; set => recorder.FileFormat = value; }
        public FrameTime[] FrameTimes => recorder.FrameTimes;

        public MediaFoundationDSCaptureFrameSource(VideoConfig videoConfig)
            : base(videoConfig)
        {
            recorder = new MFSinkWriterRecorder(videoConfig);

            HResult hr = MFExtern.MFStartup(0x20070, MFStartup.Full);
            MFError.ThrowExceptionForHR(hr);
        }

        public override IEnumerable<Mode> GetModes()
        {
            return base.GetModes().Select(m => new Mode
            {
                Width = m.Width,
                Height = m.Height,
                FrameRate = m.FrameRate,
                Format = m.Format,
                Index = m.Index,
                FrameWork = FrameWork.MediaFoundation
            });
        }

        public void StartRecording(string filename)
        {
            recorder.StartRecording(filename, FrameWidth, FrameHeight);
            Recording = recorder.Recording;
        }

        public void StopRecording()
        {
            recorder.StopRecording();
            Recording = recorder.Recording;
        }

        public override int BufferCB(double sampleTime, IntPtr buffer, int bufferLen)
        {
            int result = base.BufferCB(sampleTime, buffer, bufferLen);
            recorder.WriteSampleFromBuffer(sampleTime, buffer, bufferLen);
            return result;
        }

        public override void CleanUp()
        {
            recorder.CleanUp();
            Recording = recorder.Recording;
            base.CleanUp();
        }
    }
}
