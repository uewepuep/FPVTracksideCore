# Frame Timing Unification Changes

## Problem Summary

Windows recorded videos were finishing playback at approximately 30% progress bar completion, while the video continued playing. This was caused by inconsistent frame timing data generation between Windows and Mac platforms, leading to mismatched XML timing data and actual video duration.

## Root Cause

1. **Platform-specific timing differences**: Windows and Mac used different timestamp precision and timing mechanisms
2. **Inconsistent duration calculation**: XML frame timing duration didn't match actual MP4 file duration
3. **Progress bar calculation mismatch**: Progress calculation used XML timing while video playback used actual file duration

## Solution: Unified Frame Timing Management

### New Components

#### 1. UnifiedFrameTimingManager.cs
- **Purpose**: Centralize frame timing logic across all platforms
- **Key Features**:
  - Consistent high-precision timestamp generation
  - Unified frame time creation logic
  - Standardized duration calculation
  - Frame timing validation to detect inconsistencies

### Modified Components

#### 2. RgbaRecorderManager.cs
- **Changes**:
  - Uses `UnifiedFrameTimingManager.InitializeRecordingStartTime()` for consistent start times
  - Uses `UnifiedFrameTimingManager.GetHighPrecisionTimestamp()` for frame timestamps
  - Uses `UnifiedFrameTimingManager.CreateFrameTime()` for frame time entries

#### 3. FfmpegFrameSource.cs
- **Changes**:
  - Uses unified timestamp generation
  - Uses unified frame time creation logic
  - Ensures consistent timing data collection across platforms

#### 4. FfmpegVideoFileFrameSource.cs
- **Changes**:
  - Uses `UnifiedFrameTimingManager.CalculateVideoDuration()` for duration calculation
  - Validates frame timing consistency to detect platform issues
  - Removed platform-specific timing code (`isMac` variable)

#### 5. ReplayNode.cs
- **Changes**:
  - Uses unified duration calculation for progress bar timeline
  - Ensures consistent progress calculation across platforms

## Technical Details

### Timing Consistency
- All platforms now use `DateTime.UtcNow.ToLocalTime()` for consistent precision
- Frame time calculation uses identical logic: `(currentTime - recordingStartTime).TotalSeconds`
- Duration calculation prioritizes XML frame timing data over file-based duration

### Validation
- Added frame timing validation to detect inconsistencies
- Logs warnings when frame timing appears inconsistent
- Helps identify platform-specific recording issues

### Platform Independence
- Removed unnecessary platform-specific conditionals
- Unified WMV file handling across platforms
- Consistent FFmpeg parameter generation

## Expected Results

1. **Consistent playback timing**: Windows and Mac videos should have identical progress bar behavior
2. **Accurate duration calculation**: XML timing data should match actual video duration
3. **Reliable progress indication**: Progress bar should accurately reflect playback position
4. **Cross-platform compatibility**: Videos recorded on one platform should play correctly on another

## Testing Recommendations

1. **Record test videos** on both Windows and Mac with identical settings
2. **Compare XML timing data** between platforms for consistency
3. **Verify progress bar accuracy** during playback on both platforms
4. **Test cross-platform playback** (Windows-recorded videos on Mac and vice versa)
5. **Monitor logs** for timing validation warnings

## Migration Notes

- No breaking changes to existing APIs
- Backward compatible with existing .recordinfo.xml files
- Enhanced logging provides better debugging information
- Automatic validation detects timing issues

This unification should resolve the Windows playback timing issues by ensuring identical frame timing generation and duration calculation across all platforms.