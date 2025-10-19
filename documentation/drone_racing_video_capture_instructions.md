
# Drone Racing Video Capture and Recording System — Implementation Instructions

## Overview

This document describes how to implement a dual-stream video capture and recording system for drone racing, designed for cross-platform support (macOS and Windows). The system must:

- Continuously capture a live camera feed with accurate timing (PTS).
- Provide a real-time RGBA video stream for an interface (e.g., live display).
- Allow starting and stopping of an H.264 recording **at race start and stop**, without interrupting the live RGBA stream.
- Ensure the recorded video is fully seekable and has correct frame timing and duration metadata.
- Preserve original frame PTS timestamps end-to-end to maintain timing accuracy.
- Support both `.mkv` and `.mp4` recorded files and ensure the replay tab or UI correctly detects and displays files with these extensions.
- **Use hardware acceleration** for H.264 encoding and decoding wherever possible to reduce CPU load and minimize encoding latency.

---

## Architecture

### Components

1. **Always-on Capture Process (Process #1)**  
   - Captures live video from the camera.
   - Splits output into two streams:
     - RGBA raw video stream for interface display.
     - H.264 encoded MPEG-TS stream carrying PTS for recording.
   - Runs continuously regardless of recording state.
   - Uses hardware-accelerated H.264 encoding (e.g., `h264_videotoolbox` on macOS, `h264_nvenc` on NVIDIA GPUs, or `h264_qsv` for Intel Quick Sync) to minimize CPU usage and latency.

2. **Interface Consumer**  
   - Reads RGBA raw video stream continuously for live display.

3. **Recording Process (Process #2)**  
   - Launched only when a race starts.
   - Reads MPEG-TS stream from Process #1.
   - Remuxes or re-encodes the stream into a fully seekable H.264 recording file (`.mkv` recommended, `.mp4` optional).
   - Stops cleanly when the race ends, producing a correctly indexed, seekable recording file.
   - Uses hardware acceleration for encoding or decoding as appropriate.

---

## Implementation Details

### Hardware Acceleration Support

- On **macOS**, use:
  - **VideoToolbox** encoder: specify `-c:v h264_videotoolbox` instead of `libx264` for hardware-accelerated H.264 encoding.
- On **Windows**, options include:
  - **NVIDIA GPUs**: use `-c:v h264_nvenc`.
  - **Intel Quick Sync**: use `-c:v h264_qsv`.
  - **AMD GPUs**: use `-c:v h264_amf` (if available).
- Hardware acceleration significantly reduces CPU usage and encoding latency, improving real-time performance for live display and recording.

---

## Step-by-Step Instructions

### 1. Setup Named Pipes (macOS / Linux example)

```bash
mkfifo /tmp/rgba_pipe
mkfifo /tmp/h264_pipe
```

### 2. Always-On Capture Process (Process #1)

**macOS example with hardware acceleration:**

```bash
ffmpeg -f avfoundation -i "0" \
  -fflags nobuffer -flags low_delay \
  -vsync passthrough -copyts \
  -filter_complex "[0:v]split=2[out1][out2]" \
  -map "[out1]" -pix_fmt rgba -f rawvideo /tmp/rgba_pipe \
  -map "[out2]" -c:v h264_videotoolbox -preset ultrafast -tune zerolatency -f mpegts /tmp/h264_pipe
```

**Windows example with NVIDIA hardware acceleration:**

```powershell
ffmpeg -f dshow -i video="Your Camera Name" `
  -fflags nobuffer -flags low_delay `
  -vsync passthrough -copyts `
  -filter_complex "[0:v]split=2[out1][out2]" `
  -map "[out1]" -pix_fmt rgba -f rawvideo \.\pipe
gba_pipe `
  -map "[out2]" -c:v h264_nvenc -preset llhp -tune zerolatency -f mpegts \.\pipe\h264_pipe
```

---

### 3. Interface Consumer (Continuous RGBA feed)

The interface (UI) reads the RGBA pipe continuously for real-time display:

```bash
ffmpeg -i /tmp/rgba_pipe \
  -pix_fmt rgba -f rawvideo pipe:1
```

(On Windows, replace `/tmp/rgba_pipe` with the named pipe path `\.\pipe
gba_pipe`.)

---

### 4. Recording Process (Process #2) — Start/Stop on Race Events

- **Start recording when a race starts** by launching:

```bash
ffmpeg -i /tmp/h264_pipe \
  -copyts -vsync passthrough -avoid_negative_ts make_zero \
  -c copy \
  -f matroska recording_$(date +%Y%m%d_%H%M%S).mkv
```

- To re-encode with hardware acceleration (optional), for example on macOS:

```bash
ffmpeg -i /tmp/h264_pipe \
  -copyts -vsync passthrough -avoid_negative_ts make_zero \
  -c:v h264_videotoolbox -preset ultrafast -tune zerolatency \
  -c:a copy \
  -f matroska recording_$(date +%Y%m%d_%H%M%S).mkv
```

- **Stop recording when the race ends** by cleanly terminating this FFmpeg process.  
- The resulting recording file will be fully seekable, correctly timed, and efficiently encoded using hardware acceleration.

---

## Additional Notes

- Always verify hardware acceleration support on the target machine. FFmpeg may list available encoders with `ffmpeg -encoders | grep h264`.
- Choose hardware encoder names based on GPU/OS:
  - macOS: `h264_videotoolbox`
  - Windows NVIDIA: `h264_nvenc`
  - Windows Intel: `h264_qsv`
  - Windows AMD: `h264_amf`
- Using hardware acceleration lowers CPU load and reduces encoding latency, critical for real-time video capture and recording workflows.

---

**End of Instructions**
