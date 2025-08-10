# FFmpeg Native Libraries for macOS

This directory contains custom-built FFmpeg libraries for macOS that are used by the FPVTracksideCore application for native video processing.

## Overview

The system provides:
- **ARM64 libraries** for Apple Silicon Macs
- **Intel libraries** for Intel-based Macs
- **Automatic architecture detection** and library selection
- **Direct integration** with FFmpeg.AutoGen for native calls
- **No dependency** on Homebrew or system-installed FFmpeg

## Library Versions

The libraries are built with the following versions:
- **libavutil**: 60.x.x
- **libavcodec**: 62.x.x  
- **libavformat**: 62.x.x
- **libswscale**: 9.x.x
- **libswresample**: 6.x.x
- **libavfilter**: 11.x.x
- **libavdevice**: 62.x.x

## Building the Libraries

### Prerequisites
- macOS with Xcode Command Line Tools
- Git (for cloning FFmpeg source)

### Quick Build
```bash
cd ffmpegMediaPlatform/ffmpeg-builds
./build-and-copy.sh
```

This will build both ARM64 and Intel versions automatically.

### Manual Build

#### ARM64 (Apple Silicon)
```bash
cd ffmpegMediaPlatform/ffmpeg-builds
./build-mac.sh
```

#### Intel x86_64
```bash
cd ffmpegMediaPlatform/ffmpeg-builds
./build-mac-intel.sh
```

## Integration

The libraries are automatically integrated into the build process:

1. **Project files** are configured to copy libraries to `ffmpeg-libs/{architecture}/`
2. **Native loader** automatically detects and uses the correct libraries
3. **Architecture detection** ensures the right libraries are loaded at runtime

## Build Output Structure

After building, the libraries are organized as:
```
ffmpeg-libs/
├── arm64/
│   ├── libavutil.60.dylib
│   ├── libavcodec.62.dylib
│   ├── libavformat.62.dylib
│   ├── libswscale.9.dylib
│   ├── libswresample.6.dylib
│   ├── libavfilter.11.dylib
│   └── libavdevice.62.dylib
└── intel/
    ├── libavutil.60.dylib
    ├── libavcodec.62.dylib
    ├── libavformat.62.dylib
    ├── libswscale.9.dylib
    ├── libswresample.6.dylib
    ├── libavfilter.11.dylib
    └── libavdevice.62.dylib
```

## Usage in Code

The libraries are automatically loaded by `FfmpegNativeLoader.EnsureRegistered()`:

```csharp
// This happens automatically when using FFmpeg.AutoGen
FfmpegNativeLoader.EnsureRegistered();

// Now you can use FFmpeg functions directly
var frame = new AVFrame();
// ... use FFmpeg APIs
```

## Troubleshooting

### Libraries Not Found
- Ensure the build scripts completed successfully
- Check that `ffmpeg-libs/{architecture}/` exists in your build output
- Verify the project files are correctly configured

### Architecture Mismatch
- Build with the correct runtime identifier:
  - `dotnet build -r osx-arm64` for Apple Silicon
  - `dotnet build -r osx-x64` for Intel Macs
  - `dotnet build -r osx` for universal builds

### Runtime Errors
- Check console output for library loading messages
- Ensure all dependencies are present in the library directory
- Verify library permissions and integrity

## Benefits

1. **Performance**: Native libraries provide optimal performance for video processing
2. **Reliability**: No dependency on system-installed FFmpeg versions
3. **Consistency**: Same library versions across all builds
4. **Portability**: Self-contained libraries work on any compatible macOS system
5. **Hardware Acceleration**: Full support for VideoToolbox and AVFoundation

## Maintenance

- **Rebuild libraries** when updating FFmpeg source
- **Test both architectures** after any changes
- **Update version numbers** in the native loader if library versions change
- **Verify compatibility** with target macOS versions 