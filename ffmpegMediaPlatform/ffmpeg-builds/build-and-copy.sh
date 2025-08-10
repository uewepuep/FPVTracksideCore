#!/bin/bash
set -e

echo "=== Building FFmpeg for macOS ==="

# Build ARM64 version
echo "Building ARM64 version..."
./build-mac.sh

# Build Intel version
echo "Building Intel version..."
./build-mac-intel.sh

echo "=== Build complete ==="
echo "ARM64 libraries: $PWD/build-arm64/lib"
echo "Intel libraries: $PWD/build-intel/lib"

# Fix library paths for deployment
echo "=== Fixing library paths for deployment ==="

# Function to fix library paths
fix_library_paths() {
    local lib_dir="$1"
    local arch_name="$2"
    
    echo "Fixing library paths in $lib_dir..."
    
    # Get the absolute path to the lib directory
    local abs_lib_dir=$(cd "$lib_dir" && pwd)
    
    # Change to the lib directory
    cd "$lib_dir"
    
    # Fix each library
    for lib in *.dylib; do
        if [[ -f "$lib" ]]; then
            echo "Fixing $lib..."
            
            # Get the current install name
            local current_name=$(otool -D "$lib" | tail -n +2)
            
            # Fix the install name to use @rpath
            if [[ -n "$current_name" ]]; then
                local base_name=$(basename "$lib")
                install_name_tool -id "@rpath/$base_name" "$lib"
                echo "  Fixed install name for $lib"
            fi
            
            # Fix dependencies to use @rpath
            local deps=$(otool -L "$lib" | grep "build-$arch_name" | awk '{print $1}')
            for dep in $deps; do
                if [[ -n "$dep" ]]; then
                    local dep_name=$(basename "$dep")
                    install_name_tool -change "$dep" "@rpath/$dep_name" "$lib"
                    echo "  Fixed dependency $dep -> @rpath/$dep_name in $lib"
                fi
            done
            
            # Check if rpath already exists before adding
            local rpath_exists=$(otool -l "$lib" | grep -c "@executable_path/ffmpeg-libs/$arch_name" || true)
            if [[ $rpath_exists -eq 0 ]]; then
                # Add rpath to the library
                install_name_tool -add_rpath "@executable_path/ffmpeg-libs/$arch_name" "$lib"
                echo "  Added rpath @executable_path/ffmpeg-libs/$arch_name to $lib"
            else
                echo "  Rpath @executable_path/ffmpeg-libs/$arch_name already exists in $lib"
            fi
        fi
    done
    
    echo "Library path fixing complete for $arch_name"
}

# Fix ARM64 libraries
if [[ -d "build-arm64/lib" ]]; then
    fix_library_paths "build-arm64/lib" "arm64"
fi

# Fix Intel libraries
if [[ -d "build-intel/lib" ]]; then
    fix_library_paths "build-intel/lib" "intel"
fi

# Verify the libraries exist
echo "=== Verifying libraries ==="
echo "ARM64 libraries:"
ls -la build-arm64/lib/*.dylib

echo "Intel libraries:"
ls -la build-intel/lib/*.dylib

echo "=== Library verification complete ==="
echo ""
echo "The libraries are now ready to be used by the .NET projects."
echo "They will be automatically copied to the build output directories."
echo ""
echo "To build the project:"
echo "  cd FPVMacSideCore"
echo "  dotnet build -r osx-arm64    # For ARM64 Macs"
echo "  dotnet build -r osx-x64      # For Intel Macs"
echo ""
echo "The libraries will be automatically copied to:"
echo "  bin/Debug/net6.0/osx-arm64/ffmpeg-libs/arm64/"
echo "  bin/Debug/net6.0/osx-x64/ffmpeg-libs/intel/" 