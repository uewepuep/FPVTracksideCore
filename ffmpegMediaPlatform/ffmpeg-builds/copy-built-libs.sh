#!/bin/bash

echo "Copying newly built FFmpeg libraries..."

BUILD_DIR="$PWD/build-windows/bin"
TARGET_DIR="../ffmpeg-libs/windows"

if [ ! -d "$BUILD_DIR" ]; then
    echo "❌ Build directory not found: $BUILD_DIR"
    echo "Please run build-windows.sh first"
    exit 1
fi

if [ ! -d "$TARGET_DIR" ]; then
    echo "Creating target directory: $TARGET_DIR"
    mkdir -p "$TARGET_DIR"
fi

echo "Copying DLLs from $BUILD_DIR to $TARGET_DIR"

# Copy all FFmpeg DLLs
cp "$BUILD_DIR"/*.dll "$TARGET_DIR/"
cp "$BUILD_DIR"/*.lib "$TARGET_DIR/" 

# Copy headers for completeness
if [ -d "$PWD/build-windows/include" ]; then
    cp -r "$PWD/build-windows/include" "$TARGET_DIR/"
fi

echo "✓ Libraries copied successfully"
echo "Built libraries now available at: $TARGET_DIR"

# List what was copied
echo "Copied files:"
ls -la "$TARGET_DIR"/*.dll