using ImageServer;
using MediaFoundation;
using MediaFoundation.Misc;
using MediaFoundation.ReadWrite;
using MediaFoundation.Transform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsMediaPlatform.MediaFoundation
{
    public class MediaFoundationNetFrameSource : MediaFoundationFrameSource
    {
        private IMFMediaSource source;
        private IMFPresentationDescriptor presentationDescriptor;

        public string URL { get; private set; }

        public FrameTime[] FrameTimes { get; set; }


        public MediaFoundationNetFrameSource(VideoConfig videoConfig)
            : base(videoConfig)
        {
            AutomaticVideoConversion = true;

            Tools.Logger.VideoLog.LogCall(this, videoConfig.FilePath);
            URL = videoConfig.URL;
            FrameTimes = videoConfig.FrameTimes;
        }

        private void CreateMediaSource(string sURL)
        {
            IMFSourceResolver sourceResolver;
            object tempSource;

            // Create the source resolver.
            HResult hr = MFExtern.MFCreateSourceResolver(out sourceResolver);
            MFError.ThrowExceptionForHR(hr);

            try
            {
                // Use the source resolver to create the media source.
                MFObjectType ObjectType = MFObjectType.Invalid;

                hr = sourceResolver.CreateObjectFromURL(
                        sURL,                       // URL of the source.
                        MFResolution.MediaSource,   // Create a source object.
                        null,                       // Optional property store.
                        out ObjectType,             // Receives the created object type.
                        out tempSource                 // Receives a pointer to the media source.
                    );
                MFError.ThrowExceptionForHR(hr);

                // Get the IMFMediaSource interface from the media source.
                source = (IMFMediaSource)tempSource;

                hr = source.CreatePresentationDescriptor(out presentationDescriptor);
                MFError.ThrowExceptionForHR(hr);

                CreateReader(source);
            }
            finally
            {
                // Clean up
                MFHelper.SafeRelease(sourceResolver);
            }
        }

        public override void CleanUp()
        {
            base.CleanUp();

            if (source != null)
            {
                MFHelper.SafeRelease(source);
                source = null;
            }
        }

        public override bool Start()
        {
            if (string.IsNullOrEmpty(URL))
                return false;

            if (source == null)
            {
                CreateMediaSource(URL);

                NotifyReceivedFrame();
            }
            return base.Start();
        }

        public override IEnumerable<Mode> GetModes()
        {
            yield break;
        }
    }
}
