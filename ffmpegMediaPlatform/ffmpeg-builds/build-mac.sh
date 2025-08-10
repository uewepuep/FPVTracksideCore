#!/bin/bash
set -e

FFMPEG_DIR="$PWD/ffmpeg"
BUILD_ARM64="$PWD/build-arm64"

# Check if ffmpeg source exists, clone if not
if [ ! -d "$FFMPEG_DIR" ]; then
    echo "=== Cloning FFmpeg source ==="
    git clone https://git.ffmpeg.org/ffmpeg.git
    cd "$FFMPEG_DIR"
    echo "=== Checking out version 7.1.1 ==="
    git checkout n7.1.1
    cd ..
else
    echo "=== FFmpeg source already exists, checking version ==="
    cd "$FFMPEG_DIR"
    CURRENT_VERSION=$(git describe --tags --abbrev=0 2>/dev/null || echo "unknown")
    if [ "$CURRENT_VERSION" != "n7.1.1" ]; then
        echo "=== Updating to version 7.1.1 ==="
        git fetch origin
        git checkout n7.1.1
    else
        echo "=== Already on version 7.1.1 ==="
    fi
    cd ..
fi

CONFIGURE_FLAGS="\
  --enable-shared \
  --disable-static \
  --disable-programs \
  --disable-doc \
  --disable-debug \
  --enable-avcodec \
  --enable-avformat \
  --enable-avutil \
  --enable-swscale \
  --enable-swresample \
  --enable-protocol=file \
  --enable-indev=avfoundation \
  --enable-decoder=h264,aac,hevc,rawvideo,h264_videotoolbox,hevc_videotoolbox \
  --enable-encoder=rawvideo,h264_videotoolbox,hevc_videotoolbox \
  --enable-hwaccel=h264_videotoolbox,hevc_videotoolbox \
  --enable-filter=scale,format"

cd "$FFMPEG_DIR"

echo "=== Configure FFmpeg ==="
./configure --prefix="$BUILD_ARM64" $CONFIGURE_FLAGS --extra-ldflags="-framework VideoToolbox -framework AVFoundation"

echo "=== Clean source ==="
make clean

echo "=== Build Apple Silicon arm64 ==="
rm -rf "$BUILD_ARM64"
mkdir -p "$BUILD_ARM64"
export CFLAGS="-arch arm64"
export LDFLAGS="-arch arm64"
make -j$(sysctl -n hw.ncpu)
make install

echo "Build complete at $BUILD_ARM64"

