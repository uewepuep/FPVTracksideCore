# FPVTuxside — Linux Dependencies

## Runtime: .NET 9

```
# Fedora
sudo dnf install dotnet-runtime-9.0

# Ubuntu/Debian
sudo apt install dotnet-runtime-9.0

# Arch
sudo pacman -S dotnet-runtime-9
```

## FFmpeg

Required for video/replay. Uses system FFmpeg libraries (v7+ recommended).

```
# Fedora
sudo dnf install ffmpeg ffmpeg-libs

# Ubuntu/Debian (non-free repo required)
sudo apt install ffmpeg

# Arch
sudo pacman -S ffmpeg
```

## SDL2

Required by MonoGame for windowing/input.

```
# Fedora
sudo dnf install SDL2

# Ubuntu/Debian
sudo apt install libsdl2-2.0-0

# Arch
sudo pacman -S sdl2
```

## Clipboard

Uses `wl-clipboard` on Wayland (recommended), falls back to `xclip` on X11.

```
# Fedora (usually pre-installed)
sudo dnf install wl-clipboard

# Ubuntu/Debian
sudo apt install wl-clipboard

# Arch
sudo pacman -S wl-clipboard

# X11 fallback
sudo dnf install xclip       # Fedora
sudo apt install xclip        # Ubuntu/Debian
sudo pacman -S xclip          # Arch
```

## Text-to-Speech

Used for race announcements.

```
# Fedora
sudo dnf install espeak-ng

# Ubuntu/Debian
sudo apt install espeak-ng

# Arch
sudo pacman -S espeak-ng
```

## File Dialogs

Uses `kdialog` on KDE, `zenity` on GNOME/everything else. Install whichever matches your desktop.

**KDE:**
```
# Fedora
sudo dnf install kdialog

# Ubuntu/Debian
sudo apt install kdialog

# Arch
sudo pacman -S kdialog
```

**GNOME / other:**
```
# Fedora
sudo dnf install zenity

# Ubuntu/Debian
sudo apt install zenity

# Arch
sudo pacman -S zenity
```


## Webcam / Video Capture

Webcam input uses V4L2 via FFmpeg — no extra packages needed beyond FFmpeg above. Your user may need to be in the `video` group:

```
sudo usermod -aG video $USER
```
