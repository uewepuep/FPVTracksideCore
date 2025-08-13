# HLS Disable Configuration

This document explains how to disable HLS (HTTP Live Streaming) functionality in FPVTracksideCore while keeping all the code intact.

## What Happens When HLS is Disabled

When HLS is disabled:
- ✅ **RGBA live processing continues** - Camera feeds still work for local display
- ✅ **Recording functionality remains** - RGBA recording still works
- ❌ **HLS streaming is disabled** - No HTTP server started
- ❌ **HLS file generation is skipped** - No .m3u8 or .ts files created
- ❌ **Web streaming is unavailable** - No HTTP access to live streams

## How to Disable HLS

### Method 1: Configuration File (Recommended - No Code Changes)

The easiest way is to modify your `data/GeneralSettings.xml` file:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ArrayOfGeneralSettings xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <GeneralSettings>
    <Profile>Profile 1</Profile>
    <HlsEnabled>false</HlsEnabled>
  </GeneralSettings>
</ArrayOfGeneralSettings>
```

**Set `<HlsEnabled>false</HlsEnabled>` to disable HLS**
**Set `<HlsEnabled>true</HlsEnabled>` to enable HLS**

This setting is automatically read when the application starts and HLS will be configured accordingly.

### Method 2: Using the Configuration Class

```csharp
// Disable HLS completely
FfmpegMediaPlatform.HlsConfig.DisableHls();

// Or set the property directly
FfmpegMediaPlatform.HlsConfig.HlsEnabled = false;
```

### Method 3: Direct Property Access

```csharp
// Disable HLS directly on the frame source class
FfmpegMediaPlatform.FfmpegHlsLiveFrameSource.HlsEnabled = false;
```

### Method 4: Runtime Toggle Utility

Use the `HlsToggleUtility` class to toggle HLS at runtime:

```csharp
// Disable HLS and update config file
FfmpegMediaPlatform.HlsToggleUtility.DisableHlsInConfig();

// Enable HLS and update config file
FfmpegMediaPlatform.HlsToggleUtility.EnableHlsInConfig();

// Check status
string summary = FfmpegMediaPlatform.HlsToggleUtility.GetStatusSummary();
Console.WriteLine(summary);
```

### Method 5: Environment Variable

```bash
# Set environment variable before starting the application
export DISABLE_HLS=true
```

Then in your code:
```csharp
string hlsEnvVar = Environment.GetEnvironmentVariable("DISABLE_HLS");
if (!string.IsNullOrEmpty(hlsEnvVar) && hlsEnvVar.ToLower() == "true")
{
    FfmpegMediaPlatform.HlsConfig.DisableHls();
}
```

## Configuration File Approach (NEW!)

The **recommended approach** is to use the configuration file method. Here's how it works:

### 1. **Edit GeneralSettings.xml**
Simply change the value in your `data/GeneralSettings.xml` file:
- `false` = HLS Disabled (Performance Mode)
- `true` = HLS Enabled (Web Streaming Mode)

### 2. **Automatic Configuration**
The application automatically reads this setting on startup and configures HLS accordingly.

### 3. **Runtime Control**
Use `HlsToggleUtility` to change the setting at runtime without editing XML files.

### 4. **Status Checking**
```csharp
// Check if config and runtime are in sync
bool inSync = FfmpegMediaPlatform.HlsToggleUtility.IsConfigInSync();

// Get detailed status
string status = FfmpegMediaPlatform.HlsToggleUtility.GetStatusSummary();
Console.WriteLine(status);
```

## When to Disable HLS

Consider disabling HLS when:
- **Performance is critical** - Eliminates HTTP server overhead
- **Memory is limited** - No HLS file generation or buffering
- **Web streaming not needed** - Only local display required
- **Security concerns** - No HTTP server exposure
- **Resource optimization** - Reduce CPU/memory usage

## Performance Benefits

Disabling HLS provides:
- **Lower CPU usage** - No HLS encoding/segmenting
- **Lower memory usage** - No HLS file buffering
- **Lower disk I/O** - No HLS file writing
- **Faster startup** - No HTTP server initialization
- **Simpler FFmpeg commands** - Single output instead of dual output

## Code Changes Made

The following modifications were made to support HLS disabling:

1. **Added `HlsEnabled` flag** to `FfmpegHlsLiveFrameSource`
2. **Added `HlsEnabled` property** to `GeneralSettings` class
3. **Conditional FFmpeg command generation** - Single vs dual output
4. **Conditional HTTP server startup** - Only when HLS enabled
5. **Conditional HLS file handling** - Skip when disabled
6. **Added `HlsConfig` class** - Centralized configuration
7. **Added `HlsToggleUtility` class** - Runtime configuration control
8. **Added example usage** - Multiple ways to disable
9. **Automatic configuration reading** - From GeneralSettings.xml

## Example Usage

### Configuration File Method (Recommended)
```xml
<!-- In data/GeneralSettings.xml -->
<HlsEnabled>false</HlsEnabled>
```

### Runtime Control
```csharp
using FfmpegMediaPlatform;

// At application startup
public void ConfigureVideoSystem()
{
    // HLS is automatically configured from GeneralSettings.xml
    // No code changes needed!
    
    // Check status
    string summary = HlsToggleUtility.GetStatusSummary();
    Console.WriteLine(summary);
}

// Runtime toggle
public void ToggleHls(bool enable)
{
    if (enable)
        HlsToggleUtility.EnableHlsInConfig();
    else
        HlsToggleUtility.DisableHlsInConfig();
}
```

## Reverting Changes

### To Re-enable HLS via Config File:
```xml
<!-- In data/GeneralSettings.xml -->
<HlsEnabled>true</HlsEnabled>
```

### To Re-enable HLS via Code:
```csharp
// Re-enable HLS
HlsConfig.EnableHls();

// Or set directly
FfmpegHlsLiveFrameSource.HlsEnabled = true;
```

## Technical Details

### FFmpeg Command Changes

**When HLS Enabled (Default):**
```bash
ffmpeg -f avfoundation ... -filter_complex "[0:v]split=2[out1][out2];[out1]format=rgba[outpipe];[out2]format=yuv420p[outfile]" -map "[outpipe]" -f rawvideo pipe:1 -map "[outfile]" -c:v h264_videotoolbox ... -f hls stream.m3u8
```

**When HLS Disabled:**
```bash
ffmpeg -f avfoundation ... -f rawvideo -pix_fmt rgba pipe:1
```

### Architecture Changes

- **Dual Output → Single Output**: Eliminates video splitting and HLS encoding
- **HTTP Server**: Conditionally started only when needed
- **File Generation**: HLS files only created when enabled
- **Memory Usage**: Reduced buffer requirements
- **Configuration Integration**: Automatic reading from GeneralSettings.xml

## Troubleshooting

### HLS Still Running After Disable

1. **Check config file**: Verify `data/GeneralSettings.xml` has `<HlsEnabled>false</HlsEnabled>`
2. **Check timing**: Disable HLS before creating video sources
3. **Check scope**: The flag affects new sources, not existing ones
4. **Check sync**: Use `HlsToggleUtility.IsConfigInSync()` to verify

### Performance Not Improved

1. **Verify config value**: Check `HlsToggleUtility.GetStatusSummary()`
2. **Check logs**: Look for "HLS is DISABLED" messages
3. **Restart application**: Configuration changes require restart
4. **Check inheritance**: Ensure you're setting the correct class property

### Recording Issues

1. **RGBA recording**: Still works when HLS is disabled
2. **HLS recording**: Will not work when HLS is disabled
3. **Check recorder type**: Ensure you're using `RgbaRecorderManager`, not `HlsRecorderManager`

### Configuration Issues

1. **Check XML syntax**: Ensure `data/GeneralSettings.xml` is valid XML
2. **Check file permissions**: Ensure the application can read/write the config file
3. **Check file location**: Ensure `data/GeneralSettings.xml` is in the correct directory
4. **Sync status**: Use `HlsToggleUtility.SyncWithConfig()` to fix mismatches

## Support

For issues or questions about HLS disabling:
1. Check the logs for HLS status messages
2. Verify the configuration file setting is correct
3. Use `HlsToggleUtility.GetStatusSummary()` to check status
4. Ensure HLS is disabled before video sources are created
5. Check that you're using the correct recording manager

## Files Modified

- `FfmpegHlsLiveFrameSource.cs` - Added HLS disable functionality
- `GeneralSettings.cs` - Added HlsEnabled property
- `BaseGame.cs` - Added automatic HLS configuration reading
- `HlsConfig.cs` - New configuration class
- `HlsToggleUtility.cs` - New utility for runtime configuration control
- `HlsDisableExample.cs` - Example usage patterns
- `data/GeneralSettings.xml` - Added HlsEnabled setting (set to false)
- `README_HLS_Disable.md` - This documentation file

## Quick Start

1. **Edit `data/GeneralSettings.xml`**:
   ```xml
   <HlsEnabled>false</HlsEnabled>
   ```

2. **Restart the application** - HLS will be automatically disabled

3. **Verify status** - Check logs for "HLS disabled" messages

That's it! No code changes required.
