#!/bin/bash
set -e

# You need to have MSYS2 installed with necessary packages:
# pacman -Syu
# pacman -S base-devel pkg-config yasm nasm mingw-w64-x86_64-toolchain mingw-w64-x86_64-cuda mingw-w64-x86_64-onevpl mingw-w64-x86_64-amf

FFMPEG_DIR="$PWD/ffmpeg"
BUILD_DIR="$PWD/build-windows"

if [ ! -d "$FFMPEG_DIR" ]; then
  git clone https://git.ffmpeg.org/ffmpeg.git "$FFMPEG_DIR"
fi

cd "$FFMPEG_DIR"
git reset --hard
git clean -fdx

export PATH="/mingw64/bin:$PATH"
export PKG_CONFIG_PATH="/mingw64/lib/pkgconfig"

./configure \
  --prefix="$BUILD_DIR" \
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
  --enable-indev=dshow \
  --enable-decoder=h264,aac,hevc,rawvideo,h264_qsv,h264_nvdec,h264_amf,hevc_qsv,hevc_nvdec,hevc_amf \
  --enable-encoder=rawvideo,h264_qsv,h264_nvenc,h264_amf \
  --enable-hwaccel=h264_qsv,h264_nvdec,h264_amf,hevc_qsv,hevc_nvdec,hevc_amf \
  --enable-libmfx \
  --enable-libvpl \
  --enable-qsv \
  --enable-nvenc \
  --enable-cuda \
  --enable-cuvid \
  --enable-amf \
  --enable-filter=scale,format 
make -j$(nproc)
make install

echo "Windows build complete. Libraries installed to $BUILD_DIR"

