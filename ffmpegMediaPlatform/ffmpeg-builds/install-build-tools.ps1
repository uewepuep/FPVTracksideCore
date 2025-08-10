# PowerShell script to install FFmpeg build dependencies using Chocolatey
# Run this script as Administrator

Write-Host "Installing FFmpeg build dependencies..." -ForegroundColor Green

# Install MSYS2 which includes MinGW compiler toolchain
Write-Host "Installing MSYS2..." -ForegroundColor Yellow
choco install msys2 -y

# Install additional build tools
Write-Host "Installing build tools..." -ForegroundColor Yellow
choco install cmake -y
choco install nasm -y
choco install yasm -y

Write-Host "Installation complete!" -ForegroundColor Green
Write-Host "After installation, open MSYS2 terminal and run:" -ForegroundColor Cyan
Write-Host "  pacman -Syu" -ForegroundColor White
Write-Host "  pacman -S base-devel pkg-config yasm nasm mingw-w64-x86_64-toolchain" -ForegroundColor White
Write-Host "" 
Write-Host "Then you can run the FFmpeg build script from the MSYS2 terminal." -ForegroundColor Cyan