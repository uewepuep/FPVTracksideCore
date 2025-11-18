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
                // Use global initializer if available, otherwise fallback to direct registration
                if (!FfmpegGlobalInitializer.IsInitialized)
                {
                    FfmpegGlobalInitializer.Initialize();
                }
                
                // Set log level to reduce noise (only if not already set)
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException($"FFmpeg.AutoGen initialization failed", ex);
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
        private float slowSpeedFactor = 0.1f; // Default slow speed
        
        // Real-time playback timing
        private DateTime playbackStartTime;
        private TimeSpan playbackStartMediaTime;
        private FrameTime[] frameTimesData = Array.Empty<FrameTime>();
        private DateTime wallClockStartUtc;
        private PlaybackSpeed playbackSpeed = PlaybackSpeed.Normal;
        private bool isPlaying = false;
        private TimeSpan pausedAtMediaTime = TimeSpan.Zero; // Store exact position when paused
        private readonly object seekLock = new object();
        private bool seekRequested = false;
        private TimeSpan seekTarget = TimeSpan.Zero;
        private bool seekingToExactFrame = false;
        private TimeSpan exactSeekTarget = TimeSpan.Zero;
        private TimeSpan lastSeekTarget = TimeSpan.Zero;
        private int consecutiveSameTargetSeeks = 0;
        private bool seekInProgress = false;

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
                Tools.Logger.VideoLog.LogException(this, "Native library initialization failed", ex);
                // Re-throw to trigger fallback to external FFmpeg
                throw new NotSupportedException($"FFmpeg native libraries not available: {ex.Message}", ex);
            }
        }


        public FrameTime[] FrameTimes => frameTimesData;
        public DateTime StartTime => startTime;
        public DateTime CurrentTime 
        { 
            get 
            {
                var result = startTime + mediaTime;
                // Log occasionally to track timing
                // if (DateTime.Now.Millisecond % 500 < 50) // Log about every 500ms
                // {
                //     Tools.Logger.VideoLog.LogCallDebugOnly(this, $"CURRENTTIME: startTime={startTime:HH:mm:ss.fff}, mediaTime={mediaTime.TotalSeconds:F2}s, result={result:HH:mm:ss.fff} (State={State})");
                // }
                return result;
            }
        }
        public double FrameRate => frameRate;
        public PlaybackSpeed PlaybackSpeed
        {
            get => playbackSpeed;
            set
            {
                if (playbackSpeed != value)
                {
                    playbackSpeed = value;
                    // Reset timing baseline when speed changes to prevent catch-up effects
                    playbackStartTime = DateTime.MinValue;
                    // Playback speed is now controlled by frame pacing in ReadLoop
                    Tools.Logger.VideoLog.LogDebugCall(this, $"PlaybackSpeed changed to {value} - timing baseline reset");
                }
            }
        }
        public TimeSpan MediaTime => mediaTime;
        public TimeSpan Length => length;
        public bool Repeat { get; set; }
        public bool IsAtEnd => isAtEnd;
        public float SlowSpeedFactor 
        { 
            get => slowSpeedFactor; 
            set => slowSpeedFactor = Math.Max(0.1f, Math.Min(1.0f, value)); 
        }

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
                FrameWork = FrameWork.FFmpeg
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
                    Tools.Logger.VideoLog.LogDebugCall(this, "ProbeFileDuration: FFmpeg native libraries not properly loaded");
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
                Tools.Logger.VideoLog.LogException(this, "ProbeFileDuration error", ex);
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

                if (VideoConfig?.FrameTimes != null && VideoConfig.FrameTimes.Length > 0)
                {
                    frameTimesData = VideoConfig.FrameTimes;
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
                Tools.Logger.VideoLog.LogException(this, "LoadFrameTimesFromRecordInfo error", ex);
            }
        }

        public override bool Start()
        {
            Tools.Logger.VideoLog.LogDebugCall(this, "PLAYBACK ENGINE: ffmpeg LIB (in-process libav)");
            // Ensure native dylibs are resolved (Homebrew path on macOS)
            FfmpegNativeLoader.EnsureRegistered();
            
            // Debug: Check which FFmpeg functions are available
            Tools.Logger.VideoLog.LogDebugCall(this, $"FFmpeg function availability check:");
            Tools.Logger.VideoLog.LogDebugCall(this, $"  av_log_set_level: {(ffmpeg.av_log_set_level != null ? "OK" : "NULL")}");
            Tools.Logger.VideoLog.LogDebugCall(this, $"  avformat_open_input: {(ffmpeg.avformat_open_input != null ? "OK" : "NULL")}");
            Tools.Logger.VideoLog.LogDebugCall(this, $"  avformat_find_stream_info: {(ffmpeg.avformat_find_stream_info != null ? "OK" : "NULL")}");
            Tools.Logger.VideoLog.LogDebugCall(this, $"  avcodec_find_decoder: {(ffmpeg.avcodec_find_decoder != null ? "OK" : "NULL")}");
            Tools.Logger.VideoLog.LogDebugCall(this, $"  avcodec_alloc_context3: {(ffmpeg.avcodec_alloc_context3 != null ? "OK" : "NULL")}");
            Tools.Logger.VideoLog.LogDebugCall(this, $"  avcodec_parameters_to_context: {(ffmpeg.avcodec_parameters_to_context != null ? "OK" : "NULL")}");
            Tools.Logger.VideoLog.LogDebugCall(this, $"  avcodec_open2: {(ffmpeg.avcodec_open2 != null ? "OK" : "NULL")}");
            
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
                Tools.Logger.VideoLog.LogDebugCall(this, $"FFmpeg avformat_version test: {testVersion} (SUCCESS)");
            }
            catch (Exception funcTest)
            {
                Tools.Logger.VideoLog.LogException(this, $"FFmpeg function call test failed", funcTest);
                throw new NotSupportedException($"FFmpeg native library functions not working: {funcTest.Message}", funcTest);
            }
            
            // Try to set log level, but don't fail if it doesn't work
            try
            {
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_QUIET);
            }
            catch (Exception logEx)
            {
                Tools.Logger.VideoLog.LogException(this, $"Warning: Could not set FFmpeg log level", logEx);
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

            AVStream* st = fmt->streams[videoStreamIndex];
            AVCodecParameters* codecpar = st->codecpar;
            AVCodec* codec = ffmpeg.avcodec_find_decoder(codecpar->codec_id);
            if (codec == null) throw new Exception("decoder not found");
            codecCtx = ffmpeg.avcodec_alloc_context3(codec);
            if (codecCtx == null) throw new Exception("alloc codec ctx failed");
            if (ffmpeg.avcodec_parameters_to_context(codecCtx, codecpar) < 0) throw new Exception("params->ctx failed");

            // This seems to crash the ffmpeg.avcodec_open2, at least on windows.
            //codecCtx->thread_count = 0; // auto threads

            codecCtx->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;
            if (ffmpeg.avcodec_open2(codecCtx, codec, null) < 0) throw new Exception("avcodec_open2 failed");

            // set width/height
            VideoConfig.VideoMode ??= new Mode { Width = codecCtx->width, Height = codecCtx->height, FrameRate = 30, FrameWork = FrameWork.FFmpeg };
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
            Tools.Logger.VideoLog.LogDebugCall(this, $"INIT: mediaTime reset to {mediaTime.TotalSeconds:F2}s");
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
            // Tools.Logger.VideoLog.LogCallDebugOnly(this, "ReadLoop: Starting ReadLoop thread");
            
            // Wait for proper initialization
            int maxWaitAttempts = 50; // Wait up to 500ms
            while ((rawTextures == null || rgbaPtr == IntPtr.Zero) && maxWaitAttempts > 0 && run)
            {
                Thread.Sleep(10);
                maxWaitAttempts--;
            }
            
            if (!run || rawTextures == null || rgbaPtr == IntPtr.Zero)
            {
                // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: Failed to initialize - exiting (run={run}, rawTextures={rawTextures != null}, rgbaPtr={rgbaPtr != IntPtr.Zero})");
                return;
            }
            
            // Tools.Logger.VideoLog.LogCallDebugOnly(this, "ReadLoop: Initialization complete, starting main loop");
            
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
                // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: Starting playback from offset {videoStartOffset.TotalSeconds:F2}s to find actual frames");
            }
            
            // Main processing loop - handle seeking dynamically
            int loopCount = 0;
            while (run)
            {
                try
                {
                    // Let PTS timing drive playback naturally - no wall-clock updates needed
                    // mediaTime is updated directly from frame PTS in the decode loop
                    
                    // if (loopCount < 10 || loopCount % 300 == 0) // Log first 10 iterations, then every 300
                    // {
                    //     Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: Main loop iteration {loopCount}, mediaTime={mediaTime.TotalSeconds:F2}s, isPlaying={isPlaying}");
                    // }
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
                            Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: ReadLoop detected seek request to {seekTime.TotalSeconds:F3}s, isPlaying={isPlaying}");
                        }
                    }

                    if (shouldSeek)
                    {
                        // Use the same seeking mechanism on all platforms (Mac approach that works)
                        double tbase = st->time_base.num / (double)st->time_base.den;
                        long target = (long)(seekTime.TotalSeconds / tbase);
                        if (st->start_time != ffmpeg.AV_NOPTS_VALUE)
                        {
                            target += st->start_time;
                        }

                        int seekResult = ffmpeg.av_seek_frame(fmt, videoStreamIndex, target, ffmpeg.AVSEEK_FLAG_BACKWARD);

                        // Flush decoder buffers
                        ffmpeg.avcodec_flush_buffers(codecCtx);

                        // Set flag to skip frames until we reach exact target
                        lock (seekLock)
                        {
                            seekingToExactFrame = true;
                            exactSeekTarget = seekTime;
                        }

                        Tools.Logger.VideoLog.LogDebugCall(this, $"SEEK: Backward seek to keyframe completed, will decode forward to {seekTime.TotalSeconds:F3}s");
                    }
                    
                    // Read and process frames from current position
                    // if (loopCount < 10 || loopCount % 300 == 0) // Log before read attempt
                    // {
                    //     // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: About to call av_read_frame, loop {loopCount}");
                    // }
                    
                    int readResult = ffmpeg.av_read_frame(fmt, pkt);
                    // if (loopCount < 10 || loopCount % 300 == 0) // Log read result
                    // {
                    //     Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: av_read_frame returned {readResult}");
                    // }
                    
                    if (readResult < 0)
                    {
                        // if (loopCount % 300 == 0)
                        // {
                        //     Tools.Logger.VideoLog.LogCallDebugOnly(this, "ReadLoop: End of file reached");
                        // }

                        // End of file - seek back to start and continue if playing
                        if (isPlaying && !seekRequested)
                        {
                            Tools.Logger.VideoLog.LogDebugCall(this, "ReadLoop: Looping back to start - flushing buffers");

                            // Seek to start
                            ffmpeg.av_seek_frame(fmt, videoStreamIndex, st->start_time, ffmpeg.AVSEEK_FLAG_BACKWARD);

                            // Flush codec buffers to clear any cached frames
                            ffmpeg.avcodec_flush_buffers(codecCtx);

                            // Unref frame to release any references
                            if (frame != null)
                            {
                                ffmpeg.av_frame_unref(frame);
                            }

                            // Reset timing baseline to prevent speed issues after looping
                            playbackStartTime = DateTime.MinValue;
                            mediaTime = TimeSpan.Zero;
                            isAtEnd = false;
                            continue;
                        }
                        isAtEnd = true;
                        Thread.Sleep(16);
                        continue;
                    }
                    
                    // if (loopCount < 10 || loopCount % 300 == 0) // Log after successful read
                    // {
                    //     Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: Successfully read frame, stream_index={pkt->stream_index}, videoStreamIndex={videoStreamIndex}");
                    // }
                    
                    if (pkt->stream_index != videoStreamIndex)
                    {
                        ffmpeg.av_packet_unref(pkt);
                        // if (loopCount % 300 == 0)
                        // {
                        //     Tools.Logger.VideoLog.LogCallDebugOnly(this, "ReadLoop: Skipping non-video packet");
                        // }
                        continue;
                    }

                    // Send packet to decoder
                    int sendResult = ffmpeg.avcodec_send_packet(codecCtx, pkt);
                    ffmpeg.av_packet_unref(pkt); // Always unref packet after sending
                    
                    // if (loopCount < 10 || loopCount % 300 == 0) // Debug decode process
                    // {
                    //     Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: avcodec_send_packet returned {sendResult}");
                    // }
                    
                    if (sendResult < 0 && sendResult != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        // if (loopCount < 10 || loopCount % 300 == 0)
                        // {
                        //     Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: avcodec_send_packet failed with result {sendResult}");
                        // }
                        continue; // Skip this packet and try the next one
                    }
                    
                    // Try to receive frames (may need multiple packets before frames are available)
                    int frameCount = 0;
                    int receiveResult;
                    while ((receiveResult = ffmpeg.avcodec_receive_frame(codecCtx, frame)) == 0)
                    {
                        frameCount++;
                        // if (loopCount < 10 || loopCount % 300 == 0)
                        // {
                        //     Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: avcodec_receive_frame succeeded, frame #{frameCount}");
                        // }
                        
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
                                    // if (loopCount < 10 || loopCount % 60 == 0)
                                    // {
                                    //     Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: Using XML timing - frameIndex={frameIndex}, xmlTime={frameTimesData[frameIndex].Time:HH:mm:ss.fff}, frameTime={frameTime.TotalSeconds:F2}s");
                                    // }
                                }
                                else
                                {
                                    // Fall back to video timing if beyond XML data
                                    frameTime = TimeSpan.FromSeconds(Math.Max(0, videoSec));
                                    // if (loopCount < 10 || loopCount % 60 == 0)
                                    // {
                                    //     Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: Using video timing (beyond XML) - frameTime={frameTime.TotalSeconds:F2}s");
                                    // }
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
                                // if (loopCount < 10 || loopCount % 60 == 0)
                                // {
                                //     Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: Using video timing (no XML) - frameTime={frameTime.TotalSeconds:F2}s");
                                // }
                            }
                        }
                        
                        // Use natural frame timing from MKV file - no artificial pacing
                        // The frame timing (frameTime) already contains the correct intervals from the video
                        
                        // Check if seek is pending or if we're seeking to exact frame
                        bool seekStillPending = false;
                        bool skipFrameForExactSeek = false;
                        bool isTargetFrame = false;
                        TimeSpan exactTarget = TimeSpan.Zero;

                        lock (seekLock)
                        {
                            seekStillPending = seekRequested;
                            if (seekingToExactFrame)
                            {
                                exactTarget = exactSeekTarget;
                                // Skip this frame if it's before our exact target
                                if (frameTime < exactTarget)
                                {
                                    skipFrameForExactSeek = true;
                                }
                                else
                                {
                                    // We've reached the target frame - mark it for display
                                    isTargetFrame = true;
                                    seekingToExactFrame = false;
                                    seekInProgress = false; // Clear seek in progress flag
                                    Tools.Logger.VideoLog.LogDebugCall(this, $"SEEK: Reached exact target at {frameTime.TotalSeconds:F3}s (target was {exactTarget.TotalSeconds:F3}s)");
                                }
                            }
                        }

                        if (run && !seekStillPending && (isPlaying || isTargetFrame))
                        {
                            // Update timing (mediaTime from PTS)
                            long localPts = frame->best_effort_timestamp;
                            if (localPts == ffmpeg.AV_NOPTS_VALUE) localPts = frame->pts;
                            if (localPts != ffmpeg.AV_NOPTS_VALUE)
                            {
                                double sec = (localPts - startPts) * tb;
                                var newMediaTime = TimeSpan.FromSeconds(Math.Max(0, sec));
                                var oldMediaTime = mediaTime;

                                // When paused and at target frame, use the seek target time instead of PTS
                                // This ensures mediaTime matches what we're trying to seek to, even if video doesn't have exact frame
                                if (!isPlaying && isTargetFrame)
                                {
                                    lock (seekLock)
                                    {
                                        mediaTime = exactSeekTarget;
                                        pausedAtMediaTime = mediaTime; // Update paused position for resume
                                        Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Paused seek - set mediaTime to exactSeekTarget {mediaTime.TotalSeconds:F3}s (PTS was {newMediaTime.TotalSeconds:F3}s)");
                                    }
                                }
                                else
                                {
                                    mediaTime = newMediaTime;
                                    Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Updated mediaTime from {oldMediaTime.TotalSeconds:F3}s to {mediaTime.TotalSeconds:F3}s (isPlaying={isPlaying}, isTargetFrame={isTargetFrame})");
                                }
                            }

                            // Display frame unless we're skipping to reach target
                            if (!skipFrameForExactSeek || isTargetFrame)
                            {
                                ProcessCurrentFrame(frame, frameTime);
                                Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Displayed frame at {frameTime.TotalSeconds:F3}s, isPlaying={isPlaying}, isTargetFrame={isTargetFrame}");
                            }

                            // Real-time playback timing based on PTS
                            if (!isPlaying)
                            {
                                // When paused, sleep longer to avoid CPU spinning
                                Thread.Sleep(50);
                            }
                            else
                            {
                                double speedFactor = GetSpeedFactor();
                                double targetFrameInterval = (1000.0 / frameRate) * speedFactor; // Frame interval in milliseconds - multiply for slower playback
                                
                                // Initialize or reset timing baseline periodically to prevent drift
                                if (playbackStartTime == DateTime.MinValue)
                                {
                                    playbackStartTime = DateTime.UtcNow;
                                    playbackStartMediaTime = mediaTime;
                                }
                                else
                                {
                                    // Reset timing baseline every 5 seconds to prevent accumulating drift
                                    var timeSinceLastReset = DateTime.UtcNow - playbackStartTime;
                                    if (timeSinceLastReset.TotalSeconds > 5.0)
                                    {
                                        playbackStartTime = DateTime.UtcNow;
                                        playbackStartMediaTime = mediaTime;
                                        Tools.Logger.VideoLog.LogDebugCall(this, $"Timing baseline reset to prevent drift - mediaTime: {mediaTime.TotalSeconds:F3}s");
                                    }
                                }
                                
                                // Calculate how much time should have elapsed based on video timing
                                var videoElapsed = mediaTime - playbackStartMediaTime;
                                var targetElapsed = TimeSpan.FromMilliseconds(videoElapsed.TotalMilliseconds * speedFactor);
                                
                                // Calculate how much time has actually elapsed
                                var actualElapsed = DateTime.UtcNow - playbackStartTime;
                                
                                // Sleep if we're ahead of schedule
                                var timeDiff = targetElapsed - actualElapsed;
                                if (timeDiff.TotalMilliseconds > 1)
                                {
                                    // For slow motion, allow longer sleeps (up to 1000ms for very slow playback)
                                    int maxSleep = playbackSpeed == PlaybackSpeed.Slow ? 1000 : 50;
                                    Thread.Sleep((int)Math.Min(timeDiff.TotalMilliseconds, maxSleep));
                                }
                                else
                                {
                                    // Use frame-rate based minimal sleep for smooth playback
                                    Thread.Sleep((int)Math.Max(1, targetFrameInterval * 0.1));
                                }
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
                            // if (loopCount % 60 == 0)
                            // {
                            //     Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: Skipping frame due to pending seek");
                            // }
                            break; // Stop processing more frames if seek is pending
                        }
                        else if (!isPlaying)
                        {
                            // When paused, skip frame processing but continue loop to handle seeks
                            // if (loopCount % 300 == 0)
                            // {
                            //     Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: Skipping frame due to paused state");
                            // }
                            // Small sleep to avoid CPU spinning when paused
                            Thread.Sleep(50);
                            break; // Skip remaining frames in this packet
                        }
                    }
                    
                    // Only log receive failures if it's not EAGAIN (which is expected)
                    // if (loopCount < 10 || loopCount % 300 == 0)
                    // {
                    //     if (frameCount == 0 && receiveResult != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    //     {
                    //         Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: avcodec_receive_frame failed with result {receiveResult}, no frames decoded");
                    //     }
                    //     else if (frameCount > 0)
                    //     {
                    //         Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: Finished processing {frameCount} frame(s), last receive_frame result: {receiveResult}");
                    //     }
                    //     else if (receiveResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    //     {
                    //         Tools.Logger.VideoLog.LogCallDebugOnly(this, $"ReadLoop: Decoder needs more input packets (EAGAIN), continuing...");
                    //     }
                    // }
                    
                    // Small sleep to prevent CPU spinning
                    Thread.Sleep(1);
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, "ReadLoop error", ex);
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
                Tools.Logger.VideoLog.LogException(this, "ProcessCurrentFrame error", ex);
            }
        }

        public override bool Stop()
        {
            Tools.Logger.VideoLog.LogDebugCall(this, "FfmpegLibVideoFileFrameSource.Stop() - BEGIN");
            
            run = false;
            isPlaying = false;
            
            // Stop the reader thread and wait for it to complete
            if (readerThread != null && readerThread.IsAlive)
            {
                Tools.Logger.VideoLog.LogDebugCall(this, "Waiting for reader thread to stop...");
                if (!readerThread.Join(10000))
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, "WARNING: Reader thread did not stop within 2 seconds");
                }
                else
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, "Reader thread stopped successfully");
                }
            }
            
            // Additional wait to ensure frame processing stops
            System.Threading.Thread.Sleep(100);
            
            Tools.Logger.VideoLog.LogDebugCall(this, "FfmpegLibVideoFileFrameSource.Stop() - SUCCESS");
            return base.Stop();
        }

        public override void CleanUp()
        {
            run = false;

            // CRITICAL: Wait for reader thread to stop before freeing FFmpeg resources
            if (readerThread != null && readerThread.IsAlive)
            {
                if (!readerThread.Join(2000))
                {
                    Tools.Logger.VideoLog.LogCall(this, "WARNING: Reader thread did not stop during CleanUp within 2 seconds");
                }
            }
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
            if (fmt != null)
            {
                var fmtPtr = fmt;
                ffmpeg.avformat_close_input(&fmtPtr);
                fmt = null;
            }
            if (rgbaHandle.IsAllocated) rgbaHandle.Free();

            base.CleanUp();
        }

        public void Play()
        {
            Tools.Logger.VideoLog.LogDebugCall(this, $"Play() called - mediaTime: {mediaTime.TotalSeconds:F2}s, pausedAtMediaTime: {pausedAtMediaTime.TotalSeconds:F2}s");

            // Reset playback timing when starting/resuming playback
            // This ensures accurate real-time timing regardless of seeks or pauses
            playbackStartTime = DateTime.MinValue;
            playbackStartMediaTime = TimeSpan.Zero;
            Tools.Logger.VideoLog.LogDebugCall(this, $"Play() reset timing variables for real-time playback");
            
            // If resuming from pause, restore the exact paused position
            if (State == States.Paused && pausedAtMediaTime != TimeSpan.Zero)
            {
                Tools.Logger.VideoLog.LogDebugCall(this, $"Resuming from pause - restoring mediaTime from {mediaTime.TotalSeconds:F2}s to {pausedAtMediaTime.TotalSeconds:F2}s");
                mediaTime = pausedAtMediaTime;
                // Seek to the exact paused position in the video stream
                lock (seekLock)
                {
                    seekTarget = pausedAtMediaTime;
                    seekRequested = true;
                }
            }
            
            isPlaying = true;
            // Set the proper state in the base class
            base.Start();
            Tools.Logger.VideoLog.LogDebugCall(this, $"Play() completed - mediaTime: {mediaTime.TotalSeconds:F2}s, isPlaying: {isPlaying}");
        }

        public override bool Pause()
        {
            Tools.Logger.VideoLog.LogDebugCall(this, $"Pause() called - mediaTime: {mediaTime.TotalSeconds:F2}s");
            isPlaying = false;
            // Store the exact position where we paused
            pausedAtMediaTime = mediaTime;
            // Call base class Pause() which sets the state properly
            bool result = base.Pause();
            Tools.Logger.VideoLog.LogDebugCall(this, $"Pause() completed - stored pausedAtMediaTime: {pausedAtMediaTime.TotalSeconds:F2}s, isPlaying: {isPlaying}");
            return result;
        }

        public void SetPosition(TimeSpan seekTime)
        {
            if (fmt == null) return;

            // Track if we're repeatedly seeking to the same target
            if (Math.Abs((seekTime - lastSeekTarget).TotalSeconds) < 0.001)
            {
                consecutiveSameTargetSeeks++;
            }
            else
            {
                consecutiveSameTargetSeeks = 0;
            }
            lastSeekTarget = seekTime;

            // LOG WHO IS CALLING SetPosition
            var stackTrace = new System.Diagnostics.StackTrace(1, true);
            var callerFrame = stackTrace.GetFrame(0);
            string caller = callerFrame?.GetMethod()?.Name ?? "Unknown";
            Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: SetPosition({seekTime.TotalSeconds:F3}s) called from {caller}, currentMediaTime={mediaTime.TotalSeconds:F3}s");

            // Validate seek time - don't allow negative seeks
            if (seekTime < TimeSpan.Zero)
            {
                Tools.Logger.VideoLog.LogDebugCall(this, $"SetPosition: Invalid seek time {seekTime.TotalSeconds:F2}s, clamping to 0");
                seekTime = TimeSpan.Zero;
            }

            // Reset playback timing when seeking to ensure accurate real-time playback from new position
            playbackStartTime = DateTime.MinValue;
            playbackStartMediaTime = TimeSpan.Zero;
            Tools.Logger.VideoLog.LogDebugCall(this, $"SetPosition: Reset timing variables for real-time playback after seek to {seekTime.TotalSeconds:F2}s");

            // Set the seek request for ReadLoop to handle
            lock (seekLock)
            {
                // If a seek is already in progress, ignore this request
                if (seekInProgress && !isPlaying)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Ignoring seek request - previous seek still in progress");
                    return;
                }

                seekTarget = seekTime;
                seekRequested = true;
                seekInProgress = true;
            }
            Tools.Logger.VideoLog.LogDebugCall(this, $"SETPOSITION: Requesting seek from {mediaTime.TotalSeconds:F2}s to {seekTime.TotalSeconds:F2}s (State={State})");
            
            // Don't update mediaTime immediately - let the seek operation update it
            // This prevents timing conflicts between UI and actual seek position
            // mediaTime = seekTime; // REMOVED - will be set by seek operation
            
            // Auto-play after seeking - but don't override pause state
            if (seekTime > TimeSpan.Zero && State != States.Paused)
            {
                isPlaying = true;
            }
            
            isAtEnd = false;
            
            Tools.Logger.VideoLog.LogDebugCall(this, $"SetPosition: Requested seek to {seekTime.TotalSeconds:F2}s");
        }

        public void SetPosition(DateTime seekTime)
        {
            var stackTrace = new System.Diagnostics.StackTrace(1, true);
            var callerFrame = stackTrace.GetFrame(0);
            string caller = callerFrame?.GetMethod()?.Name ?? "Unknown";

            var offset = seekTime - StartTime;
            Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: SetPosition(DateTime {seekTime:HH:mm:ss.fff}) called from {caller}, StartTime={StartTime:HH:mm:ss.fff}, calculated offset={offset.TotalSeconds:F3}s");

            if (offset < TimeSpan.Zero) offset = TimeSpan.Zero;
            if (Length > TimeSpan.Zero && offset > Length) offset = Length;
            SetPosition(offset);
        }

        public void PrevFrame()
        {
            Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: PrevFrame called - currentMediaTime={mediaTime.TotalSeconds:F3}s, frameTimesData.Length={frameTimesData?.Length ?? 0}, frameRate={frameRate}");

            // Use actual frame timing data if available for accurate navigation
            if (frameTimesData != null && frameTimesData.Length > 0)
            {
                Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Using XML frame timing data with {frameTimesData.Length} frames");

                // Find current frame index based on mediaTime - find closest match
                TimeSpan currentRelativeTime = mediaTime;
                int currentIndex = -1;
                double minDiff = double.MaxValue;

                for (int i = 0; i < frameTimesData.Length; i++)
                {
                    TimeSpan frameRelTime = frameTimesData[i].Time - startTime;
                    double diff = Math.Abs((frameRelTime - currentRelativeTime).TotalSeconds);

                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        currentIndex = i;
                    }

                    // If we're now moving away from the target, we found the closest
                    if (frameRelTime > currentRelativeTime + TimeSpan.FromSeconds(0.1))
                    {
                        break;
                    }
                }

                Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Found currentIndex={currentIndex}, frameTime={(currentIndex >= 0 ? (frameTimesData[currentIndex].Time - startTime).TotalSeconds : -1):F3}s, diff={minDiff:F3}s");

                // Move to previous frame - find first frame with different timestamp
                if (currentIndex > 0)
                {
                    TimeSpan currentTime = frameTimesData[currentIndex].Time - startTime;
                    int prevIndex = currentIndex - 1;

                    // If we're stuck (tried to seek to this frame before but couldn't reach it),
                    // skip over it and try the next one back
                    if (consecutiveSameTargetSeeks > 2)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Skipping unreachable frame at index {prevIndex}, looking for next previous frame");
                        prevIndex--;
                        consecutiveSameTargetSeeks = 0; // Reset counter
                    }

                    // Skip frames with same timestamp - look for strictly less than
                    while (prevIndex > 0 && (frameTimesData[prevIndex].Time - startTime) >= currentTime)
                    {
                        prevIndex--;
                    }

                    // If we're at the beginning and still have same timestamp, just use index 0
                    if (prevIndex == 0 && (frameTimesData[0].Time - startTime) >= currentTime)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Already at first unique timestamp, cannot go back further");
                        return;
                    }

                    TimeSpan prevFrameTime = frameTimesData[prevIndex].Time - startTime;
                    Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Moving to prev frame index {prevIndex}, time {prevFrameTime.TotalSeconds:F3}s (current was {currentTime.TotalSeconds:F3}s, skipped {currentIndex - prevIndex - 1} frames)");
                    SetPosition(prevFrameTime);
                    return;
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Cannot go to prev frame, currentIndex={currentIndex}");
                }
            }
            else
            {
                Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: No XML timing data, using fallback frameRate={frameRate}");
            }

            // Fallback to estimated frame duration
            var step = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, frameRate));
            var pos = mediaTime - step;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Fallback - seeking to {pos.TotalSeconds:F3}s (step={step.TotalMilliseconds:F1}ms)");
            SetPosition(pos);
        }

        public void NextFrame()
        {
            Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: NextFrame called - currentMediaTime={mediaTime.TotalSeconds:F3}s, frameTimesData.Length={frameTimesData?.Length ?? 0}, frameRate={frameRate}");

            // Use actual frame timing data if available for accurate navigation
            if (frameTimesData != null && frameTimesData.Length > 0)
            {
                Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Using XML frame timing data with {frameTimesData.Length} frames");

                // Find current frame index based on mediaTime - find closest match
                TimeSpan currentRelativeTime = mediaTime;
                int currentIndex = -1;
                double minDiff = double.MaxValue;

                for (int i = 0; i < frameTimesData.Length; i++)
                {
                    TimeSpan frameRelTime = frameTimesData[i].Time - startTime;
                    double diff = Math.Abs((frameRelTime - currentRelativeTime).TotalSeconds);

                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        currentIndex = i;
                    }

                    // If we're now moving away from the target, we found the closest
                    if (frameRelTime > currentRelativeTime + TimeSpan.FromSeconds(0.1))
                    {
                        break;
                    }
                }

                Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Found currentIndex={currentIndex}, frameTime={(currentIndex >= 0 ? (frameTimesData[currentIndex].Time - startTime).TotalSeconds : -1):F3}s, diff={minDiff:F3}s");

                // Move to next frame - find first frame with different timestamp
                if (currentIndex >= 0 && currentIndex < frameTimesData.Length - 1)
                {
                    TimeSpan currentTime = frameTimesData[currentIndex].Time - startTime;
                    int nextIndex = currentIndex + 1;

                    // Skip frames with same timestamp - look for strictly greater than
                    while (nextIndex < frameTimesData.Length - 1 && (frameTimesData[nextIndex].Time - startTime) <= currentTime)
                    {
                        nextIndex++;
                    }

                    // If we reached the end and still have same timestamp
                    if (nextIndex == frameTimesData.Length - 1 && (frameTimesData[nextIndex].Time - startTime) <= currentTime)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Already at last unique timestamp, cannot go forward");
                        return;
                    }

                    TimeSpan nextFrameTime = frameTimesData[nextIndex].Time - startTime;
                    Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Moving to next frame index {nextIndex}, time {nextFrameTime.TotalSeconds:F3}s (current was {currentTime.TotalSeconds:F3}s, skipped {nextIndex - currentIndex - 1} frames)");
                    SetPosition(nextFrameTime);
                    return;
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Cannot go to next frame, currentIndex={currentIndex}, maxIndex={frameTimesData.Length - 1}");
                }
            }
            else
            {
                Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: No XML timing data, using fallback frameRate={frameRate}");
            }

            // Fallback to estimated frame duration
            var step = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, frameRate));
            var pos = mediaTime + step;
            if (Length > TimeSpan.Zero && pos > Length) pos = Length;
            Tools.Logger.VideoLog.LogCall(this, $"SEEK_DEBUG: Fallback - seeking to {pos.TotalSeconds:F3}s (step={step.TotalMilliseconds:F1}ms)");
            SetPosition(pos);
        }

        public void Mute(bool mute = true) { }

        // Removed UpdatePlaybackTime() - PTS timing from ReadLoop handles mediaTime naturally

        public override bool UpdateTexture(Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice, int drawFrameId, ref Microsoft.Xna.Framework.Graphics.Texture2D texture)
        {
            // Let PTS timing from ReadLoop drive mediaTime naturally
            // No need to override with wall-clock timing

            // Call base implementation to get the frame
            bool result = base.UpdateTexture(graphicsDevice, drawFrameId, ref texture);
            
            // Log occasionally for debugging
            // if (drawFrameId % 120 == 0)
            // {
            //     Tools.Logger.VideoLog.LogCallDebugOnly(this, $"VIDEO UI: Reading frame from rawTextures buffer for draw frame {drawFrameId}, mediaTime={mediaTime.TotalSeconds:F2}s, isPlaying={isPlaying}");
            // }
            
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
            // Normal: 1x, Slow: 10x slower = 10% speed, FastAsPossible: no pacing (treat as 1x here)
            double factor = GetSpeedFactor();
            // For FastAsPossible, we still compute anchor but pacing sleep is skipped
            return TimeSpan.FromTicks((long)(media.Ticks * factor));
        }
        
        private double GetSpeedFactor()
        {
            // Speed factor used as sleep multiplier: higher value = slower playback
            return playbackSpeed switch
            {
                PlaybackSpeed.Slow => 1.0 / slowSpeedFactor, // Use custom slow speed (e.g., 1/0.1 = 10x slower for 0.1 speed)
                PlaybackSpeed.FastAsPossible => 1.0, // Normal timing calculations
                _ => 1.0 // Normal speed: 100% speed
            };
        }
    }
} 