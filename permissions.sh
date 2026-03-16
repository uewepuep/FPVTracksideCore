#!/bin/sh
# Strip macOS quarantine flags from all dylibs in the project.
# Run this after a git pull if macOS starts blocking dylibs.
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
find "$SCRIPT_DIR" -not -path "*/.git/*" -exec xattr -d com.apple.quarantine {} \; 2>/dev/null
find "$SCRIPT_DIR/../FPVTrackside" -not -path "*/.git/*" -exec xattr -d com.apple.quarantine {} \; 2>/dev/null
echo "Done. Quarantine flags removed."
