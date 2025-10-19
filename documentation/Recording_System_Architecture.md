# FPVTracksideCore Recording System Architecture

## Overview

The FPVTracksideCore system implements a dual-stream recording architecture that captures camera feeds in native format and converts them to RGBA for live display and MKV recording. This document explains the complete pipeline from camera capture to final output.

## System Architecture

### 1. Camera Capture Layer

**Platform-Specific Implementation:**

**macOS (AVFoundation):**
- **Native Format**: UYVY422 (2 bytes per pixel)
- **Hardware Acceleration**: VideoToolbox when available
- **Command**: `ffmpeg -f avfoundation -pixel_format uyvy422 -video_size 1280x720 -i "index" -pix_fmt rgba -f rawvideo -`

**Windows (DirectShow):**
- **Native Format**: YUY2 (2 bytes per pixel)  
- **Hardware Acceleration**: D3D11VA, CUDA when available
- **Command**: `ffmpeg -f dshow -pixel_format yuy2 -video_size 1280x720 -i "camera_name" -pix_fmt rgba -f rawvideo -`

### 2. RGBA Conversion Pipeline

**Live Mode**: Single stream conversion to RGBA for game engine display
**Recording Mode**: Dual-stream using `filter_complex` to split input:
- **Stream 1**: RGBA output for live display (pipe:1)
- **Stream 2**: H.264 encoding for recording (MKV output file)

### 3. Recording Architecture

**Separate Process Approach**: The system uses two independent FFmpeg processes:

- **Main Process**: Camera capture → RGBA conversion → Live display
- **Recording Process**: RGBA input → H.264 encoding → MKV output

**Process Flow**: Camera input → Main FFmpeg (RGBA) → RGBA Recorder Manager → Separate FFmpeg (H.264) → MKV file

### 4. Hardware Acceleration Detection

**Automatic Detection**: System detects and utilizes available hardware acceleration

**macOS**: VideoToolbox (`h264_videotoolbox` encoder)
**Windows**: NVIDIA CUDA, Intel QSV, AMD D3D11VA
**Fallback**: Software conversion maintains real-time performance

### 5. Frame Timing and Synchronization

**Camera-Driven Timing**: Camera operates at natural frame rate (typically 30fps), independent of game engine
**Recording Sync**: Uses wallclock timestamps and frame counters for accurate PTS and metadata generation

### 6. File Output and Metadata

**Video Output**: MKV (Matroska) with H.264 codec, 5Mbps default, fast start enabled, optimized GOP structure

**Why MKV Instead of MP4?**
- **Timing Accuracy**: Superior timestamp precision for FPV racing analysis
- **Streaming Robustness**: Resilient to corruption during interruptions
- **Metadata Flexibility**: Better support for custom metadata and frame timing
- **FFmpeg Compatibility**: Better hardware acceleration support
- **Seeking Performance**: Frame-accurate playback for race analysis
- **Cross-Platform**: Excellent compatibility across platforms

## Recording Start/Stop Process

### **Recording Initialization Flow**

The recording process follows a specific flow when starting and stopping:

#### 1. **Race Recording Request**
```csharp
// In VideoManager.cs - triggered when race recording is requested
public void StartRecording(Race race)
{
    // 1. Identify all video sources configured for recording
    var recordingSources = captureFrameSources.Where(r => r.VideoConfig.RecordVideoForReplays).ToList();
    
    // 2. Filter sources based on visibility (FPV feeds only record when visible)
    foreach (ICaptureFrameSource source in recordingSources)
    {
        if (source.VideoConfig.VideoBounds.All(r => r.SourceType == SourceTypes.FPVFeed))
        {
            if (source.IsVisible)
            {
                recording.Add(source);  // Add to recording collection
            }
        }
        else
        {
            recording.Add(source);  // Non-FPV sources always record
        }
    }
}
```

#### 2. **Frame Source Recording Start**
```csharp
// In FfmpegFrameSource.cs - called for each video source
public void StartRecording(string filename)
{
    recordingFilename = filename;
    Recording = true;
    
    // Use measured frame rate if available, otherwise fall back to configured rate
    float recordingFrameRate = frameRateMeasured ? measuredFrameRate : (VideoConfig.VideoMode?.FrameRate ?? 30.0f);
    
    // Start RGBA recording with separate ffmpeg process
    bool started = rgbaRecorderManager.StartRecording(filename, width, height, recordingFrameRate, this);
}
```

#### 3. **Dual-Stream Recording Activation**
When recording starts, the system activates dual-stream recording through the existing FFmpeg process:

**macOS (AVFoundation):**
```bash
ffmpeg -f avfoundation -pixel_format uyvy422 -video_size 1280x720 -i "index" \
       -filter_complex "split=2[out1][out2];[out1]format=rgba[outpipe];[out2]format=yuv420p[outfile]" \
       -map "[outpipe]" -f rawvideo pipe:1 \
       -map "[outfile]" -c:v h264_videotoolbox -preset ultrafast -tune zerolatency -b:v 5M -f matroska output.mkv
```

**Windows (DirectShow):**
```bash
ffmpeg -f dshow -rtbufsize 2048M -framerate 30 -video_size 1280x720 -i video="camera_name" \
       -filter_complex "split=2[out1][out2];[out1]format=rgba[outpipe];[out2]format=yuv420p[outfile]" \
       -map "[outpipe]" -f rawvideo pipe:1 \
       -map "[outfile]" -c:v h264_nvenc -preset llhp -tune zerolatency -b:v 5M -f matroska output.mkv
```

### **Recording Stop Process**

#### 1. **Stop Recording Request**
```csharp
// In VideoManager.cs
public void StopRecording()
{
    lock (recording)
    {
        recording.Clear();  // Clear all recording sources
    }
    mutex.Set();  // Signal recording thread to stop
}
```

#### 2. **Frame Source Recording Stop**
```csharp
// In FfmpegFrameSource.cs
public void StopRecording()
{
    Recording = false;
    finalising = true;
    
    // Stop RGBA recording
    bool stopped = rgbaRecorderManager.StopRecording();
    
    finalising = false;
}
```

#### 3. **FFmpeg Process Cleanup**
When recording stops, the system:

1. **Sends SIGINT Signal**: Graceful shutdown for clean file finalization
2. **Waits for Exit**: Allows FFmpeg to finalize the MKV file
3. **Force Kill if Needed**: Falls back to process termination if graceful shutdown fails
4. **Process Continues**: FFmpeg continues running in live-only mode (without recording output)

### **Key Recording States**

| State | Description | FFmpeg Mode |
|-------|-------------|-------------|
| **Live Only** | Camera capture for display only | Single stream to RGBA output |
| **Recording Active** | Dual-stream: live + recording | Split stream with MKV recording |
| **Finalizing** | Recording stopped, finalizing file | Graceful shutdown process |
| **Stopped** | Back to live-only mode | Single stream to RGBA output |

### **Recording File Management**

#### **Filename Generation**
```csharp
// In VideoManager.cs
private string GetRecordingFilename(Race race, FrameSource source)
{
    // Use .mkv for better seekability and timestamp preservation
    return Path.Combine(EventDirectory.FullName, race.ID.ToString(), source.VideoConfig.ffmpegId) + ".mkv";
}
```

#### **Directory Structure**
```
events/
├── {race-id}/
│   ├── {camera-id}.mkv                    # Main video recording
│   ├── {camera-id}.recordinfo.xml         # Frame timing metadata
│   └── race.json                          # Race information
```

### **Recording Process Lifecycle**

1. **Pre-Recording**: FFmpeg running in live-only mode
2. **Recording Start**: Dual-stream recording activated (same process)
3. **Recording Active**: Two parallel streams (live display + MKV recording)
4. **Recording Stop**: Recording stream stopped, live stream continues
5. **File Finalization**: MKV file completed and finalized
6. **Post-Recording**: FFmpeg continues in live-only mode
7. **Metadata Generation**: `.recordinfo.xml` file created with frame timing data

## Recording Data Flow

### **How Recording Feed Gets Input Data**

The recording feed receives its input data through a separate FFmpeg process that consumes RGBA frames from the main camera process:

#### **1. Camera Input Source**
```csharp
// Camera feeds provide native format data directly to FFmpeg
// macOS: UYVY422 format from AVFoundation
// Windows: YUY2 format from DirectShow
```

#### **2. Separate FFmpeg Process Recording**
When recording is active, the RGBA Recorder Manager starts a separate FFmpeg process:

```bash
# Separate recording process that reads RGBA frames from stdin
-f rawvideo -pix_fmt rgba -s {width}x{height} -i pipe:0 -c:v h264_videotoolbox output.mkv
```

**Process Breakdown:**
- **Main Process**: Camera capture → RGBA conversion → Live display
- **Recording Process**: RGBA input from stdin → H.264 encoding → MKV output

#### **3. Data Flow Architecture**

```
Camera Input (UYVY422/YUY2)
         ↓
   Main FFmpeg Process
         ↓
   RGBA Conversion
         ↓
    ┌─────────┴─────────┐
    ↓                   ↓
Live Display        RGBA Recorder Manager
(RGBA output)      (Separate Process)
    ↓                   ↓
Game Engine         H.264 Encoding
(Real-time)         (MKV output)
```

#### **4. Process Output Mapping**

```bash
# Main process outputs RGBA for live display
-f rawvideo -pix_fmt rgba pipe:1

# Recording process reads RGBA from stdin and outputs MKV
-f rawvideo -pix_fmt rgba -i pipe:0 -c:v h264_videotoolbox output.mkv
```

**Main Process Output:**
- **Format**: RGBA (4 bytes per pixel)
- **Output**: `pipe:1` (stdout) for game engine consumption
- **Purpose**: Real-time display in the UI

**Recording Process:**
- **Input**: RGBA frames from stdin (main process output)
- **Output**: H.264 encoded video to MKV container
- **Purpose**: High-quality video recording

#### **5. Data Synchronization**

The separate process approach ensures perfect synchronization between live display and recording:

- **Same Input Source**: Both processes receive the same RGBA frame data
- **Parallel Processing**: Main process handles live display, recording process handles encoding
- **Identical Timing**: Both processes maintain the same frame timing and rate
- **No Latency**: Live display doesn't wait for recording encoding

#### **6. Platform-Specific Implementation**

**Main Process (Camera Capture):**
- **macOS**: AVFoundation with UYVY422 → RGBA conversion
- **Windows**: DirectShow with YUY2 → RGBA conversion

**Recording Process (Separate FFmpeg):**
- **macOS**: `h264_videotoolbox` hardware encoder
- **Windows**: `h264_nvenc` hardware encoder
- **Both**: RGBA input from stdin, MKV output

#### **7. Key Advantages of This Approach**

1. **Zero Latency**: Live display gets frames immediately without waiting for encoding
2. **Perfect Sync**: Recording and live display are perfectly synchronized
3. **Quality Preservation**: Recording quality is never compromised by live display needs
4. **Process Isolation**: Recording failures don't affect live display
5. **Hardware Acceleration**: Each process can use optimal hardware acceleration
6. **Memory Efficiency**: No need to duplicate or buffer frames between processes

#### **8. Data Processing Pipeline**

```
Camera Frame (UYVY422/YUY2)
         ↓
   Main FFmpeg Process
         ↓
   RGBA Conversion
         ↓
    ┌─────────┴─────────┐
    ↓                   ↓
Live Display        RGBA Recorder Manager
(RGBA output)      (Separate Process)
    ↓                   ↓
Game Engine         H.264 Encoding
(Real-time)         (Hardware accelerated)
    ↓                   ↓
UI Display          MKV Container
                    (High quality)
```

## Recording Timing and PTS Management

### **How Timing is Used for Recording and PTS Generation**

The system implements sophisticated timing mechanisms to ensure accurate frame synchronization and proper PTS (Presentation Time Stamp) generation for the recorded video:

#### **1. FFmpeg PTS Generation Parameters**

The recording process uses specific FFmpeg parameters to ensure accurate timing:

```bash
# Critical timing parameters for PTS generation
-use_wallclock_as_timestamps 1    # Use real-time arrival for PTS calculation
-fflags +genpts                   # Generate presentation timestamps
-fps_mode passthrough             # Preserve original frame timing (VFR)
-video_track_timescale 90000      # Standard video timescale for precise timing
-avoid_negative_ts make_zero      # Handle timing issues gracefully
```

**PTS Generation Breakdown:**
- **`-use_wallclock_as_timestamps 1`**: Uses real-time frame arrival for PTS calculation
- **`-fflags +genpts`**: Explicitly generates presentation timestamps
- **`-fps_mode passthrough`**: **No frame rate enforcement** - preserves original frame timing exactly as received
- **`-video_track_timescale 90000`**: Standard MPEG timescale (90kHz) for precise timing

#### **2. Frame Timing Collection System**

The system collects precise frame timing data using the `UnifiedFrameTimingManager`:

```csharp
// High-precision timestamp collection for each frame
var currentTime = UnifiedFrameTimingManager.GetHighPrecisionTimestamp();

// Create standardized frame time entry
var frameTime = UnifiedFrameTimingManager.CreateFrameTime(
    recordingFrameCounter, currentTime, recordingStartTime);

// Store frame timing for metadata generation
frameTimes.Add(frameTime);
```

**Timing Data Structure:**
```csharp
public class FrameTime
{
    public int Frame { get; set; }           // Frame number (0-based)
    public DateTime Time { get; set; }       // Absolute timestamp
    public double Seconds { get; set; }      // Time since recording start
}
```

#### **3. Wallclock-Driven Timing Architecture**

The system uses wallclock timestamps to ensure recording frame rate matches source exactly:

**Timing Flow:**
1. **Camera Input**: Frames arrive at camera's natural rate (e.g., 30fps)
2. **Frame Processing**: Each frame gets high-precision timestamp
3. **PTS Calculation**: FFmpeg uses frame arrival time for PTS generation
4. **Output Synchronization**: Recorded video maintains exact source timing

**Key Benefits:**
- **Accurate Frame Rate**: Recording matches camera's actual frame rate
- **Timing Preservation**: Variable frame rates are maintained
- **Synchronization**: Live display and recording are perfectly synchronized

#### **4. GOP and Keyframe Timing Optimization**

The system optimizes keyframe placement for seeking performance:

```csharp
// Calculate optimal GOP size based on frame rate
int gop = Math.Max(1, (int)Math.Round(frameRate * 0.1f)); // 0.1s GOP

// FFmpeg parameters for keyframe optimization
-g {gop} -keyint_min {gop}                                    # GOP size control
-force_key_frames "expr:gte(t,n_forced*0.1)"                  # Keyframe every 0.1s
```

**GOP Strategy:**
- **0.1 Second Intervals**: Keyframes placed every 0.1 seconds for optimal seeking
- **Frame Rate Adaptive**: GOP size automatically adjusts to camera frame rate
- **Seeking Performance**: Optimized for race analysis and replay functionality

**Important Note:** The GOP calculation uses the `frameRate` parameter passed to the recorder, but this is **only used for keyframe placement optimization**. The actual output video maintains the **original frame timing** from the input stream without forcing a specific frame rate.

## Keyframe Interval Creation and GOP Management

### **How Keyframe Intervals are Calculated and Managed**

The system implements sophisticated keyframe interval management to optimize seeking performance and video quality:

#### **1. GOP Size Calculation**

The Group of Pictures (GOP) size is dynamically calculated based on the source frame rate:

```csharp
// Calculate optimal GOP size based on frame rate
int gop = Math.Max(1, (int)Math.Round(frameRate * 0.1f)); // 0.1s GOP at the configured/measured fps
```

**GOP Calculation Breakdown:**
- **Base Interval**: 0.1 seconds (100ms) target interval
- **Frame Rate Multiplication**: `frameRate * 0.1f` converts time to frame count
- **Rounding**: `Math.Round()` ensures whole frame intervals
- **Minimum Protection**: `Math.Max(1, ...)` prevents GOP size of 0

**Examples:**
- **30fps Camera**: `30 * 0.1 = 3` frames → GOP size = 3 frames
- **60fps Camera**: `60 * 0.1 = 6` frames → GOP size = 6 frames
- **24fps Camera**: `24 * 0.1 = 2.4` → rounded to 2 frames → GOP size = 2 frames

#### **2. FFmpeg Keyframe Control Parameters**

The system uses multiple FFmpeg parameters to ensure consistent keyframe placement:

```bash
# GOP size control
-g {gop} -keyint_min {gop}                                    # Set GOP size and minimum keyframe interval

# Force keyframe expression
-force_key_frames "expr:gte(t,n_forced*0.1)"                  # Keyframe at least every 0.1s
```

**Parameter Explanation:**
- **`-g {gop}`**: Sets the maximum GOP size (maximum frames between keyframes)
- **`-keyint_min {gop}`**: Sets the minimum keyframe interval (prevents too-frequent keyframes)
- **`-force_key_frames "expr:gte(t,n_forced*0.1)"`**: Forces a keyframe every 0.1 seconds regardless of GOP size

#### **3. Dual Keyframe Control Strategy**

The system implements a two-layer approach to keyframe management:

**Layer 1: Frame-Based Control (GOP)**
```bash
-g {gop} -keyint_min {gop}
```
- **Purpose**: Controls keyframe frequency based on frame count
- **Benefit**: Optimizes compression efficiency
- **Example**: With 30fps and 0.1s target → GOP = 3 frames

**Layer 2: Time-Based Control (Force Expression)**
```bash
-force_key_frames "expr:gte(t,n_forced*0.1)"
```
- **Purpose**: Ensures keyframes at regular time intervals
- **Benefit**: Guarantees seeking performance regardless of frame rate variations
- **Example**: Forces keyframe every 100ms even if frame rate drops

#### **4. Keyframe Expression Logic**

The `-force_key_frames` expression uses FFmpeg's expression language:

```bash
"expr:gte(t,n_forced*0.1)"
```

**Expression Breakdown:**
- **`t`**: Current timestamp in seconds
- **`n_forced`**: Number of forced keyframes so far
- **`n_forced*0.1`**: Expected time for next keyframe (0.1s intervals)
- **`gte(t,n_forced*0.1)`**: True when current time ≥ expected keyframe time

**How It Works:**
1. **Start**: `n_forced = 0`, so `0.1 * 0 = 0` seconds
2. **First Keyframe**: When `t ≥ 0`, first keyframe is forced
3. **Subsequent**: `n_forced = 1`, so `0.1 * 1 = 0.1` seconds
4. **Continue**: Keyframes forced at 0.0s, 0.1s, 0.2s, 0.3s, etc.

#### **5. Seeking Performance Optimization**

The keyframe strategy is specifically designed for FPV racing applications:

**Racing-Specific Benefits:**
- **0.1 Second Precision**: Allows seeking to any 100ms interval
- **Frame-Accurate Seeking**: Within the GOP size (typically 2-6 frames)
- **Race Analysis**: Easy navigation to specific race moments
- **Replay Functionality**: Smooth seeking during video playback

**Seeking Performance:**
- **Best Case**: Seeking to keyframe → instant display
- **Worst Case**: Seeking to middle of GOP → decode from previous keyframe
- **Typical Performance**: Seeking within 50ms of target time

#### **6. Compression Efficiency vs. Seeking Performance**

The system balances compression efficiency with seeking performance:

**Compression Efficiency:**
- **Larger GOPs**: Better compression (fewer keyframes)
- **Smaller GOPs**: Worse compression (more keyframes)

**Seeking Performance:**
- **Larger GOPs**: Slower seeking (more frames to decode)
- **Smaller GOPs**: Faster seeking (fewer frames to decode)

**Optimal Balance:**
- **0.1 Second Target**: Provides good seeking performance
- **Frame Rate Adaptive**: Automatically adjusts to camera capabilities
- **Hardware Acceleration**: Minimizes encoding overhead

#### **7. Hardware vs. Software Encoding Impact**

Keyframe management differs between hardware and software encoding:

**Hardware Encoding (VideoToolbox, NVENC, etc.):**
- **GOP Control**: Hardware encoders respect GOP parameters
- **Force Keyframes**: May have limitations on expression-based forcing
- **Performance**: Faster encoding with consistent keyframe placement

**Software Encoding (libx264, etc.):**
- **GOP Control**: Full control over all keyframe parameters
- **Force Keyframes**: Complete expression language support
- **Performance**: Slower encoding but maximum flexibility

**System Adaptation:**
```csharp
// Detect hardware encoding acceleration
var encodingConfig = HardwareAccelerationDetector.DetectEncoding(ffmpegMediaFramework);
string encodingArgs = HardwareAccelerationDetector.GetEncodingArgs(encodingConfig);

// Hardware encoders may modify keyframe behavior
if (encodingConfig.IsAvailable)
{
    Tools.Logger.VideoLog.LogCall(this, $"✓ Using hardware encoding ({encodingConfig.DisplayName}) for recording");
}
else
{
    Tools.Logger.VideoLog.LogCall(this, $"Using software encoding ({encodingConfig.DisplayName}) for recording");
}
```

#### **8. Keyframe Validation and Monitoring**

The system monitors keyframe placement during recording:

**Logging and Verification:**
- **GOP Size Logging**: Records calculated GOP size for each recording
- **FFmpeg Output**: Monitors FFmpeg stderr for keyframe-related messages
- **Performance Metrics**: Tracks encoding performance and keyframe frequency

**Example Log Output:**
```
RGBA Recording ffmpeg command (using VideoToolbox): 
-f rawvideo -pix_fmt rgba -s 1280x720 -use_wallclock_as_timestamps 1 
-fflags +genpts -i pipe:0 -c:v h264_videotoolbox -g 3 -keyint_min 3 
-force_key_frames "expr:gte(t,n_forced*0.1)" -pix_fmt yuv420p 
-fps_mode passthrough -video_track_timescale 90000 -avoid_negative_ts make_zero 
-movflags +faststart -y "output.mkv"
```

This comprehensive keyframe management ensures optimal seeking performance for race analysis while maintaining efficient video compression.

#### **5. Cross-Platform Timing Consistency**

The `UnifiedFrameTimingManager` ensures identical timing behavior across platforms:

```csharp
public static DateTime GetHighPrecisionTimestamp()
{
    // Use DateTime.UtcNow for consistent precision across platforms
    return DateTime.UtcNow.ToLocalTime();
}

public static FrameTime CreateFrameTime(int frameNumber, DateTime currentTime, DateTime recordingStartTime)
{
    var timeSinceStart = currentTime - recordingStartTime;
    return new FrameTime
    {
        Frame = frameNumber,
        Time = currentTime,
        Seconds = timeSinceStart.TotalSeconds // Consistent precision
    };
}
```

**Platform Consistency Features:**
- **Unified Timestamps**: Same timing source across Windows and macOS
- **Consistent Precision**: Identical timing calculations
- **Cross-Platform Validation**: Frame timing consistency validation

#### **6. Frame Rate Detection and Validation**

The system automatically detects and validates actual frame timing (not frame rates):

```csharp
// Track actual frame timing for debugging and verification
if (recordingFrameCounter % 100 == 0) // Log every 100 frames
{
    double intervalMs = (currentTime - lastFrameWriteTime).TotalMilliseconds;
    double actualFps = (1000.0 / intervalMs) * 100; // 100 frames over interval
    double avgFps = recordingFrameCounter / totalSeconds;
    
    Tools.Logger.VideoLog.LogCall(this, 
        $"RECORDING TIMING: Frame {recordingFrameCounter}, " +
        $"Recent: {actualFps:F3}fps, Average: {avgFps:F3}fps, " +
        $"PerFrame: {intervalMs/100:F2}ms (wallclock-driven)");
}
```

**Frame Timing Monitoring:**
- **Real-Time Detection**: Monitors actual frame intervals during recording
- **Performance Validation**: Ensures timing consistency throughout the recording
- **Debug Logging**: Comprehensive timing information for troubleshooting
- **No Frame Rate Enforcement**: The system preserves original timing without forcing a specific frame rate

#### **7. PTS and Timing Validation**

The system validates timing consistency to detect platform-specific issues:

```csharp
public static bool ValidateFrameTimingConsistency(FrameTime[] frameTimes, float expectedFrameRate)
{
    var sortedFrames = frameTimes.OrderBy(f => f.Time).ToArray();
    var totalDuration = (sortedFrames.Last().Time - sortedFrames.First().Time).TotalSeconds;
    var averageFrameRate = (sortedFrames.Length - 1) / totalDuration;
    
    var frameRateDifference = Math.Abs(averageFrameRate - expectedFrameRate);
    var isConsistent = frameRateDifference < (expectedFrameRate * 0.1); // Within 10%
    
    return isConsistent;
}
```

**Validation Criteria:**
- **Frame Rate Accuracy**: Within 10% of expected frame rate
- **Timing Consistency**: Smooth frame intervals throughout recording
- **Platform Detection**: Identifies timing issues specific to Windows/macOS

#### **8. Metadata Generation with Timing Data**

Frame timing data is used to generate comprehensive metadata:

**`.recordinfo.xml` Structure:**
```xml
<FrameTimes>
    <FrameTime Frame="1" Time="2024-01-01T12:00:00.000" Seconds="0.000" />
    <FrameTime Frame="2" Time="2024-01-01T12:00:00.033" Seconds="0.033" />
    <FrameTime Frame="3" Time="2024-01-01T12:00:00.067" Seconds="0.067" />
    <!-- ... continues for all frames ... -->
</FrameTimes>
```

**Metadata Benefits:**
- **Frame-Accurate Seeking**: Precise timeline positioning for race analysis
- **Timing Verification**: Validate recording accuracy against source
- **Cross-Platform Compatibility**: Consistent timing data across all platforms

### **FFmpeg File Management During Start/Stop**

The system carefully manages FFmpeg output file creation and access during recording start/stop operations:

#### **Recording Start - File Creation**
```csharp
// When recording starts, dual-stream recording is activated in existing FFmpeg process
if (Recording && !string.IsNullOrEmpty(recordingFilename))
{
    string recordingPath = Path.GetFullPath(recordingFilename);
    
    // FFmpeg command includes output file path for recording stream
    ffmpegArgs = $"... -f matroska \"{recordingPath}\"";
    
    // Dual-stream recording activated without process restart
    // Live stream continues, recording stream added
}
```

**File Creation Process:**
1. **Filename Generation**: `GetRecordingFilename()` creates `.mkv` path in race directory
2. **Dual-Stream Activation**: Existing FFmpeg process switches to dual-stream mode
3. **File Initialization**: Recording stream begins writing to specified MKV output path
4. **Stream Activation**: Both live display and recording streams become active

#### **Recording Stop - File Finalization**
```csharp
// Stop recording stream while keeping live stream active
bool isRecordingMKV = Recording && !string.IsNullOrEmpty(recordingFilename) && recordingFilename.EndsWith(".mkv");

if (isRecordingMKV)
{
    // Stop recording stream, allow FFmpeg to finalize MKV file
    // Live stream continues uninterrupted
    // FFmpeg process remains running for live display
}
```

**File Finalization Process:**
1. **SIGINT Signal**: System sends interrupt signal to FFmpeg
2. **Graceful Shutdown**: FFmpeg completes current frame encoding
3. **File Finalization**: MKV container headers and metadata written
4. **Process Exit**: FFmpeg exits cleanly with complete file
5. **Fallback Kill**: Force kill if graceful shutdown times out

#### **File Access Management**

**During Recording:**
- **Exclusive Access**: FFmpeg has exclusive write access to `.mkv` file
- **Live Updates**: File grows continuously as frames are encoded
- **No Read Access**: Other processes cannot read incomplete file

**After Recording:**
- **File Complete**: MKV file is fully finalized and seekable
- **Read Access**: UI can immediately access file for playback
- **Metadata Generation**: `.recordinfo.xml` created alongside video file
- **Live Stream Continues**: FFmpeg remains running for live display (no output file)

#### **Error Handling and Recovery**

```csharp
try
{
    // Attempt graceful shutdown
    SendGracefulShutdownSignal();
    if (!process.WaitForExit(5000))
    {
        // Fallback to force kill
        process.Kill();
        process.WaitForExit(3000);
    }
}
catch (Exception ex)
{
    // Emergency cleanup
    process.Kill();
    // Note: File may be corrupted if graceful shutdown failed
}
```

**Recovery Scenarios:**
1. **Graceful Success**: Clean MKV file with proper headers
2. **Timeout Fallback**: Force kill, file may be incomplete
3. **Process Crash**: File corruption possible, requires cleanup
4. **System Interruption**: Partial file may need manual removal

## Current Implementation Status

### **Active Recording Method: Dual-Stream Approach**
The system **currently uses the dual-stream approach** for all recording operations:

1. **Filename Generation**: The `GetRecordingFilename()` method in `VideoManager.cs` explicitly generates `.mkv` files:
   ```csharp
   private string GetRecordingFilename(Race race, FrameSource source)
   {
       // Use .mkv for better seekability and timestamp preservation as per specification
       return Path.Combine(EventDirectory.FullName, race.ID.ToString(), source.VideoConfig.ffmpegId) + ".mkv";
   }
   ```

2. **Recording Pipeline**: When recording is active, the system uses FFmpeg's filter_complex to split the camera input into two streams:
   - **Live Stream**: RGBA output for immediate display (pipe:1)
   - **Recording Stream**: H.264 encoding to MKV container (output file)

3. **Output Format**: The system directly records to MKV format using the dual-stream approach, providing native MKV benefits.

### **Platform-Specific Implementation**
- **macOS**: Uses AVFoundation with VideoToolbox hardware acceleration
- **Windows**: Uses DirectShow with NVENC hardware acceleration
- **Both**: Implement the same dual-stream filter_complex approach

#### Metadata Generation
- **RecordInfo XML**: Automatically generated alongside video files
- **Frame Timing**: Precise frame-by-frame timing data
- **Recording Parameters**: Camera settings, frame rate, resolution
- **Platform Information**: Hardware acceleration details

### 7. Performance Optimizations

**Memory Management**: Buffer pooling, direct pipe output, minimized garbage collection
**Processing Efficiency**: Hardware acceleration, separate camera/game threads, zero-copy access
**Platform-Specific**: VideoToolbox (macOS), DirectShow + hardware detection (Windows)

## Technical Implementation Details

**Buffer Management**: RGBA buffers (4 bytes per pixel), raw texture buffers for game engine
**Frame Processing**: Camera-driven timing, dual-stream recording via FFmpeg, game engine notification

### Recording Command Generation

**Dual-Stream Commands**: FFmpeg uses `filter_complex` to split camera input into live display and recording streams

**macOS**: AVFoundation + VideoToolbox with `h264_videotoolbox` encoder
**Windows**: DirectShow + NVENC with `h264_nvenc` encoder
**Both**: Same dual-stream architecture with platform-specific hardware acceleration

## Configuration and Tuning

**Camera Settings**: Configurable resolution, auto-detected frame rate, platform-optimized pixel formats
**Recording Quality**: 5Mbps default, H.264 with hardware acceleration, 0.1s GOP intervals
**Performance**: Optimized buffers, single-threaded FFmpeg, automatic hardware fallback

## Troubleshooting and Debugging

**Common Issues**: Hardware acceleration fallback, frame rate mismatches, recording failures
**Debug Logging**: Comprehensive video operations, frame timing, hardware detection
**Performance Monitoring**: Real-time frame rates, memory usage, hardware utilization

## Future Enhancements

**Planned Improvements**: Multi-camera support, H.265/HEVC/AV1 codecs, streaming integration, AI enhancement
**Architecture Evolution**: Modular plugin design, improved Linux support, cloud integration

## Conclusion

The FPVTracksideCore recording system provides high-performance FPV racing video capture through hardware acceleration, optimized buffer management, and precise timing control. The dual-stream architecture ensures recording quality is never compromised by live display requirements, while camera-driven timing guarantees accurate frame synchronization.
