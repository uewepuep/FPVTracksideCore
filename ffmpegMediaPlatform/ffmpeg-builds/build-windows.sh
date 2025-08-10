#!/bin/bash
set -e

echo "FFmpeg 7.1.1 Windows Build Script"
echo "=================================="

# Check for required build tools
check_build_tools() {
    echo "Checking build environment..."
    
    # Check for compiler
    if command -v gcc >/dev/null 2>&1; then
        echo "✓ GCC found: $(gcc --version | head -n1)"
    elif command -v x86_64-w64-mingw32-gcc >/dev/null 2>&1; then
        echo "✓ MinGW GCC found: $(x86_64-w64-mingw32-gcc --version | head -n1)"
        export CC=x86_64-w64-mingw32-gcc
        export CXX=x86_64-w64-mingw32-g++
    else
        echo "❌ No suitable compiler found!"
        echo "Please install MSYS2 and run:"
        echo "  pacman -S base-devel pkg-config yasm nasm mingw-w64-x86_64-toolchain"
        echo "Or run the install-build-tools.ps1 script as administrator"
        exit 1
    fi
    
    # Check for make
    if ! command -v make >/dev/null 2>&1; then
        echo "❌ make not found! Please install build tools."
        exit 1
    fi
    
    echo "✓ Build tools check complete"
}

FFMPEG_DIR="$PWD/ffmpeg"
BUILD_DIR="$PWD/build-windows"

# Run build tools check
check_build_tools

if [ ! -d "$FFMPEG_DIR" ]; then
    echo "Cloning FFmpeg repository..."
    git clone https://git.ffmpeg.org/ffmpeg.git "$FFMPEG_DIR"
fi

cd "$FFMPEG_DIR"
echo "Preparing FFmpeg source..."
git reset --hard
git clean -fdx
git fetch --tags
git checkout n7.1.1
echo "✓ FFmpeg 7.1.1 source ready"

# Set up build environment
export PATH="/mingw64/bin:$PATH"
export PKG_CONFIG_PATH="/mingw64/lib/pkgconfig"

./configure \
  --prefix="$BUILD_DIR" \
  --enable-shared \
  --disable-static \
  --disable-programs \
  --disable-doc \
  --disable-debug \
  --enable-pic \
  --enable-avcodec \
  --enable-avformat \
  --enable-avutil \
  --enable-swscale \
  --enable-swresample \
  --enable-avfilter \
  --enable-avdevice \
  --enable-protocol=file \
  --enable-indev=dshow \
  --enable-decoder=h264,aac,hevc,rawvideo,mjpeg \
  --enable-encoder=rawvideo,libx264,h264_nvenc,h264_qsv \
  --enable-hwaccel=h264_dxva2,hevc_dxva2,h264_qsv,hevc_qsv \
  --enable-filter=scale,format \
  --enable-gpl \
  --enable-version3 
make -j$(nproc)
make install

echo "Copying runtime dependencies..."
# Copy essential runtime dependencies that FFmpeg needs on Windows
DEPS_SOURCE="../ffmpeg-libs/windows"
if [ -d "$DEPS_SOURCE" ]; then
    cp "$DEPS_SOURCE/libiconv-2.dll" "$BUILD_DIR/bin/" || echo "Warning: libiconv-2.dll not found"
    cp "$DEPS_SOURCE/libwinpthread-1.dll" "$BUILD_DIR/bin/" || echo "Warning: libwinpthread-1.dll not found"
    cp "$DEPS_SOURCE/zlib1.dll" "$BUILD_DIR/bin/" || echo "Warning: zlib1.dll not found"
    cp "$DEPS_SOURCE/liblzma-5.dll" "$BUILD_DIR/bin/" || echo "Warning: liblzma-5.dll not found"
    echo "✓ Runtime dependencies copied"
else
    echo "Warning: Dependency source directory not found at $DEPS_SOURCE"
    echo "You may need to manually copy: libiconv-2.dll, libwinpthread-1.dll, zlib1.dll, liblzma-5.dll"
fi

echo "Windows build complete. Libraries installed to $BUILD_DIR"

