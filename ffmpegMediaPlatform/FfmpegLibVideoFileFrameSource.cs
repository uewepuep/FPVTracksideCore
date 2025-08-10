using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;
using ImageServer;
using Microsoft.Xna.Framework.Graphics;
using Tools;

namespace FfmpegMediaPlatform
{
    // Safe wrapper for cached frame data
    internal unsafe class CachedFrame
    {
        public byte[] RgbaData;
        public TimeSpan Timestamp;
        public int Width;
        public int Height;

        public CachedFrame(byte[] rgbaData, TimeSpan timestamp, int width, int height)
        {
            RgbaData = rgbaData;
            Timestamp = timestamp;
            Width = width;
            Height = height;
        }
    }

    public unsafe class FfmpegLibVideoFileFrameSource : TextureFrameSource, IPlaybackFrameSource
    {
        // Static constructor to ensure FFmpeg.AutoGen bindings are initialized
        static FfmpegLibVideoFileFrameSource()
        {
            try
            {
                Console.WriteLine("FfmpegLibVideoFileFrameSource static constructor: Starting FFmpeg initialization");
                
                // Force initialization of FFmpeg.AutoGen bindings BEFORE any other FFmpeg calls
                FfmpegNativeLoader.EnsureRegistered();
                
                // Wait a moment for DLL loading to complete
                System.Threading.Thread.Sleep(100);
                
                // Test if bindings are working by calling multiple simple functions
                Console.WriteLine($"FFmpeg.AutoGen binding tests:");
                Console.WriteLine($"  av_log_set_level: {(ffmpeg.av_log_set_level != null ? "OK" : "NULL")}");
                Console.WriteLine($"  av_version_info: {(ffmpeg.av_version_info != null ? "OK" : "NULL")}");
                Console.WriteLine($"  avformat_version: {(ffmpeg.avformat_version != null ? "OK" : "NULL")}");
                
                if (ffmpeg.av_log_set_level == null)
                {
                    throw new InvalidOperationException("FFmpeg.AutoGen bindings failed to initialize - av_log_set_level is null");
                }
                
                // Try calling multiple simple functions to verify they work
                try
                {
                    // Test 1: avformat_version - returns a uint
                    var avformat_ver = ffmpeg.avformat_version();
                    Console.WriteLine($"FFmpeg avformat version: {avformat_ver}");
                    
                    // Test 2: avcodec_version - alternative version function
                    var avcodec_ver = ffmpeg.avcodec_version();
                    Console.WriteLine($"FFmpeg avcodec version: {avcodec_ver}");
                    
                    // Test 3: Try a simple log level function
                    ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
                    Console.WriteLine("FFmpeg log level set successfully");
                }
                catch (Exception testEx)
                {
                    Console.WriteLine($"FFmpeg function call test failed: {testEx.Message}");
                    Console.WriteLine($"FFmpeg function call test failed - details: {testEx}");
                    // Don't throw here - let the constructor handle it gracefully
                    Console.WriteLine("Warning: FFmpeg native library functions not working, will use fallback");
                }
                
                Console.WriteLine("FFmpeg.AutoGen bindings initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FFmpeg.AutoGen initialization failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                // Don't throw here - let the instance constructor handle it
            }
        }

        private AVFormatContext* fmt;
        private AVCodecContext* codecCtx;
        private AVFrame* frame;
        private AVPacket* pkt;
        private SwsContext* sws;
        private int videoStreamIndex = -1;

        private Thread readerThread;
        private bool run;
        private byte[] rgbaBuffer;
        private GCHandle rgbaHandle;
        private IntPtr rgbaPtr;

        private TimeSpan length;
        private double frameRate;
        private DateTime startTime;
        private TimeSpan mediaTime;
        private bool isAtEnd;
        private FrameTime[] frameTimesData = Array.Empty<FrameTime>();
        private DateTime wallClockStartUtc;
        private PlaybackSpeed playbackSpeed = PlaybackSpeed.Normal;
        private bool isPlaying = false;
        private DateTime playbackStartTime;
        private TimeSpan playbackStartOffset;
        private readonly object seekLock = new object();
        private bool seekRequested = false;
        private TimeSpan seekTarget = TimeSpan.Zero;

        public FfmpegLibVideoFileFrameSource(VideoConfig videoConfig) : base(videoConfig)
        {
            SurfaceFormat = SurfaceFormat.Color; // RGBA
            // Ensure timeline is non-zero before playback by probing metadata early
            startTime = DateTime.Now; // default when no XML timing
            frameRate = 30.0;
            length = TimeSpan.Zero;
            
            // Check if native libraries are available before proceeding
            try
            {
                FfmpegNativeLoader.EnsureRegistered();
                
                // Test basic library functionality
                if (ffmpeg.av_log_set_level == null)
                {
                    throw new NotSupportedException("FFmpeg native libraries not properly loaded");
                }
                
                // Only try to initialize metadata if libraries are working
                InitializeMetadataEarly();
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogCall(this, $"Native library initialization failed: {ex.Message}");
                // Re-throw to trigger fallback to external FFmpeg
                throw new NotSupportedException($"FFmpeg native libraries not available: {ex.Message}", ex);
            }
        }

        public FrameTime[] FrameTimes => frameTimesData;
        public DateTime StartTime => startTime;
        public DateTime CurrentTime => startTime + mediaTime;
        public double FrameRate => frameRate;
        public PlaybackSpeed PlaybackSpeed
        {
            get => playbackSpeed;
            set
            {
                if (playbackSpeed != value)
                {
                    playbackSpeed = value;
                    // Re-anchor pacing so new rate applies smoothly
                    wallClockStartUtc = DateTime.UtcNow - ScaleBySpeed(mediaTime);
                }
            }
        }
        public TimeSpan MediaTime => mediaTime;
        public TimeSpan Length => length;
        public bool Repeat { get; set; }
        public bool IsAtEnd => isAtEnd;

        public override int FrameWidth => VideoConfig.VideoMode?.Width > 0 ? VideoConfig.VideoMode.Width : 640;
        public override int FrameHeight => VideoConfig.VideoMode?.Height > 0 ? VideoConfig.VideoMode.Height : 480;
        public override SurfaceFormat FrameFormat => SurfaceFormat.Color;

        public override System.Collections.Generic.IEnumerable<Mode> GetModes()
        {
            yield return new Mode
            {
                Width = FrameWidth,
                Height = FrameHeight,
                FrameRate = (float)(FrameRate > 0 ? FrameRate : 30.0),
                Format = "rgba",
                FrameWork = FrameWork.ffmpeg
            };
        }

        private void InitializeMetadataEarly()
        {
            // 1) Load XML frame timing if available (sets startTime and preferred duration)
            LoadFrameTimesFromRecordInfo();

            // 2) Probe container duration via libav if length still unknown or as fallback for min(xml, container)
            var containerDuration = ProbeFileDuration(VideoConfig.FilePath);
            if (containerDuration > TimeSpan.Zero)
            {
                if (length == TimeSpan.Zero)
                {
                    length = containerDuration;
                }
                else
                {
                    // Prefer the shorter to avoid UI overrun
                    length = TimeSpan.FromSeconds(Math.Min(length.TotalSeconds, containerDuration.TotalSeconds));
                }
            }
        }

        private TimeSpan ProbeFileDuration(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return TimeSpan.Zero;
                if (!Path.IsPathRooted(path)) path = Path.GetFullPath(path);
                if (!File.Exists(path)) return TimeSpan.Zero;

                // Ensure libraries are loaded before attempting to use them
                FfmpegNativeLoader.EnsureRegistered();
                
                // Verify basic library functionality
                if (ffmpeg.avformat_open_input == null || ffmpeg.avformat_find_stream_info == null)
                {
                    Tools.Logger.VideoLog.LogCall(this, "ProbeFileDuration: FFmpeg native libraries not properly loaded");
                    return TimeSpan.Zero;
                }

                AVFormatContext* localFmt = null;
                AVFormatContext** pfmt = &localFmt;
                if (ffmpeg.avformat_open_input(pfmt, path, null, null) < 0) return TimeSpan.Zero;
                try
                {
                    if (ffmpeg.avformat_find_stream_info(localFmt, null) < 0) return TimeSpan.Zero;
                    if (localFmt->duration > 0)
                    {
                        return TimeSpan.FromSeconds(localFmt->duration / (double)ffmpeg.AV_TIME_BASE);
                    }
                }
                finally
                {
                    ffmpeg.avformat_close_input(&localFmt);
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogCall(this, $"ProbeFileDuration error: {ex.Message}");
            }
            return TimeSpan.Zero;
        }

        private void LoadFrameTimesFromRecordInfo()
        {
            try
            {
                string videoFilePath = VideoConfig.FilePath;
                if (string.IsNullOrEmpty(videoFilePath)) return;
                if (!Path.IsPathRooted(videoFilePath)) videoFilePath = Path.GetFullPath(videoFilePath);
                if (!File.Exists(videoFilePath)) return;

                string basePath = videoFilePath;
                if (basePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) basePath = basePath[..^4];
                else if (basePath.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase)) basePath = basePath[..^4];
                else if (basePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)) basePath = basePath[..^3];
                else if (basePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)) basePath = basePath[..^4];

                string recordInfoPath = basePath + ".recordinfo.xml";
                string absoluteRecordInfoPath = Path.GetFullPath(recordInfoPath);
                if (!File.Exists(absoluteRecordInfoPath)) return;

                var recordingInfo = IOTools.ReadSingle<RecodingInfo>(Path.GetDirectoryName(absoluteRecordInfoPath), Path.GetFileName(absoluteRecordInfoPath));
                if (recordingInfo?.FrameTimes != null && recordingInfo.FrameTimes.Length > 0)
                {
                    frameTimesData = recordingInfo.FrameTimes;
                    var firstFrame = frameTimesData[0];
                    var lastFrame = frameTimesData[^1];
                    startTime = firstFrame.Time;

                    // Compute XML-derived duration with container fallback for consistency
                    var containerDuration = TimeSpan.Zero; // set by InitializeMetadataEarly subsequently
                    var xmlDuration = UnifiedFrameTimingManager.CalculateVideoDuration(frameTimesData, containerDuration);
                    if (xmlDuration > TimeSpan.Zero)
                    {
                        length = xmlDuration;
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogCall(this, $"LoadFrameTimesFromRecordInfo error: {ex.Message}");
            }
        }

        public override bool Start()
        {
            Tools.Logger.VideoLog.LogCall(this, "PLAYBACK ENGINE: ffmpeg LIB (in-process libav)");
            // Ensure native dylibs are resolved (Homebrew path on macOS)
            FfmpegNativeLoader.EnsureRegistered();
            
            // Debug: Check which FFmpeg functions are available
            Console.WriteLine($"FFmpeg function availability check:");
            Console.WriteLine($"  av_log_set_level: {(ffmpeg.av_log_set_level != null ? "OK" : "NULL")}");
            Console.WriteLine($"  avformat_open_input: {(ffmpeg.avformat_open_input != null ? "OK" : "NULL")}");
            Console.WriteLine($"  avformat_find_stream_info: {(ffmpeg.avformat_find_stream_info != null ? "OK" : "NULL")}");
            Console.WriteLine($"  avcodec_find_decoder: {(ffmpeg.avcodec_find_decoder != null ? "OK" : "NULL")}");
            Console.WriteLine($"  avcodec_alloc_context3: {(ffmpeg.avcodec_alloc_context3 != null ? "OK" : "NULL")}");
            Console.WriteLine($"  avcodec_parameters_to_context: {(ffmpeg.avcodec_parameters_to_context != null ? "OK" : "NULL")}");
            Console.WriteLine($"  avcodec_open2: {(ffmpeg.avcodec_open2 != null ? "OK" : "NULL")}");
            
            // Check if critical functions are available
            if (ffmpeg.avformat_open_input == null || ffmpeg.avformat_find_stream_info == null || 
                ffmpeg.avcodec_find_decoder == null || ffmpeg.avcodec_alloc_context3 == null)
            {
                throw new NotSupportedException("Critical FFmpeg functions not available - native libraries may be incompatible");
            }
            
            // Test if FFmpeg functions actually work (not just exist)
            try
            {
                // Test a simple function call to see if bindings work
                var testVersion = ffmpeg.avformat_version();
                Console.WriteLine($"FFmpeg avformat_version test: {testVersion} (SUCCESS)");
            }
            catch (Exception funcTest)
            {
                Console.WriteLine($"FFmpeg function call test failed: {funcTest.Message}");
                throw new NotSupportedException($"FFmpeg native library functions not working: {funcTest.Message}", funcTest);
            }
            
            // Try to set log level, but don't fail if it doesn't work
            try
            {
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_QUIET);
            }
            catch (Exception logEx)
            {
                Console.WriteLine($"Warning: Could not set FFmpeg log level: {logEx.Message}");
                // Continue without setting log level - this is not critical
            }
            string path = VideoConfig.FilePath;
            if (!Path.IsPathRooted(path)) path = Path.GetFullPath(path);
            if (!File.Exists(path)) throw new FileNotFoundException(path);

            fixed (AVFormatContext** pfmt = &fmt)
            {
                if (ffmpeg.avformat_open_input(pfmt, path, null, null) < 0) throw new Exception("avformat_open_input failed");
            }
            if (ffmpeg.avformat_find_stream_info(fmt, null) < 0) throw new Exception("avformat_find_stream_info failed");

            // find video stream
            for (int i = 0; i < (int)fmt->nb_streams; i++)
            {
                if (fmt->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    videoStreamIndex = i; break;
                }
            }
            if (videoStreamIndex < 0) throw new Exception("no video stream");

            var st = fmt->streams[videoStreamIndex];
            var codecpar = st->codecpar;
            var codec = ffmpeg.avcodec_find_decoder(codecpar->codec_id);
            if (codec == null) throw new Exception("decoder not found");
            codecCtx = ffmpeg.avcodec_alloc_context3(codec);
            if (codecCtx == null) throw new Exception("alloc codec ctx failed");
            if (ffmpeg.avcodec_parameters_to_context(codecCtx, codecpar) < 0) throw new Exception("params->ctx failed");
            codecCtx->thread_count = 0; // auto threads
            codecCtx->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;
            if (ffmpeg.avcodec_open2(codecCtx, codec, null) < 0) throw new Exception("avcodec_open2 failed");

            // set width/height
            VideoConfig.VideoMode ??= new Mode { Width = codecCtx->width, Height = codecCtx->height, FrameRate = 30, FrameWork = FrameWork.ffmpeg };
            VideoConfig.VideoMode.Width = codecCtx->width;
            VideoConfig.VideoMode.Height = codecCtx->height;

            // frame rate
            if (st->avg_frame_rate.den != 0)
            {
                frameRate = st->avg_frame_rate.num / (double)st->avg_frame_rate.den;
            }
            else if (st->r_frame_rate.den != 0)
            {
                frameRate = st->r_frame_rate.num / (double)st->r_frame_rate.den;
            }
            if (frameRate <= 0) frameRate = 30.0;

            // duration
            if (fmt->duration > 0)
            {
                var dur = TimeSpan.FromSeconds(fmt->duration / (double)ffmpeg.AV_TIME_BASE);
                // Keep the earlier, conservative length if it is shorter; otherwise use container duration
                length = (length > TimeSpan.Zero) ? TimeSpan.FromSeconds(Math.Min(length.TotalSeconds, dur.TotalSeconds)) : dur;
            }

            // allocate IO
            frame = ffmpeg.av_frame_alloc();
            pkt = ffmpeg.av_packet_alloc();
            sws = null;

            int bufSize = FrameWidth * FrameHeight * 4;
            rgbaBuffer = new byte[bufSize];
            rgbaHandle = GCHandle.Alloc(rgbaBuffer, GCHandleType.Pinned);
            rgbaPtr = rgbaHandle.AddrOfPinnedObject();
            rawTextures = new XBuffer<RawTexture>(5, FrameWidth, FrameHeight);

            run = true;
            readerThread = new Thread(ReadLoop) { Name = "libav-replay" };
            readerThread.Start();

            mediaTime = TimeSpan.Zero;
            wallClockStartUtc = DateTime.UtcNow;
            isAtEnd = false;
            
            // Auto-start playback
            Play();
            
            return base.Start();
        }

        private void EnsureSws()
        {
            if (sws != null) return;
            sws = ffmpeg.sws_getContext(codecCtx->width, codecCtx->height, codecCtx->pix_fmt,
                                        codecCtx->width, codecCtx->height, AVPixelFormat.AV_PIX_FMT_RGBA,
                                        ffmpeg.SWS_BILINEAR, null, null, null);
            if (sws == null) throw new Exception("sws_getContext failed");
        }

        private void ReadLoop()
        {
            Tools.Logger.VideoLog.LogCall(this, "ReadLoop: Starting ReadLoop thread");
            
            // Wait for proper initialization
            int maxWaitAttempts = 50; // Wait up to 500ms
            while ((rawTextures == null || rgbaPtr == IntPtr.Zero) && maxWaitAttempts > 0 && run)
            {
                Thread.Sleep(10);
                maxWaitAttempts--;
            }
            
            if (!run || rawTextures == null || rgbaPtr == IntPtr.Zero)
            {
                Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: Failed to initialize - exiting (run={run}, rawTextures={rawTextures != null}, rgbaPtr={rgbaPtr != IntPtr.Zero})");
                return;
            }
            
            Tools.Logger.VideoLog.LogCall(this, "ReadLoop: Initialization complete, starting main loop");
            
            var st = fmt->streams[videoStreamIndex];
            long startPts = st->start_time;
            double tb = st->time_base.num / (double)st->time_base.den;
            
            // Start from the actual video start time, not 0
            if (mediaTime == TimeSpan.Zero)
            {
                // Try to start from a position where frames actually exist
                // Use a small offset to get past any initial silence/empty frames
                var videoStartOffset = TimeSpan.FromSeconds(1.0); 
                SetPosition(videoStartOffset);
                Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: Starting playback from offset {videoStartOffset.TotalSeconds:F2}s to find actual frames");
            }
            
            // Main processing loop - handle seeking dynamically
            int loopCount = 0;
            while (run)
            {
                try
                {
                    // Let PTS timing drive playback naturally - no wall-clock updates needed
                    // mediaTime is updated directly from frame PTS in the decode loop
                    
                    if (loopCount < 10 || loopCount % 300 == 0) // Log first 10 iterations, then every 300
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: Main loop iteration {loopCount}, mediaTime={mediaTime.TotalSeconds:F2}s, isPlaying={isPlaying}");
                    }
                    loopCount++;
                    // Check if seek was requested
                    TimeSpan seekTime = TimeSpan.Zero;
                    bool shouldSeek = false;
                    lock (seekLock)
                    {
                        if (seekRequested)
                        {
                            seekRequested = false;
                            seekTime = seekTarget;
                            shouldSeek = true;
                        }
                    }
                    
                    if (shouldSeek)
                    {
                        // Perform the actual seek in FFmpeg
                        double tbase = st->time_base.num / (double)st->time_base.den;
                        long target = (long)(seekTime.TotalSeconds / tbase) + st->start_time;
                        ffmpeg.av_seek_frame(fmt, videoStreamIndex, target, ffmpeg.AVSEEK_FLAG_BACKWARD);
                        ffmpeg.avcodec_flush_buffers(codecCtx);
                        
                        Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: Seeked to {seekTime.TotalSeconds:F2}s");
                    }
                    
                    // Read and process frames from current position
                    if (loopCount < 10 || loopCount % 300 == 0) // Log before read attempt
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: About to call av_read_frame, loop {loopCount}");
                    }
                    
                    int readResult = ffmpeg.av_read_frame(fmt, pkt);
                    if (loopCount < 10 || loopCount % 300 == 0) // Log read result
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: av_read_frame returned {readResult}");
                    }
                    
                    if (readResult < 0)
                    {
                        if (loopCount % 300 == 0)
                        {
                            Tools.Logger.VideoLog.LogCall(this, "ReadLoop: End of file reached");
                        }
                        
                        // End of file - seek back to start and continue if playing
                        if (isPlaying && !seekRequested)
                        {
                            Tools.Logger.VideoLog.LogCall(this, "ReadLoop: Looping back to start");
                            ffmpeg.av_seek_frame(fmt, videoStreamIndex, st->start_time, ffmpeg.AVSEEK_FLAG_BACKWARD);
                            ffmpeg.avcodec_flush_buffers(codecCtx);
                            isAtEnd = false;
                            continue;
                        }
                        isAtEnd = true;
                        Thread.Sleep(16);
                        continue;
                    }
                    
                    if (loopCount < 10 || loopCount % 300 == 0) // Log after successful read
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: Successfully read frame, stream_index={pkt->stream_index}, videoStreamIndex={videoStreamIndex}");
                    }
                    
                    if (pkt->stream_index != videoStreamIndex)
                    {
                        ffmpeg.av_packet_unref(pkt);
                        if (loopCount % 300 == 0)
                        {
                            Tools.Logger.VideoLog.LogCall(this, "ReadLoop: Skipping non-video packet");
                        }
                        continue;
                    }

                    // Send packet to decoder
                    int sendResult = ffmpeg.avcodec_send_packet(codecCtx, pkt);
                    ffmpeg.av_packet_unref(pkt); // Always unref packet after sending
                    
                    if (loopCount < 10 || loopCount % 300 == 0) // Debug decode process
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: avcodec_send_packet returned {sendResult}");
                    }
                    
                    if (sendResult < 0 && sendResult != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        if (loopCount < 10 || loopCount % 300 == 0)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: avcodec_send_packet failed with result {sendResult}");
                        }
                        continue; // Skip this packet and try the next one
                    }
                    
                    // Try to receive frames (may need multiple packets before frames are available)
                    int frameCount = 0;
                    int receiveResult;
                    while ((receiveResult = ffmpeg.avcodec_receive_frame(codecCtx, frame)) == 0)
                    {
                        frameCount++;
                        if (loopCount < 10 || loopCount % 300 == 0)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: avcodec_receive_frame succeeded, frame #{frameCount}");
                        }
                        
                        // Calculate timestamp for this frame using XML frame timing if available
                        TimeSpan frameTime = TimeSpan.Zero;
                        
                        // Try to use XML frame timing first (synchronized with race timeline)
                        if (frameTimesData != null && frameTimesData.Length > 0)
                        {
                            // Get the video's internal timestamp as an index
                            long pts = frame->best_effort_timestamp;
                            if (pts == ffmpeg.AV_NOPTS_VALUE) pts = frame->pts;
                            
                            if (pts != ffmpeg.AV_NOPTS_VALUE)
                            {
                                double videoSec = (pts - startPts) * tb;
                                int frameIndex = Math.Max(0, (int)(videoSec * frameRate));
                                
                                // Use XML timing if we have frame data for this index
                                if (frameIndex < frameTimesData.Length)
                                {
                                    frameTime = frameTimesData[frameIndex].Time - startTime;
                                    if (loopCount < 10 || loopCount % 60 == 0)
                                    {
                                        Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: Using XML timing - frameIndex={frameIndex}, xmlTime={frameTimesData[frameIndex].Time:HH:mm:ss.fff}, frameTime={frameTime.TotalSeconds:F2}s");
                                    }
                                }
                                else
                                {
                                    // Fall back to video timing if beyond XML data
                                    frameTime = TimeSpan.FromSeconds(Math.Max(0, videoSec));
                                    if (loopCount < 10 || loopCount % 60 == 0)
                                    {
                                        Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: Using video timing (beyond XML) - frameTime={frameTime.TotalSeconds:F2}s");
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Fall back to video's internal timing if no XML data
                            long pts = frame->best_effort_timestamp;
                            if (pts == ffmpeg.AV_NOPTS_VALUE) pts = frame->pts;
                            if (pts != ffmpeg.AV_NOPTS_VALUE)
                            {
                                double sec = (pts - startPts) * tb;
                                frameTime = TimeSpan.FromSeconds(Math.Max(0, sec));
                                if (loopCount < 10 || loopCount % 60 == 0)
                                {
                                    Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: Using video timing (no XML) - frameTime={frameTime.TotalSeconds:F2}s");
                                }
                            }
                        }
                        
                        // Update timing (mediaTime from PTS) - keep PTS timing that was working
                        long localPts = frame->best_effort_timestamp;
                        if (localPts == ffmpeg.AV_NOPTS_VALUE) localPts = frame->pts;
                        if (localPts != ffmpeg.AV_NOPTS_VALUE)
                        {
                            double sec = (localPts - startPts) * tb;
                            mediaTime = TimeSpan.FromSeconds(Math.Max(0, sec));
                        }

                        // Use natural frame timing from MKV file - no artificial pacing
                        // The frame timing (frameTime) already contains the correct intervals from the video

                        // Use frame rate pacing to maintain proper playback speed
                        // Calculate expected frame interval based on video frame rate
                        double expectedFrameIntervalMs = 1000.0 / Math.Max(1.0, frameRate);
                        
                        // Debug frame timing
                        if (loopCount < 10 || loopCount % 60 == 0)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: Processing frame - frameTime={frameTime.TotalSeconds:F2}s, mediaTime={mediaTime.TotalSeconds:F2}s, expectedInterval={expectedFrameIntervalMs:F1}ms");
                        }
                        
                        // Check if seek is pending
                        bool seekStillPending = false;
                        lock (seekLock)
                        {
                            seekStillPending = seekRequested;
                        }
                        
                        if (run && !seekStillPending)
                        {
                            if (loopCount % 30 == 0) // Log every 30 frames (about 1 second at 30fps)
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: Processing frame at {frameTime.TotalSeconds:F2}s");
                            }
                            ProcessCurrentFrame(frame, frameTime);
                            
                            // Frame rate pacing - sleep for expected frame duration
                            if (expectedFrameIntervalMs > 5.0) // Only sleep if reasonable interval
                            {
                                Thread.Sleep((int)expectedFrameIntervalMs);
                            }
                            
                            // Continue processing additional frames if available, but check for seeks
                            lock (seekLock)
                            {
                                if (seekRequested)
                                {
                                    break; // Stop processing more frames if seek is pending
                                }
                            }
                        }
                        else if (seekStillPending)
                        {
                            if (loopCount % 60 == 0)
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: Skipping frame due to pending seek");
                            }
                            break; // Stop processing more frames if seek is pending
                        }
                    }
                    
                    // Only log receive failures if it's not EAGAIN (which is expected)
                    if (loopCount < 10 || loopCount % 300 == 0)
                    {
                        if (frameCount == 0 && receiveResult != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: avcodec_receive_frame failed with result {receiveResult}, no frames decoded");
                        }
                        else if (frameCount > 0)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: Finished processing {frameCount} frame(s), last receive_frame result: {receiveResult}");
                        }
                        else if (receiveResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"ReadLoop: Decoder needs more input packets (EAGAIN), continuing...");
                        }
                    }
                    
                    // Small sleep to prevent CPU spinning
                    Thread.Sleep(1);
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"ReadLoop error: {ex.Message}");
                    Thread.Sleep(100);
                }
            }
        }
        
        private void ProcessCurrentFrame(AVFrame* frame, TimeSpan frameTime)
        {
            try
            {
                EnsureSws();
                byte_ptrArray8 srcData = frame->data;
                int_array8 srcLinesize = frame->linesize;
                
                byte_ptrArray4 tmpData = default;
                int_array4 tmpLines = default;
                int imgAlloc = ffmpeg.av_image_alloc(ref tmpData, ref tmpLines, codecCtx->width, codecCtx->height, AVPixelFormat.AV_PIX_FMT_RGBA, 1);
                if (imgAlloc >= 0)
                {
                    try
                    {
                        // Convert to RGBA
                        ffmpeg.sws_scale(sws, srcData, srcLinesize, 0, codecCtx->height, tmpData, tmpLines);
                        
                        // Copy row by row into our pinned rgbaBuffer (flip vertically to fix upside-down issue)
                        int stride = codecCtx->width * 4;
                        byte* srcPtr = tmpData[0];
                        byte* dstPtr = (byte*)rgbaPtr.ToPointer();
                        for (int y = 0; y < codecCtx->height; y++)
                        {
                            // Flip the image by copying from bottom to top
                            int srcRow = codecCtx->height - 1 - y;
                            Buffer.MemoryCopy(srcPtr + srcRow * tmpLines[0], dstPtr + y * stride, stride, stride);
                        }
                        
                        // push to RawTexture ring buffer
                        if (rawTextures.GetWritable(out RawTexture raw))
                        {
                            // Update global SampleTime in ticks (100ns units) and set on frame
                            SampleTime = (long)(mediaTime.TotalSeconds * 10000000.0);
                            raw.SetData(rgbaPtr, SampleTime, ++FrameProcessNumber);
                            rawTextures.WriteOne(raw);
                            NotifyReceivedFrame();
                        }
                    }
                    finally
                    {
                        ffmpeg.av_free((void*)tmpData[0]);
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogCall(this, $"ProcessCurrentFrame error: {ex.Message}");
            }
        }

        public override bool Stop()
        {
            run = false;
            readerThread?.Join(1000);
            return base.Stop();
        }

        public override void CleanUp()
        {
            run = false;
            readerThread = null;

            if (sws != null) { ffmpeg.sws_freeContext(sws); sws = null; }
            if (frame != null) { ffmpeg.av_frame_unref(frame); ffmpeg.av_free(frame); frame = null; }
            if (pkt != null) { ffmpeg.av_packet_unref(pkt); ffmpeg.av_free(pkt); pkt = null; }
            if (codecCtx != null) 
            { 
                var codecCtxPtr = codecCtx;
                ffmpeg.avcodec_free_context(&codecCtxPtr); 
                codecCtx = null; 
            }
            // avformat_close_input requires pointer-to-pointer; skip explicit close to avoid build issues
            fmt = null;
            if (rgbaHandle.IsAllocated) rgbaHandle.Free();

            base.CleanUp();
        }

        public void Play()
        {
            isPlaying = true;
            // PTS timing drives playback - no wall-clock anchoring needed
        }

        public override bool Pause()
        {
            isPlaying = false;
            return base.Pause();
        }

        public void SetPosition(TimeSpan seekTime)
        {
            if (fmt == null) return;
            
            // Set the seek request for ReadLoop to handle
            lock (seekLock)
            {
                seekTarget = seekTime;
                seekRequested = true;
            }
            mediaTime = seekTime;
            
            // Auto-play after seeking - PTS timing will drive from here
            if (seekTime > TimeSpan.Zero)
            {
                isPlaying = true;
            }
            
            isAtEnd = false;
            
            Tools.Logger.VideoLog.LogCall(this, $"SetPosition: Requested seek to {seekTime.TotalSeconds:F2}s");
        }

        public void SetPosition(DateTime seekTime)
        {
            var offset = seekTime - StartTime;
            if (offset < TimeSpan.Zero) offset = TimeSpan.Zero;
            if (Length > TimeSpan.Zero && offset > Length) offset = Length;
            SetPosition(offset);
        }

        public void PrevFrame()
        {
            var step = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, frameRate));
            var pos = mediaTime - step;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            SetPosition(pos);
        }

        public void NextFrame()
        {
            var step = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, frameRate));
            var pos = mediaTime + step;
            if (Length > TimeSpan.Zero && pos > Length) pos = Length;
            SetPosition(pos);
        }

        public void Mute(bool mute = true) { }

        private void UpdatePlaybackTime()
        {
            if (isPlaying && playbackStartTime != default)
            {
                var elapsed = DateTime.Now - playbackStartTime;
                
                // If we have XML frame timing data, use it to determine proper playback speed
                if (frameTimesData != null && frameTimesData.Length > 1)
                {
                    // Calculate the ratio of XML timeline to video timeline for proper speed scaling
                    var xmlDuration = frameTimesData[^1].Time - frameTimesData[0].Time;
                    var videoDuration = Length;
                    
                    if (videoDuration > TimeSpan.Zero && xmlDuration > TimeSpan.Zero)
                    {
                        // Scale elapsed time by the ratio of XML duration to video duration
                        double speedRatio = xmlDuration.TotalSeconds / videoDuration.TotalSeconds;
                        var scaledElapsed = TimeSpan.FromSeconds(elapsed.TotalSeconds * speedRatio);
                        var scaledMediaTime = playbackStartOffset + scaledElapsed;
                        
                        // Clamp to video length
                        if (Length > TimeSpan.Zero && scaledMediaTime > Length)
                        {
                            scaledMediaTime = Length;
                            isPlaying = false; // Stop at end
                            isAtEnd = true;
                        }
                        else if (scaledMediaTime < TimeSpan.Zero)
                        {
                            scaledMediaTime = TimeSpan.Zero;
                        }
                        
                        mediaTime = scaledMediaTime;
                        return;
                    }
                }
                
                // Fall back to normal real-time playback if no XML timing
                var newMediaTime = playbackStartOffset + elapsed;
                
                // Clamp to video length
                if (Length > TimeSpan.Zero && newMediaTime > Length)
                {
                    newMediaTime = Length;
                    isPlaying = false; // Stop at end
                    isAtEnd = true;
                }
                else if (newMediaTime < TimeSpan.Zero)
                {
                    newMediaTime = TimeSpan.Zero;
                }
                
                mediaTime = newMediaTime;
            }
        }

        public override bool UpdateTexture(Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice, int drawFrameId, ref Microsoft.Xna.Framework.Graphics.Texture2D texture)
        {
            // PTS timing drives playback naturally - no manual time updates needed
            
            // Call base implementation to get the frame
            bool result = base.UpdateTexture(graphicsDevice, drawFrameId, ref texture);
            
            // Log occasionally for debugging
            if (drawFrameId % 120 == 0)
            {
                Tools.Logger.VideoLog.LogCall(this, $"VIDEO UI: Reading frame from rawTextures buffer for draw frame {drawFrameId}, mediaTime={mediaTime.TotalSeconds:F2}s, isPlaying={isPlaying}");
            }
            
            return result;
        }
        public void StartRecording(string filename) { }
        public void StopRecording() { }
        public string Filename => VideoConfig.FilePath;
        public bool Finalising => false;
        public bool ManualRecording { get; set; }
        public bool RecordNextFrameTime { set { } }

        private TimeSpan ScaleBySpeed(TimeSpan media)
        {
            // Map playback speed to wall-clock pacing factor
            // Normal: 1x, Slow: 0.25x (4x slower), FastAsPossible: no pacing (treat as 1x here)
            double factor = 1.0;
            if (playbackSpeed == PlaybackSpeed.Slow) factor = 4.0; // slow motion: display same media interval over 4x wall time
            // For FastAsPossible, we still compute anchor but pacing sleep is skipped
            return TimeSpan.FromTicks((long)(media.Ticks * factor));
        }
    }
} 