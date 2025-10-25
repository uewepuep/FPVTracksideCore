#!/bin/bash

# Fix Mac app icon configuration
# This script copies the icon to the proper location and updates Info.plist

APP_DIR="$1"
ICON_FILE="$2"

if [ -z "$APP_DIR" ] || [ -z "$ICON_FILE" ]; then
    echo "Usage: $0 <app_directory> <icon_file>"
    exit 1
fi

# Create Resources directory if it doesn't exist
mkdir -p "$APP_DIR/Contents/Resources"

# Copy icon to Resources directory
cp "$ICON_FILE" "$APP_DIR/Contents/Resources/"

# Extract icon filename
ICON_NAME=$(basename "$ICON_FILE")

# Update Info.plist to include CFBundleIconFile
INFO_PLIST="$APP_DIR/Contents/Info.plist"

if [ -f "$INFO_PLIST" ]; then
    # Check if CFBundleIconFile already exists
    if ! grep -q "CFBundleIconFile" "$INFO_PLIST"; then
        # Add CFBundleIconFile before the closing </dict>
        sed -i '' "s|</dict>|	<key>CFBundleIconFile</key>\
	<string>$ICON_NAME</string>\
</dict>|" "$INFO_PLIST"
        echo "Added CFBundleIconFile to Info.plist"
    else
        echo "CFBundleIconFile already exists in Info.plist"
    fi
fi

echo "Icon configuration complete for $APP_DIR"