using SharpDX.Direct3D11;
using System;
using Tools;

namespace WindowsMediaPlatform.MediaFoundation
{
    // Wraps the D3D11 Video Processor API for GPU-native colour conversion.
    // Converts NV12/YUY2 textures to B8G8R8X8 via VideoProcessorBlt into an internal
    // RenderTarget texture, then CopyResource to the caller's output texture.
    // The internal blit target owns the BindFlags.RenderTarget requirement, so the
    // output texture can be a plain XNA Texture2D (ShaderResource only).
    public class D3D11VideoProcessor : IDisposable
    {
        private VideoDevice videoDevice;
        private VideoContext videoContext;
        private DeviceContext deviceContext;
        private VideoProcessorEnumerator enumerator;
        private VideoProcessor processor;

        // Internal VP output surface: must have BindFlags.RenderTarget.
        // After each blit, CopyResource transfers it to the caller's texture.
        private Texture2D blitTarget;
        private VideoProcessorOutputView blitTargetView;

        public D3D11VideoProcessor(Device device, int width, int height)
        {
            videoDevice = device.QueryInterface<VideoDevice>();
            videoContext = device.ImmediateContext.QueryInterface<VideoContext>();
            deviceContext = device.ImmediateContext;

            VideoProcessorContentDescription contentDesc = new VideoProcessorContentDescription
            {
                InputFrameFormat = VideoFrameFormat.Progressive,
                InputWidth = width,
                InputHeight = height,
                OutputWidth = width,
                OutputHeight = height,
                Usage = VideoUsage.PlaybackNormal
            };

            videoDevice.CreateVideoProcessorEnumerator(ref contentDesc, out enumerator);
            videoDevice.CreateVideoProcessor(enumerator, 0, out processor);

            blitTarget = new Texture2D(device, new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = SharpDX.DXGI.Format.B8G8R8X8_UNorm,
                SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None
            });

            VideoProcessorOutputViewDescription ovDesc = new VideoProcessorOutputViewDescription
            {
                Dimension = VpovDimension.Texture2D,
                Texture2D = new Texture2DVpov { MipSlice = 0 }
            };
            videoDevice.CreateVideoProcessorOutputView(blitTarget, enumerator, ovDesc, out blitTargetView);

            Logger.VideoLog.Log(this, "Created D3D11 video processor " + width + "x" + height);
        }

        // Converts inputTexture (NV12/YUY2, at subresource index) to outputTexture (B8G8R8X8).
        // VP blits to the internal RenderTarget, then CopyResource to outputTexture.
        // outputTexture must be B8G8R8X8_UNorm, Default usage — no special bind flags needed.
        public void Process(Texture2D inputTexture, int subresource, Texture2D outputTexture)
        {
            VideoProcessorInputViewDescription ivDesc = new VideoProcessorInputViewDescription
            {
                FourCC = 0,
                Dimension = VpivDimension.Texture2D,
                Texture2D = new Texture2DVpiv
                {
                    MipSlice = 0,
                    ArraySlice = subresource
                }
            };

            VideoProcessorInputView inputView;
            videoDevice.CreateVideoProcessorInputView(inputTexture, enumerator, ivDesc, out inputView);

            try
            {
                VideoProcessorStream stream = new VideoProcessorStream
                {
                    Enable = true,
                    PInputSurface = inputView.NativePointer
                };

                videoContext.VideoProcessorBlt(processor, blitTargetView, 0, 1, new[] { stream });
                // CopyResource(destination, source): blitTarget → outputTexture
                deviceContext.CopyResource(outputTexture, blitTarget);
            }
            finally
            {
                inputView.Dispose();
            }
        }

        public void Dispose()
        {
            blitTargetView?.Dispose();
            blitTarget?.Dispose();
            processor?.Dispose();
            enumerator?.Dispose();
            videoContext?.Dispose();
            videoDevice?.Dispose();
        }
    }
}
