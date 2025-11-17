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
        private DateTime firstFrameTime; // Actual time of first frame for PTS offset
        private int frameWidth;
        private int frameHeight;
        private float frameRate;
        private int recordingFrameCounter;
        private int totalPacketsWritten; // Track total packets for debugging
        private DateTime lastFrameWriteTime;
        private DateTime lastFrameCaptureTime; // Store last frame time for padding
        private byte[] lastFrameData; // Store last frame for padding
        private AVRational timeBase;
        private long lastPacketPts; // Track last packet PTS

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
                    firstFrameTime = DateTime.MinValue; // Will be set on first WriteFrame call
                    recordingFrameCounter = 0;
                    totalPacketsWritten = 0;
                    lastPacketPts = 0;
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

                    Tools.Logger.VideoLog.LogCall(this, $"[DIAG] Recording started - File: {outputPath}");
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
                // Note: Hardware encoders may override this
                timeBase = new AVRational { num = 1, den = 90000 };
                codecContext->time_base = timeBase;

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

                // Log actual timebase after codec open (hardware encoders may change it)
                Tools.Logger.VideoLog.LogCall(this, $"[DIAG] Codec timebase after open: {codecContext->time_base.num}/{codecContext->time_base.den}");

                // Update our local timebase to match what codec actually uses
                timeBase = codecContext->time_base;

                // Set stream timebase to match codec timebase for proper rescaling
                videoStream->time_base = codecContext->time_base;

                Tools.Logger.VideoLog.LogCall(this, $"[DIAG] Stream timebase set to: {videoStream->time_base.num}/{videoStream->time_base.den}");

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

                // Write header with Matroska-specific options for proper indexing
                AVDictionary* options = null;
                // Force writing cues (index) for all frames - ensures seeking works to end of file
                ffmpeg.av_dict_set(&options, "reserve_index_space", "100000", 0);
                ffmpeg.av_dict_set(&options, "cluster_size_limit", "2097152", 0); // 2MB clusters
                ffmpeg.av_dict_set(&options, "cluster_time_limit", "1000", 0); // 1 second max cluster time

                int headerRet = ffmpeg.avformat_write_header(formatContext, &options);
                if (headerRet < 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Failed to write header: {headerRet}");
                    return false;
                }
                if (options != null)
                {
                    ffmpeg.av_dict_free(&options);
                }
                Tools.Logger.VideoLog.LogCall(this, $"[DIAG] Header written with Matroska indexing options");

                // CRITICAL: Check stream timebase AFTER write_header (it may have changed!)
                Tools.Logger.VideoLog.LogCall(this, $"[DIAG] Stream timebase AFTER write_header: {videoStream->time_base.num}/{videoStream->time_base.den}");

                // If stream timebase changed, we need to use the new one for packet writes
                if (videoStream->time_base.num != timeBase.num || videoStream->time_base.den != timeBase.den)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"[DIAG] WARNING: Stream timebase changed from {timeBase.num}/{timeBase.den} to {videoStream->time_base.num}/{videoStream->time_base.den}");
                    Tools.Logger.VideoLog.LogCall(this, $"[DIAG] Will need to rescale packets from {timeBase.num}/{timeBase.den} to {videoStream->time_base.num}/{videoStream->time_base.den}");
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
                    // Set first frame time on the very first frame
                    if (firstFrameTime == DateTime.MinValue)
                    {
                        firstFrameTime = captureTime;
                        Tools.Logger.VideoLog.LogCall(this, $"[DIAG] First frame captured at {firstFrameTime:HH:mm:ss.fff}");
                    }

                    // Track actual frame timing
                    if (recordingFrameCounter % 100 == 0)
                    {
                        double intervalMs = lastFrameWriteTime != DateTime.MinValue ? (captureTime - lastFrameWriteTime).TotalMilliseconds : 0;
                        double actualFps = intervalMs > 0 ? (1000.0 / intervalMs) * 100 : 0;
                        double totalSeconds = (captureTime - firstFrameTime).TotalSeconds;
                        double avgFps = recordingFrameCounter > 0 ? recordingFrameCounter / totalSeconds : 0;

                        Tools.Logger.VideoLog.LogCall(this, $"LIBAV RECORDING: Frame {recordingFrameCounter}, Recent: {actualFps:F3}fps, Average: {avgFps:F3}fps, PerFrame: {intervalMs/100:F2}ms");
                        lastFrameWriteTime = captureTime;
                    }

                    // Queue frame for async writing
                    frameQueue.Enqueue((rgbaData, captureTime, frameNumber));
                    frameQueueSemaphore.Release();

                    // Collect frame timing for XML
                    // Use recordingStartTime for Time field (absolute DateTime for lap marker alignment)
                    // But calculate Seconds from firstFrameTime (matches video PTS starting at 0)
                    recordingFrameCounter++;

                    var timeSinceRecordingStart = captureTime - recordingStartTime;
                    var timeSinceFirstFrame = captureTime - firstFrameTime;

                    var frameTime = new FrameTime
                    {
                        Frame = recordingFrameCounter,
                        Time = captureTime,  // Absolute time for lap marker conversion
                        Seconds = timeSinceFirstFrame.TotalSeconds  // Relative to first frame (matches video PTS)
                    };
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
                Tools.Logger.VideoLog.LogCall(this, $"LibAV frame writing loop cancelled - {frameQueue.Count} frames still in queue");
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, $"Error in LibAV frame writing loop: {ex.Message}");
            }

            // CRITICAL FIX: Process all remaining queued frames after cancellation
            // This ensures no frames are lost when StopRecording() is called
            int remainingFrames = 0;
            while (frameQueue.TryDequeue(out var frameData))
            {
                if (IsEncoderReady())
                {
                    EncodeFrame(frameData.rgbaData, frameData.captureTime);
                    remainingFrames++;
                }
            }

            if (remainingFrames > 0)
            {
                Tools.Logger.VideoLog.LogCall(this, $"[DIAG] Processed {remainingFrames} remaining queued frames after cancellation");
            }

            Tools.Logger.VideoLog.LogCall(this, $"[DIAG] FrameWritingLoop complete - total frames encoded: {recordingFrameCounter}");
        }

        /// <summary>
        /// Encode a single RGBA frame with explicit PTS from capture timestamp
        /// </summary>
        private unsafe void EncodeFrame(byte[] rgbaData, DateTime captureTime)
        {
            try
            {
                // Calculate PTS from capture time relative to FIRST FRAME
                // firstFrameTime is set in WriteFrame() before frames are queued
                var timeSinceFirstFrame = captureTime - firstFrameTime;
                long pts = (long)(timeSinceFirstFrame.TotalSeconds * timeBase.den / timeBase.num);

                // Log PTS for debugging timing issues (first 3 frames and every 100 frames)
                if (recordingFrameCounter <= 3 || recordingFrameCounter % 100 == 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"[DIAG] EncodeFrame #{recordingFrameCounter}, TimeSinceFirst={timeSinceFirstFrame.TotalSeconds:F6}s, PTS={pts}");
                }

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
                if (sendRet == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    // Encoder buffer full - need to receive packets first
                    Tools.Logger.VideoLog.LogCall(this, $"[DIAG] Encoder buffer full (EAGAIN) at frame {recordingFrameCounter}");
                }
                else if (sendRet < 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"[DIAG] ERROR sending frame {recordingFrameCounter} to encoder: {sendRet}");
                    return;
                }

                // Receive encoded packets (IMPORTANT: Always try to receive, even after EAGAIN)
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

                    // Rescale packet PTS from codec timebase (1/90000) to stream timebase (1/1000)
                    ffmpeg.av_packet_rescale_ts(packet, codecContext->time_base, videoStream->time_base);
                    packet->stream_index = videoStream->index;

                    // Track last packet PTS (after rescaling)
                    lastPacketPts = packet->pts;
                    totalPacketsWritten++;

                    // Log packet details periodically and for last 10 packets
                    if (totalPacketsWritten % 100 == 0 || totalPacketsWritten <= 3)
                    {
                        // Convert PTS ticks to seconds: ticks * timebase = ticks * (num/den)
                        // With timebase 1/90000: ticks / 90000 = seconds
                        double ptsSeconds = packet->pts * ((double)videoStream->time_base.num / videoStream->time_base.den);
                        Tools.Logger.VideoLog.LogCall(this, $"[DIAG] WrittenPacket #{totalPacketsWritten}: PTS={packet->pts} ticks, timebase={videoStream->time_base.num}/{videoStream->time_base.den} = {ptsSeconds:F6}s");
                    }

                    // Store for last packet logging
                    if (totalPacketsWritten > recordingFrameCounter - 10)
                    {
                        double ptsSeconds = packet->pts * ((double)videoStream->time_base.num / videoStream->time_base.den);
                        Tools.Logger.VideoLog.LogCall(this, $"[DIAG] WrittenPacket(end) #{totalPacketsWritten}: PTS={ptsSeconds:F6}s");
                    }

                    // Write packet to output
                    int writeRet = ffmpeg.av_interleaved_write_frame(formatContext, packet);
                    if (writeRet < 0)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Error writing packet: {writeRet}");
                    }

                    ffmpeg.av_packet_unref(packet);
                }

                // If send failed with EAGAIN, retry after receiving packets
                if (sendRet == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    sendRet = ffmpeg.avcodec_send_frame(codecContext, yuvFrame);
                    if (sendRet < 0 && sendRet != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Error on retry sending frame to encoder: {sendRet}");
                    }
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
                Tools.Logger.VideoLog.LogCall(this, $"[DIAG] StopRecording called - File: {currentOutputPath}, Frames: {recordingFrameCounter}, QueuedFrames: {frameQueue.Count}");

                // Wait for async frame writing to complete - use longer timeout to ensure all frames are encoded
                if (taskToWait != null)
                {
                    try
                    {
                        // Wait longer to ensure all queued frames are processed
                        await taskToWait.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs * 2));
                        Tools.Logger.VideoLog.LogCall(this, "Frame writing task completed successfully");
                    }
                    catch (TimeoutException)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Frame writing task did not complete in time - {frameQueue.Count} frames still in queue");
                    }
                }

                // Wait for encoder to finish processing all queued frames
                await Task.Delay(200);

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
                        Tools.Logger.VideoLog.LogCall(this, $"[DIAG] Recording complete - File: {outputPath}, Size: {fileInfo.Length} bytes");
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
                        // Set stream duration explicitly before writing trailer
                        // This ensures MKV has correct duration metadata for seeking
                        if (videoStream != null && lastPacketPts > 0)
                        {
                            videoStream->duration = lastPacketPts;
                            Tools.Logger.VideoLog.LogCall(this, $"[DIAG] Set stream duration to {lastPacketPts} ticks ({lastPacketPts/1000.0:F3}s)");
                        }

                        // Ensure all data is flushed before writing trailer
                        if (formatContext->pb != null)
                        {
                            ffmpeg.avio_flush(formatContext->pb);
                            Tools.Logger.VideoLog.LogCall(this, $"[DIAG] Flushed IO context before trailer");
                        }

                        Tools.Logger.VideoLog.LogCall(this, $"[DIAG] Writing trailer to file...");
                        int trailerRet = ffmpeg.av_write_trailer(formatContext);
                        Tools.Logger.VideoLog.LogCall(this, $"[DIAG] Trailer written, result: {trailerRet} (0=success)");

                        // Final flush after trailer
                        if (formatContext->pb != null)
                        {
                            ffmpeg.avio_flush(formatContext->pb);
                            Tools.Logger.VideoLog.LogCall(this, $"[DIAG] Final flush after trailer");
                        }

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

                Tools.Logger.VideoLog.LogCall(this, $"[DIAG] FlushEncoder START - Total frames sent to encoder: {recordingFrameCounter}");

                // Send null frame to signal end of stream
                int sendRet = ffmpeg.avcodec_send_frame(codecContext, null);
                Tools.Logger.VideoLog.LogCall(this, $"[DIAG] FlushEncoder - Sent null frame, result: {sendRet}");

                // Receive all remaining packets - keep trying even after EAGAIN
                int flushedPackets = 0;
                int eagainCount = 0;
                while (true)
                {
                    int receiveRet = ffmpeg.avcodec_receive_packet(codecContext, packet);
                    if (receiveRet == ffmpeg.AVERROR_EOF)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"[DIAG] FlushEncoder EOF - Flushed {flushedPackets} packets after {eagainCount} EAGAIN attempts");
                        break;
                    }
                    else if (receiveRet == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        eagainCount++;
                        if (eagainCount > 10)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"[DIAG] FlushEncoder stopping after {eagainCount} EAGAIN attempts, {flushedPackets} packets flushed");
                            break;
                        }
                        // Try again - encoder might have more packets after processing
                        continue;
                    }
                    else if (receiveRet < 0)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"[DIAG] FlushEncoder ERROR receiving packet: {receiveRet}");
                        break;
                    }

                    // Reset EAGAIN counter when we successfully receive a packet
                    eagainCount = 0;

                    // Rescale packet PTS from codec timebase to stream timebase
                    ffmpeg.av_packet_rescale_ts(packet, codecContext->time_base, videoStream->time_base);
                    packet->stream_index = videoStream->index;

                    // Track last packet PTS (after rescaling)
                    lastPacketPts = packet->pts;

                    // Log PTS of flushed packets
                    if (flushedPackets < 10)
                    {
                        double ptsSeconds = packet->pts * ((double)videoStream->time_base.num / videoStream->time_base.den);
                        Tools.Logger.VideoLog.LogCall(this, $"[DIAG] FlushedPacket #{flushedPackets}, PTS={packet->pts} ticks, timebase={videoStream->time_base.num}/{videoStream->time_base.den} = {ptsSeconds:F6}s");
                    }

                    ffmpeg.av_interleaved_write_frame(formatContext, packet);
                    ffmpeg.av_packet_unref(packet);
                    flushedPackets++;
                    totalPacketsWritten++;
                }

                double lastPacketSeconds = lastPacketPts * ((double)videoStream->time_base.num / videoStream->time_base.den);
                Tools.Logger.VideoLog.LogCall(this, $"[DIAG] FlushEncoder COMPLETE - Flushed: {flushedPackets}, TotalPackets: {totalPacketsWritten}, LastPTS: {lastPacketPts} ticks = {lastPacketSeconds:F6}s (Expected ~{(recordingFrameCounter/30.0):F2}s for {recordingFrameCounter} frames@30fps)");
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
