#!/bin/bash
set -e

FFMPEG_REPO="https://github.com/FFmpeg/FFmpeg.git"
FFMPEG_DIR="$PWD/ffmpeg"
BUILD_INTEL="$PWD/build-intel"

# Clone FFmpeg if not already cloned
if [ ! -d "$FFMPEG_DIR" ]; then
  echo "Cloning FFmpeg repository..."
  git clone --depth 1 $FFMPEG_REPO "$FFMPEG_DIR"
fi

cd "$FFMPEG_DIR"

echo "Cleaning previous builds..."
make clean || true

echo "Building Intel x86_64 FFmpeg on Apple Silicon Mac..."

rm -rf "$BUILD_INTEL"
mkdir -p "$BUILD_INTEL"

export CFLAGS="-arch x86_64"
export LDFLAGS="-arch x86_64"

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
  --enable-decoder=h264,aac,hevc,rawvideo \
  --enable-encoder=rawvideo \
  --enable-hwaccel=h264_videotoolbox,hevc_videotoolbox \
  --enable-filter=scale,format"

./configure --prefix="$BUILD_INTEL" $CONFIGURE_FLAGS --extra-ldflags="-framework VideoToolbox -framework AVFoundation" --arch=x86_64 --target-os=darwin --enable-cross-compile

make -j$(sysctl -n hw.ncpu)
make install

echo "Intel build complete at $BUILD_INTEL"
