# PowerShell script to build Windows FFmpeg libraries and copy them to the project
# Run this script from the ffmpeg-builds directory

param(
    [switch]$SkipBuild,
    [switch]$SkipCopy
)

Write-Host "FFmpeg Windows Build and Copy Script" -ForegroundColor Green
Write-Host "====================================" -ForegroundColor Green

$FFMPEG_DIR = "$PWD\ffmpeg"
$BUILD_DIR = "$PWD\build-windows"
$PROJECT_ROOT = "$PWD\..\.."

if (-not $SkipBuild) {
    Write-Host "Building FFmpeg for Windows..." -ForegroundColor Yellow
    
    # Check if MSYS2 is available
    $msys2Path = "C:\msys64\usr\bin\bash.exe"
    if (-not (Test-Path $msys2Path)) {
        Write-Host "❌ MSYS2 not found at $msys2Path" -ForegroundColor Red
        Write-Host "Please install MSYS2 first by running:" -ForegroundColor Cyan
        Write-Host "  .\install-build-tools.ps1" -ForegroundColor White
        exit 1
    }
    
    # Check if FFmpeg source exists
    if (-not (Test-Path $FFMPEG_DIR)) {
        Write-Host "Cloning FFmpeg repository..." -ForegroundColor Yellow
        git clone https://git.ffmpeg.org/ffmpeg.git "$FFMPEG_DIR"
    }
    
    # Build FFmpeg using MSYS2
    Write-Host "Running FFmpeg build..." -ForegroundColor Yellow
    & $msys2Path -c "cd '$FFMPEG_DIR' && git reset --hard && git clean -fdx && git fetch --tags && git checkout n7.1.1"
    
    $buildScript = @"
cd '$FFMPEG_DIR'
export PATH="/mingw64/bin:`$PATH"
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
  --enable-protocol=file \
  --enable-indev=dshow \
  --enable-decoder=h264,aac,hevc,rawvideo \
  --enable-encoder=rawvideo \
  --enable-filter=scale,format
make -j$(nproc)
make install
"@
    
    $buildScript | & $msys2Path -c "bash"
    
    if (-not (Test-Path "$BUILD_DIR\bin\avcodec-61.dll")) {
        Write-Host "❌ FFmpeg build failed!" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "✓ FFmpeg Windows build complete" -ForegroundColor Green
}

if (-not $SkipCopy) {
    Write-Host "Copying FFmpeg libraries to project..." -ForegroundColor Yellow
    
    # Create target directories
    $targetDirs = @(
        "$PROJECT_ROOT\FPVTracksideCore\ffmpeg-libs\windows",
        "$PROJECT_ROOT\ffmpegMediaPlatform\ffmpeg-libs\windows"
    )
    
    foreach ($dir in $targetDirs) {
        if (-not (Test-Path $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
    }
    
    # Copy DLLs and libs
    Write-Host "Copying DLLs and import libraries..." -ForegroundColor Yellow
    Copy-Item "$BUILD_DIR\bin\*.dll" "$PROJECT_ROOT\FPVTracksideCore\ffmpeg-libs\windows\" -Force
    Copy-Item "$BUILD_DIR\bin\*.lib" "$PROJECT_ROOT\FPVTracksideCore\ffmpeg-libs\windows\" -Force
    Copy-Item "$BUILD_DIR\bin\*.dll" "$PROJECT_ROOT\ffmpegMediaPlatform\ffmpeg-libs\windows\" -Force
    Copy-Item "$BUILD_DIR\bin\*.lib" "$PROJECT_ROOT\ffmpegMediaPlatform\ffmpeg-libs\windows\" -Force
    
    # Copy MinGW64 runtime dependencies required by FFmpeg DLLs
    Write-Host "Copying MinGW64 runtime dependencies..." -ForegroundColor Yellow
    $mingwDependencies = @("libiconv-2.dll", "liblzma-5.dll", "zlib1.dll", "libwinpthread-1.dll")
    $msys2BinPath = "C:\msys64\mingw64\bin"
    
    foreach ($dep in $mingwDependencies) {
        $sourcePath = Join-Path $msys2BinPath $dep
        if (Test-Path $sourcePath) {
            Copy-Item $sourcePath "$PROJECT_ROOT\FPVTracksideCore\ffmpeg-libs\windows\" -Force
            Copy-Item $sourcePath "$PROJECT_ROOT\ffmpegMediaPlatform\ffmpeg-libs\windows\" -Force
            Write-Host "  Copied $dep" -ForegroundColor Gray
        } else {
            Write-Host "  Warning: $dep not found at $sourcePath" -ForegroundColor Yellow
        }
    }
    
    # Copy headers
    Write-Host "Copying header files..." -ForegroundColor Yellow
    if (Test-Path "$PROJECT_ROOT\FPVTracksideCore\ffmpeg-libs\windows\include") {
        Remove-Item "$PROJECT_ROOT\FPVTracksideCore\ffmpeg-libs\windows\include" -Recurse -Force
    }
    if (Test-Path "$PROJECT_ROOT\ffmpegMediaPlatform\ffmpeg-libs\windows\include") {
        Remove-Item "$PROJECT_ROOT\ffmpegMediaPlatform\ffmpeg-libs\windows\include" -Recurse -Force
    }
    
    Copy-Item "$BUILD_DIR\include\*" "$PROJECT_ROOT\FPVTracksideCore\ffmpeg-libs\windows\include\" -Recurse -Force
    Copy-Item "$BUILD_DIR\include\*" "$PROJECT_ROOT\ffmpegMediaPlatform\ffmpeg-libs\windows\include\" -Recurse -Force
    
    Write-Host "✓ FFmpeg libraries copied to project" -ForegroundColor Green
}

Write-Host "Windows FFmpeg setup complete!" -ForegroundColor Green
Write-Host "Libraries are now available for replay video playback." -ForegroundColor Cyan 