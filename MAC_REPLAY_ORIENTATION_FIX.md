# Mac Replay Orientation Fix

## Problem
Video recordings made on Mac were playing back upside down in the replay screen.

## Root Cause
- On Mac, cameras output frames upside down by default (this is normal for AVFoundation)
- During recording, frames are saved with their native orientation (upside down)
- During replay, the video file needs to be flipped to display correctly

## Solution
Modified `FfmpegVideoFileFrameSource.BuildVideoFilter()` to handle Mac replay correctly:

### Mac Behavior:
- **Recording**: Frames are saved as-is (upside down) from the camera
- **Replay**: Apply `vflip` by default to correct the orientation
- If user sets `VideoConfig.Flipped = true`, then don't apply vflip (shows upside down)

### Windows Behavior (unchanged):
- **Recording**: Frames are saved right-side up from the camera
- **Replay**: Only apply `vflip` if `VideoConfig.Flipped = true`

## Code Changes
File: `/ffmpegMediaPlatform/FfmpegVideoFileFrameSource.cs`

```csharp
// Mac recordings are stored with camera's native orientation (upside down)
// So we need to flip them during playback to display correctly
if (isMac)
{
    if (!VideoConfig.Flipped)  // Note: inverted logic for Mac
        filters.Add("vflip");
}
else
{
    if (VideoConfig.Flipped)   // Normal logic for Windows
        filters.Add("vflip");
}
```

## Testing
1. Record a video on Mac
2. Play it back in replay mode
3. Video should display right-side up

## Important Notes
- This fix only affects replay playback, not live camera display
- Windows behavior remains unchanged
- The fix is platform-specific and automatically detected at runtime