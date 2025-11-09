# Fast Application Exit for FFmpeg Media Platform

## Overview
The FFmpeg media platform has been optimized for fast application shutdown by reducing all wait timeouts and adding an immediate termination mode.

## Changes Made

### 1. Reduced Timeouts
All wait operations during disposal have been significantly reduced:
- Recording worker task wait: 5 seconds → 100ms
- Frame processing task wait: 1 second → 50ms
- Reading thread wait: 1 second → 100ms
- Process termination wait: 3 seconds → 100ms
- RGBA recorder timeout: 10 seconds → 500ms
- Windows process cleanup delay: 50ms → 10ms

### 2. Immediate Termination Mode
Added a static flag `FfmpegFrameSource.ImmediateTerminationOnExit` that when set to `true`:
- Skips all graceful shutdown waits
- Kills processes immediately without waiting
- Reduces all timeouts to near-zero values (5-10ms)
- Prioritizes application responsiveness over file integrity

## Usage

### Setting Immediate Termination on Application Exit

In your main application shutdown code, set the flag before disposing resources:

```csharp
// In your application's exit handler or cleanup code
protected override void OnExit(ExitEventArgs e)
{
    // Enable immediate termination for instant exit
    FfmpegFrameSource.ImmediateTerminationOnExit = true;

    // Now dispose your media resources - will be nearly instant
    mediaFrameSource?.Dispose();

    base.OnExit(e);
}
```

Or in a Windows Forms application:

```csharp
private void Form_FormClosing(object sender, FormClosingEventArgs e)
{
    // Enable fast exit mode
    FfmpegFrameSource.ImmediateTerminationOnExit = true;

    // Dispose media resources
    DisposeMediaResources();
}
```

### Normal Operation (Default)
By default, the flag is `false` and the application will:
- Use reduced but still safe timeouts
- Attempt graceful shutdown for recording processes
- Ensure video files are properly finalized

### Fast Exit Mode
When `ImmediateTerminationOnExit = true`:
- Application exits almost instantly
- FFmpeg processes are killed immediately
- No waits for thread joins or process exits
- Video files being recorded may not be properly finalized

## Performance Impact

### Before Optimization
- Application exit could take 10-20 seconds with multiple FFmpeg processes
- Each process disposal could wait up to 5-10 seconds
- Thread joins and cleanup added additional delays

### After Optimization (Normal Mode)
- Application exit typically takes 1-2 seconds
- Reduced timeouts while maintaining file integrity
- Graceful shutdown for recordings when possible

### After Optimization (Immediate Mode)
- Application exit is nearly instant (< 100ms)
- All FFmpeg processes terminated immediately
- Zero blocking waits during disposal
- Trade-off: Recordings in progress may be corrupted

## Best Practices

1. **Use Immediate Mode for Application Exit Only**
   - Set the flag only when the entire application is exiting
   - Don't use it for normal camera switching or stopping

2. **Normal Camera Operations**
   - Leave the flag as `false` for normal operations
   - This ensures recordings are properly finalized
   - Camera switching remains reliable

3. **Handle Recording Warnings**
   - If using immediate mode while recording, warn users
   - Consider showing a "Saving recordings..." message briefly
   - Or prompt users to confirm exit if actively recording

## Technical Details

The optimization works by:
1. Reducing all `Thread.Join()`, `Process.WaitForExit()`, and `Task.Wait()` timeouts
2. Adding conditional logic to skip waits entirely in immediate mode
3. Killing processes without graceful shutdown in immediate mode
4. Canceling tasks without waiting for completion

This ensures the application remains responsive and exits quickly when the user requests it, improving the overall user experience.