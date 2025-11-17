using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using ImageServer;
using Tools;

namespace FfmpegMediaPlatform
{
    /// <summary>
    /// FFmpeg.AutoGen-based recorder that directly encodes RGBA frames to MP4 with precise PTS control.
    /// Replaces stdin-based RgbaRecorderManager to eliminate timing issues caused by async queue delays.
    /// </summary>
    public class LibavRecorderManager : IDisposable
    {
        private unsafe AVFormatContext* formatContext;
        private unsafe AVCodecContext* codecContext;
        private unsafe AVStream* videoStream;
        private unsafe AVFrame* yuvFrame;
        private unsafe AVPacket* packet;
        private unsafe SwsContext* swsContext;

        private bool isRecording;
        private string currentOutputPath;
        private readonly object recordingLock = new object();
        private ICaptureFrameSource frameSourceForRecordInfo;

        // Frame timing collection for .recordinfo.xml
        private List<FrameTime> frameTimes;
        private DateTime recordingStartTime;
        private int frameWidth;
        private int frameHeight;
        private float frameRate;
        private int recordingFrameCounter;
        private DateTime lastFrameWriteTime;
        private AVRational timeBase;

        // PERFORMANCE: Async frame writing to prevent blocking
        private readonly System.Collections.Concurrent.ConcurrentQueue<(byte[] rgbaData, DateTime captureTime, int frameNumber)> frameQueue;
        private System.Threading.SemaphoreSlim frameQueueSemaphore;
        private Task frameWritingTask;
        private CancellationTokenSource frameWritingCancellation;

        public bool IsRecording => isRecording;
        public string CurrentOutputPath => currentOutputPath;
        public FrameTime[] FrameTimes => frameTimes?.ToArray() ?? new FrameTime[0];

        public event Action<string> RecordingStarted;
        public event Action<string, bool> RecordingStopped; // path, success

        static LibavRecorderManager()
        {
            // Ensure FFmpeg native libraries are loaded
            FfmpegNativeLoader.EnsureRegistered();
        }

        public LibavRecorderManager()
        {
            this.frameTimes = new List<FrameTime>();
            this.frameQueue = new System.Collections.Concurrent.ConcurrentQueue<(byte[], DateTime, int)>();
            this.frameQueueSemaphore = new System.Threading.SemaphoreSlim(0);
            this.frameWritingCancellation = new CancellationTokenSource();
        }

        /// <summary>
        /// Start recording RGBA frames to an MP4 file using FFmpeg.AutoGen
        /// </summary>
        public bool StartRecording(string outputPath, int frameWidth, int frameHeight, float frameRate, ICaptureFrameSource captureFrameSource = null)
        {
            lock (recordingLock)
            {
                if (isRecording)
                {
                    Tools.Logger.VideoLog.LogCall(this, "Recording already in progress, cannot start new recording");
                    return false;
                }

                try
                {
                    // Ensure output directory exists
                    string outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    currentOutputPath = outputPath;
                    frameSourceForRecordInfo = captureFrameSource;
                    this.frameWidth = frameWidth;
                    this.frameHeight = frameHeight;
                    this.frameRate = frameRate;

                    // Reset frame timing collection
                    frameTimes.Clear();
                    recordingStartTime = UnifiedFrameTimingManager.InitializeRecordingStartTime();
                    recordingFrameCounter = 0;
                    lastFrameWriteTime = DateTime.MinValue;

                    // Reset async components
                    frameWritingCancellation?.Dispose();
                    frameWritingCancellation = new CancellationTokenSource();
                    while (frameQueue.TryDequeue(out _)) { }
                    frameQueueSemaphore?.Dispose();
                    frameQueueSemaphore = new System.Threading.SemaphoreSlim(0);

                    // Initialize FFmpeg encoder
                    if (!InitializeEncoder(outputPath, frameWidth, frameHeight, frameRate))
                    {
                        Tools.Logger.VideoLog.LogCall(this, "Failed to initialize FFmpeg encoder");
                        return false;
                    }

                    isRecording = true;

                    // Start async frame writing task
                    frameWritingTask = Task.Run(() => FrameWritingLoop(frameWritingCancellation.Token));

                    Tools.Logger.VideoLog.LogCall(this, $"LibAV recording started successfully - Output: {outputPath}");
                    RecordingStarted?.Invoke(outputPath);

                    return true;
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    Tools.Logger.VideoLog.LogCall(this, $"Exception while starting LibAV recording: {ex.Message}");
                    CleanupEncoder();
                    return false;
                }
            }
        }

        /// <summary>
        /// Initialize FFmpeg encoder with direct PTS control
        /// </summary>
        private unsafe bool InitializeEncoder(string outputPath, int width, int height, float frameRate)
        {
            try
            {
                // Allocate output format context
                AVFormatContext* ctx = null;
                int ret = ffmpeg.avformat_alloc_output_context2(&ctx, null, null, outputPath);
                if (ret < 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Failed to allocate output context: {ret}");
                    return false;
                }
                formatContext = ctx;

                // Find H.264 encoder - prefer hardware, fallback to software
                AVCodec* codec = null;
                string encoderName = null;

                // Try hardware encoders based on platform
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                {
                    // macOS: Try VideoToolbox (Apple hardware encoder)
                    codec = ffmpeg.avcodec_find_encoder_by_name("h264_videotoolbox");
                    if (codec != null)
                    {
                        encoderName = "h264_videotoolbox (macOS VideoToolbox - GPU accelerated)";
                    }
                }
                else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    // Windows: Try NVIDIA NVENC first (most common GPU), then Intel QSV, then AMD AMF
                    string[] windowsEncoders = new[]
                    {
                        "h264_nvenc",    // NVIDIA GPU
                        "h264_qsv",      // Intel Quick Sync
                        "h264_amf"       // AMD GPU
                    };

                    foreach (var encoder in windowsEncoders)
                    {
                        codec = ffmpeg.avcodec_find_encoder_by_name(encoder);
                        if (codec != null)
                        {
                            encoderName = encoder switch
                            {
                                "h264_nvenc" => "h264_nvenc (NVIDIA NVENC - GPU accelerated)",
                                "h264_qsv" => "h264_qsv (Intel Quick Sync - GPU accelerated)",
                                "h264_amf" => "h264_amf (AMD AMF - GPU accelerated)",
                                _ => encoder
                            };
                            break;
                        }
                    }
                }

                // Fallback to software encoder if hardware not available
                if (codec == null)
                {
                    codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
                    if (codec != null)
                    {
                        encoderName = "libx264 (software - CPU)";
                    }
                }

                if (codec == null)
                {
                    Tools.Logger.VideoLog.LogCall(this, "No H.264 codec found (hardware or software)");
                    return false;
                }

                Tools.Logger.VideoLog.LogCall(this, $"Using encoder: {encoderName}");

                // Create video stream
                videoStream = ffmpeg.avformat_new_stream(formatContext, codec);
                if (videoStream == null)
                {
                    Tools.Logger.VideoLog.LogCall(this, "Failed to create video stream");
                    return false;
                }
                videoStream->id = (int)formatContext->nb_streams - 1;

                // Allocate codec context
                codecContext = ffmpeg.avcodec_alloc_context3(codec);
                if (codecContext == null)
                {
                    Tools.Logger.VideoLog.LogCall(this, "Failed to allocate codec context");
                    return false;
                }

                // Set codec parameters
                codecContext->codec_id = AVCodecID.AV_CODEC_ID_H264;
                codecContext->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
                codecContext->width = width;
                codecContext->height = height;
                codecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;

                // Time base: 1/90000 for precise timing (standard for H.264)
                timeBase = new AVRational { num = 1, den = 90000 };
                codecContext->time_base = timeBase;
                videoStream->time_base = timeBase;

                // Frame rate
                codecContext->framerate = new AVRational { num = (int)(frameRate * 1000), den = 1000 };

                // Bitrate and quality settings
                codecContext->bit_rate = width * height * 4; // Reasonable bitrate
                codecContext->gop_size = Math.Max(1, (int)(frameRate * 0.1)); // 0.1 second GOP
                codecContext->max_b_frames = 0; // No B-frames for lower latency

                // Quality settings
                if ((formatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
                {
                    codecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
                }

                // Open codec
                int openRet = ffmpeg.avcodec_open2(codecContext, codec, null);
                if (openRet < 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Failed to open codec: {openRet}");
                    return false;
                }

                // Copy codec parameters to stream
                ffmpeg.avcodec_parameters_from_context(videoStream->codecpar, codecContext);

                // Allocate frame for YUV420P
                yuvFrame = ffmpeg.av_frame_alloc();
                if (yuvFrame == null)
                {
                    Tools.Logger.VideoLog.LogCall(this, "Failed to allocate YUV frame");
                    return false;
                }
                yuvFrame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
                yuvFrame->width = width;
                yuvFrame->height = height;

                int bufferRet = ffmpeg.av_frame_get_buffer(yuvFrame, 32);
                if (bufferRet < 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Failed to allocate frame buffer: {bufferRet}");
                    return false;
                }

                // Initialize SWS context for RGBA -> YUV420P conversion
                swsContext = ffmpeg.sws_getContext(
                    width, height, AVPixelFormat.AV_PIX_FMT_RGBA,
                    width, height, AVPixelFormat.AV_PIX_FMT_YUV420P,
                    ffmpeg.SWS_BILINEAR, null, null, null);

                if (swsContext == null)
                {
                    Tools.Logger.VideoLog.LogCall(this, "Failed to create SWS context");
                    return false;
                }

                // Allocate packet
                packet = ffmpeg.av_packet_alloc();
                if (packet == null)
                {
                    Tools.Logger.VideoLog.LogCall(this, "Failed to allocate packet");
                    return false;
                }

                // Open output file
                if ((formatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
                {
                    AVIOContext* ioContext;
                    int ioRet = ffmpeg.avio_open(&ioContext, outputPath, ffmpeg.AVIO_FLAG_WRITE);
                    if (ioRet < 0)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Failed to open output file: {ioRet}");
                        return false;
                    }
                    formatContext->pb = ioContext;
                }

                // Write header
                int headerRet = ffmpeg.avformat_write_header(formatContext, null);
                if (headerRet < 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Failed to write header: {headerRet}");
                    return false;
                }

                Tools.Logger.VideoLog.LogCall(this, "FFmpeg encoder initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                return false;
            }
        }

        /// <summary>
        /// Write an RGBA frame with explicit capture timestamp for precise PTS control
        /// </summary>
        public unsafe bool WriteFrame(byte[] rgbaData, DateTime captureTime, int frameNumber)
        {
            lock (recordingLock)
            {
                if (!isRecording || codecContext == null)
                {
                    return false;
                }

                try
                {
                    // Track actual frame timing
                    if (recordingFrameCounter % 100 == 0)
                    {
                        double intervalMs = lastFrameWriteTime != DateTime.MinValue ? (captureTime - lastFrameWriteTime).TotalMilliseconds : 0;
                        double actualFps = intervalMs > 0 ? (1000.0 / intervalMs) * 100 : 0;
                        double totalSeconds = (captureTime - recordingStartTime).TotalSeconds;
                        double avgFps = recordingFrameCounter > 0 ? recordingFrameCounter / totalSeconds : 0;

                        Tools.Logger.VideoLog.LogCall(this, $"LIBAV RECORDING: Frame {recordingFrameCounter}, Recent: {actualFps:F3}fps, Average: {avgFps:F3}fps, PerFrame: {intervalMs/100:F2}ms");
                        lastFrameWriteTime = captureTime;
                    }

                    // Queue frame for async writing
                    frameQueue.Enqueue((rgbaData, captureTime, frameNumber));
                    frameQueueSemaphore.Release();

                    // Collect frame timing for XML
                    recordingFrameCounter++;
                    var frameTime = UnifiedFrameTimingManager.CreateFrameTime(
                        recordingFrameCounter, captureTime, recordingStartTime);
                    frameTimes.Add(frameTime);

                    return true;
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    Tools.Logger.VideoLog.LogCall(this, $"Error queueing frame for LibAV encoding: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Check if encoder is ready (safe wrapper for pointer check)
        /// </summary>
        private unsafe bool IsEncoderReady()
        {
            return codecContext != null;
        }

        /// <summary>
        /// Async frame writing loop to prevent blocking the camera thread
        /// </summary>
        private async Task FrameWritingLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && isRecording)
                {
                    await frameQueueSemaphore.WaitAsync(cancellationToken);

                    if (frameQueue.TryDequeue(out var frameData))
                    {
                        if (IsEncoderReady())
                        {
                            EncodeFrame(frameData.rgbaData, frameData.captureTime);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Tools.Logger.VideoLog.LogCall(this, "LibAV frame writing loop cancelled");
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, $"Error in LibAV frame writing loop: {ex.Message}");
            }
        }

        /// <summary>
        /// Encode a single RGBA frame with explicit PTS from capture timestamp
        /// </summary>
        private unsafe void EncodeFrame(byte[] rgbaData, DateTime captureTime)
        {
            try
            {
                // Calculate PTS from capture time relative to recording start
                var timeSinceStart = captureTime - recordingStartTime;
                long pts = (long)(timeSinceStart.TotalSeconds * timeBase.den / timeBase.num);

                // Convert RGBA to YUV420P
                fixed (byte* rgbaPtr = rgbaData)
                {
                    byte*[] srcData = new byte*[] { rgbaPtr, null, null, null };
                    int[] srcLinesize = new int[] { frameWidth * 4, 0, 0, 0 };

                    ffmpeg.sws_scale(swsContext, srcData, srcLinesize, 0, frameHeight,
                        yuvFrame->data, yuvFrame->linesize);
                }

                // Set PTS explicitly from capture timestamp
                yuvFrame->pts = pts;

                // Send frame to encoder
                int sendRet = ffmpeg.avcodec_send_frame(codecContext, yuvFrame);
                if (sendRet < 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Error sending frame to encoder: {sendRet}");
                    return;
                }

                // Receive encoded packets
                while (true)
                {
                    int receiveRet = ffmpeg.avcodec_receive_packet(codecContext, packet);
                    if (receiveRet == ffmpeg.AVERROR(ffmpeg.EAGAIN) || receiveRet == ffmpeg.AVERROR_EOF)
                    {
                        break;
                    }
                    else if (receiveRet < 0)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Error receiving packet from encoder: {receiveRet}");
                        break;
                    }

                    // Rescale packet timestamps to stream timebase
                    ffmpeg.av_packet_rescale_ts(packet, codecContext->time_base, videoStream->time_base);
                    packet->stream_index = videoStream->index;

                    // Write packet to output
                    int writeRet = ffmpeg.av_interleaved_write_frame(formatContext, packet);
                    if (writeRet < 0)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Error writing packet: {writeRet}");
                    }

                    ffmpeg.av_packet_unref(packet);
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
        }

        /// <summary>
        /// Stop the current recording
        /// </summary>
        public bool StopRecording(int timeoutMs = 500)
        {
            return StopRecordingAsync(timeoutMs).GetAwaiter().GetResult();
        }

        private async Task<bool> StopRecordingAsync(int timeoutMs = 500)
        {
            bool wasRecording;
            Task taskToWait;

            lock (recordingLock)
            {
                wasRecording = isRecording;
                taskToWait = frameWritingTask;

                if (!isRecording)
                {
                    Tools.Logger.VideoLog.LogCall(this, "No recording in progress to stop");
                    return true;
                }

                frameWritingCancellation.Cancel();
                isRecording = false;
            }

            try
            {
                Tools.Logger.VideoLog.LogCall(this, "Stopping LibAV recording");

                // Wait for async frame writing to complete
                if (taskToWait != null)
                {
                    try
                    {
                        await taskToWait.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs / 2));
                    }
                    catch (TimeoutException)
                    {
                        Tools.Logger.VideoLog.LogCall(this, "Frame writing task did not complete in time");
                    }
                }

                bool success = false;
                string outputPath = currentOutputPath;

                // Flush encoder and write trailer
                success = FinalizeEncoding();

                // Verify output file
                if (File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);
                    if (fileInfo.Length > 0)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"LibAV recording completed - File size: {fileInfo.Length} bytes");
                        success = true;
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "LibAV recording file is empty");
                        success = false;
                    }
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, "LibAV recording file does not exist");
                    success = false;
                }

                // Generate XML metadata
                if (success)
                {
                    GenerateRecordInfoFile(outputPath);
                    Task.Delay(100).Wait();
                }

                RecordingStopped?.Invoke(outputPath, success);
                return success;
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, $"Exception while stopping LibAV recording: {ex.Message}");

                lock (recordingLock)
                {
                    isRecording = false;
                }

                RecordingStopped?.Invoke(currentOutputPath, false);
                return false;
            }
            finally
            {
                CleanupEncoder();
            }
        }

        /// <summary>
        /// Finalize encoding - flush and write trailer
        /// </summary>
        private unsafe bool FinalizeEncoding()
        {
            try
            {
                if (codecContext != null)
                {
                    FlushEncoder();

                    // Write trailer
                    if (formatContext != null)
                    {
                        int trailerRet = ffmpeg.av_write_trailer(formatContext);
                        Tools.Logger.VideoLog.LogCall(this, $"Write trailer result: {trailerRet}");
                        return trailerRet >= 0;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                return false;
            }
        }

        /// <summary>
        /// Flush encoder to get remaining packets
        /// </summary>
        private unsafe void FlushEncoder()
        {
            try
            {
                if (codecContext == null) return;

                // Send null frame to signal end of stream
                ffmpeg.avcodec_send_frame(codecContext, null);

                // Receive all remaining packets
                while (true)
                {
                    int receiveRet = ffmpeg.avcodec_receive_packet(codecContext, packet);
                    if (receiveRet == ffmpeg.AVERROR_EOF || receiveRet == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        break;
                    }
                    else if (receiveRet < 0)
                    {
                        break;
                    }

                    ffmpeg.av_packet_rescale_ts(packet, codecContext->time_base, videoStream->time_base);
                    packet->stream_index = videoStream->index;
                    ffmpeg.av_interleaved_write_frame(formatContext, packet);
                    ffmpeg.av_packet_unref(packet);
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
        }

        /// <summary>
        /// Cleanup FFmpeg resources
        /// </summary>
        private unsafe void CleanupEncoder()
        {
            try
            {
                if (packet != null)
                {
                    fixed (AVPacket** pktPtr = &packet)
                    {
                        ffmpeg.av_packet_free(pktPtr);
                    }
                    packet = null;
                }

                if (yuvFrame != null)
                {
                    fixed (AVFrame** framePtr = &yuvFrame)
                    {
                        ffmpeg.av_frame_free(framePtr);
                    }
                    yuvFrame = null;
                }

                if (swsContext != null)
                {
                    ffmpeg.sws_freeContext(swsContext);
                    swsContext = null;
                }

                if (codecContext != null)
                {
                    fixed (AVCodecContext** ctxPtr = &codecContext)
                    {
                        ffmpeg.avcodec_free_context(ctxPtr);
                    }
                    codecContext = null;
                }

                if (formatContext != null)
                {
                    if ((formatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0 && formatContext->pb != null)
                    {
                        ffmpeg.avio_closep(&formatContext->pb);
                    }

                    fixed (AVFormatContext** fmtPtr = &formatContext)
                    {
                        ffmpeg.avformat_free_context(*fmtPtr);
                    }
                    formatContext = null;
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
        }

        /// <summary>
        /// Generate .recordinfo.xml file using collected frame timing data
        /// </summary>
        private void GenerateRecordInfoFile(string videoFilePath)
        {
            try
            {
                if (frameSourceForRecordInfo == null)
                {
                    Tools.Logger.VideoLog.LogCall(this, "No frame source available for .recordinfo.xml generation");
                    return;
                }

                var recordingInfo = new RecodingInfo(frameSourceForRecordInfo);
                recordingInfo.FrameTimes = frameTimes.ToArray();
                recordingInfo.FilePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), videoFilePath);

                string basePath = videoFilePath;
                if (basePath.EndsWith(".mp4"))
                {
                    basePath = basePath.Replace(".mp4", "");
                }
                else if (basePath.EndsWith(".mkv"))
                {
                    basePath = basePath.Replace(".mkv", "");
                }

                FileInfo recordInfoFile = new FileInfo(basePath + ".recordinfo.xml");
                IOTools.Write(recordInfoFile.Directory.FullName, recordInfoFile.Name, recordingInfo);

                if (recordInfoFile.Exists)
                {
                    var fileInfo = new FileInfo(recordInfoFile.FullName);
                    Tools.Logger.VideoLog.LogCall(this, $"Generated .recordinfo.xml file: {recordInfoFile.FullName} ({fileInfo.Length} bytes)");
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, $"WARNING: .recordinfo.xml file was not created: {recordInfoFile.FullName}");
                }

                Tools.Logger.VideoLog.LogCall(this, $"Frame times count: {frameTimes.Count}");
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, $"Failed to generate .recordinfo.xml file for {videoFilePath}: {ex.Message}");
            }
        }

        public unsafe void Dispose()
        {
            if (isRecording)
            {
                StopRecording(500);
            }

            CleanupEncoder();
            frameWritingCancellation?.Dispose();
            frameQueueSemaphore?.Dispose();
        }
    }
}
