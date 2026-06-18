# Bundling the OpenCV dylibs for macOS arm64

This directory commits the `libOpenCvSharpExtern.dylib` built on Mac
(Apple Silicon) together with the full set of OpenCV 4.11 `libopencv_*.dylib`
files it depends on, **with their references rewritten to `@loader_path`**.

This makes ArUco detection work on an end user's Mac even without
`brew install opencv@4`.

---

## 1. Prerequisites

- Apple Silicon Mac (arm64)
- `brew install opencv@4` done (4.11 series; check with `brew info opencv`)
  - The same version that was used to build `libOpenCvSharpExtern.dylib`
- Xcode Command Line Tools (so `otool` and `install_name_tool` are available)
- This repository cloned, working on a branch based on `aruco-debug`

```bash
cd <repo>/Timing/native/osx-arm64
ls -la libOpenCvSharpExtern.dylib  # the already-committed one should be visible
```

---

## 2. Identifying the required dylibs

Check the OpenCV libraries that `libOpenCvSharpExtern.dylib` links against.

```bash
otool -L libOpenCvSharpExtern.dylib | grep -i opencv
```

Example output (the actual paths depend on the brew location):

```
/opt/homebrew/opt/opencv/lib/libopencv_aruco.411.dylib   (compatibility version 4.11.0, current version 4.11.0)
/opt/homebrew/opt/opencv/lib/libopencv_calib3d.411.dylib ...
/opt/homebrew/opt/opencv/lib/libopencv_imgproc.411.dylib ...
/opt/homebrew/opt/opencv/lib/libopencv_core.411.dylib    ...
... (and possibly features2d / objdetect / video / dnn / flann etc. depending on the build)
```

**All** of the `libopencv_*.dylib` files listed here must be copied into
this directory.

---

## 3. Copy & pull in transitive dependencies

The OpenCV libraries depend on each other, so run `otool -L` recursively on
each copied dylib and add any missing `libopencv_*.dylib`. You also need to
include non-system dependencies such as `libtbb` / `libpng` / `libjpeg`
(exclude the Mac-standard ones that start with `/usr/lib/` or `/System/`).

```bash
# Locate the lib directory of brew's opencv
OPENCV_LIB=$(brew --prefix opencv)/lib
echo "OPENCV_LIB=$OPENCV_LIB"

# Stage 1: using libOpenCvSharpExtern.dylib's references as a guide, but
# copying the whole built OpenCV including libs it does not directly
# reference (this part was switched to manual). Copying everything turned
# out to be necessary because the libs are ultimately called from other
# dylibs and all of them end up being needed.
otool -L libOpenCvSharpExtern.dylib

cp ~/src/opencv_build/opencv/build/lib/*411* ./

# Stage 2: starting from the OpenCV dylibs, recursively pull in the required
# dylibs (iterate until reaching a fixed point)
```

```bash
#!/bin/bash

# Collect into the current directory
TARGET_DIR="."

# File for recording processed files (stores absolute paths)
PROCESSED_FILE=$(mktemp)
trap 'rm -f "$PROCESSED_FILE"' EXIT

# Function to logically normalize a path (keep symlinks but resolve ..)
normalize_path() {
    local path="$1"
    local dir
    local base
    if [ -d "$path" ]; then
        dir="$path"
        base=""
    else
        dir=$(dirname "$path")
        base=$(basename "$path")
    fi
    if [ -d "$dir" ]; then
        local resolved_dir
        resolved_dir=$(cd "$dir" && pwd -L)
        if [ -n "$base" ]; then
            echo "${resolved_dir}/${base}"
        else
            echo "${resolved_dir}"
        fi
    else
        echo "$path"
    fi
}

# Queue of paths to scan
queue=()

# Seed the queue with the existing .dylib files in the current directory
for f in *.dylib; do
    if [ -e "$f" ]; then
        abs_path=$(normalize_path "$f")
        install_name=$(otool -D "$abs_path" 2>/dev/null | tail -n 1)
        if [[ "$install_name" == /opt/homebrew/* ]]; then
            queue+=($(normalize_path "$install_name"))
        else
            queue+=("$abs_path")
        fi
    fi
done

while [ ${#queue[@]} -gt 0 ]; do
    # Pop the head of the queue
    current="${queue[0]}"
    queue=("${queue[@]:1}")

    # Skip if already processed (same absolute path)
    if grep -Fxq "$current" "$PROCESSED_FILE" 2>/dev/null; then
        continue
    fi
    echo "$current" >> "$PROCESSED_FILE"

    echo "Scanning: $current"

    filename=$(basename "$current")
    # Check existence at the destination and copy
    if [ ! -f "$TARGET_DIR/$filename" ]; then
        if [ -f "$current" ]; then
            if cp "$current" "$TARGET_DIR/"; then
                echo "Copied: $current -> $TARGET_DIR/$filename"
            else
                echo "Failed to copy: $current"
                continue
            fi
        else
            echo "Warning: Source file not found: $current"
            # If the real file is not found, scan a same-named file in the
            # current directory if one exists
            if [ -f "$TARGET_DIR/$filename" ]; then
                current=$(normalize_path "$TARGET_DIR/$filename")
            else
                continue
            fi
        fi
    fi

    # Extract dependency paths using otool -L
    deps=$(otool -L "$current" 2>/dev/null | sed -E 's/^[[:space:]]*//; s/ \(.*\)$//' | grep -E '^(/opt/homebrew/|@rpath/|@loader_path/)')

    for dep in $deps; do
        # Pattern 1: absolute path inside /opt/homebrew/
        if [[ "$dep" == /opt/homebrew/*.dylib ]]; then
            normalized_dep=$(normalize_path "$dep")
            if ! grep -Fxq "$normalized_dep" "$PROCESSED_FILE" 2>/dev/null; then
                queue+=("$normalized_dep")
            fi

        # Pattern 2: relative path containing @rpath or @loader_path
        elif [[ "$dep" == @*/*.dylib ]]; then
            filename=$(basename "$dep")

            # Base directory used to resolve the relative path
            base_dir=$(dirname "$current")

            if [[ "$dep" == @rpath/* ]]; then
                # For @rpath, get the path list from the LC_RPATH section
                rpaths=$(otool -l "$current" 2>/dev/null | grep -A 2 "LC_RPATH" | grep "path" | awk '{print $2}')

                for rpath in $rpaths; do
                    [[ -z "$rpath" ]] && continue
                    # Replace @loader_path with base_dir to get an absolute path
                    resolved_rpath=$(echo "$rpath" | sed "s|@loader_path|$base_dir|g")

                    # Clean trailing slash and join
                    resolved_rpath_clean="${resolved_rpath%/}"
                    resolved_dep="${resolved_rpath_clean}/${filename}"

                    if [ -f "$resolved_dep" ]; then
                        if [[ "$resolved_dep" == /opt/homebrew/* ]]; then
                            normalized_dep=$(normalize_path "$resolved_dep")
                            if ! grep -Fxq "$normalized_dep" "$PROCESSED_FILE" 2>/dev/null; then
                                queue+=("$normalized_dep")
                            fi
                        fi
                    fi
                done
            else
                # For @loader_path
                resolved_dep=$(echo "$dep" | sed "s|@loader_path|$base_dir|g")

                if [ -f "$resolved_dep" ]; then
                    if [[ "$resolved_dep" == /opt/homebrew/* ]]; then
                        normalized_dep=$(normalize_path "$resolved_dep")
                        if ! grep -Fxq "$normalized_dep" "$PROCESSED_FILE" 2>/dev/null; then
                            queue+=("$normalized_dep")
                        fi
                    fi
                fi
            fi
        fi
    done
done

echo "Done. All collected dylibs are in the current directory."
```

---

## 4. Rewriting install_name

Unify everything to `@loader_path/` references so that the dylibs in the
same directory are used even when the end user's Mac has no brew.

```bash
#!/bin/bash

# Target: All dylibs in the current directory
TARGET_DIR="."

echo "Starting to update paths and verify LC_RPATH for dylib files in $TARGET_DIR..."
echo "--------------------------------------------------"

for lib in "$TARGET_DIR"/*.dylib; do
    # Skip if no dylib files are found
    [ -f "$lib" ] || continue

    filename=$(basename "$lib")

    # 1. Get current ID (install name)
    current_id=$(otool -D "$lib" 2>/dev/null | tail -n 1)

    id_to_change=""
    if [[ "$current_id" == /opt/homebrew/* ]]; then
        id_to_change="$current_id"
    fi

    # 2. Get dependencies that need to be changed
    # Extract dependencies containing "/opt/homebrew/", excluding the library's own ID
    deps_to_change=()
    all_deps=$(otool -L "$lib" 2>/dev/null | grep "/opt/homebrew/" | awk '{print $1}')
    for dep in $all_deps; do
        if [ "$dep" != "$current_id" ]; then
            # Avoid duplicates
            if [[ ! " ${deps_to_change[*]} " =~ " ${dep} " ]]; then
                deps_to_change+=("$dep")
            fi
        fi
    done

    # 3. Check if @loader_path is already in LC_RPATH
    rpaths=$(otool -l "$lib" 2>/dev/null | grep -A 2 "LC_RPATH" | grep "path" | awk '{print $2}')
    has_loader_path=false
    for rpath in $rpaths; do
        if [ "$rpath" = "@loader_path" ]; then
            has_loader_path=true
            break
        fi
    done

    # Determine if any changes are needed
    need_change=false
    if [ -n "$id_to_change" ] || [ ${#deps_to_change[@]} -gt 0 ] || [ "$has_loader_path" = false ]; then
        need_change=true
    fi

    if [ "$need_change" = true ]; then
        echo "Updating: $filename"

        # 4. Remove signature before making changes
        echo "  -> Removing code signature..."
        codesign --remove-signature "$lib" 2>/dev/null

        # 5. Change ID if necessary
        if [ -n "$id_to_change" ]; then
            new_id="@loader_path/$filename"
            echo "  -> Changing ID: $id_to_change -> $new_id"
            install_name_tool -id "$new_id" "$lib"
            if [ $? -ne 0 ]; then
                echo "     Warning: Failed to change ID."
            fi
        fi

        # 6. Change dependencies if necessary
        for dep in "${deps_to_change[@]}"; do
            dep_name=$(basename "$dep")
            new_path="@loader_path/$dep_name"
            echo "  -> Changing dependency: $dep -> $new_path"
            install_name_tool -change "$dep" "$new_path" "$lib"
            if [ $? -ne 0 ]; then
                echo "     Warning: Failed to change dependency $dep."
            fi
        done

        # 7. Add @loader_path to LC_RPATH if necessary
        if [ "$has_loader_path" = false ]; then
            echo "  -> Adding @loader_path to LC_RPATH"
            install_name_tool -add_rpath "@loader_path" "$lib"
            if [ $? -ne 0 ]; then
                echo "     Warning: Failed to add @loader_path to LC_RPATH."
            fi
        fi

        # 8. Re-sign the dylib after making changes
        echo "  -> Re-signing with ad-hoc signature..."
        if command -v codesign >/dev/null 2>&1; then
            codesign -f -s - "$lib" >/dev/null 2>&1
            if [ $? -eq 0 ]; then
                echo "  -> Successfully re-signed."
            else
                echo "  -> Warning: codesign failed."
            fi
        else
            echo "  -> Warning: codesign command not found."
        fi
        echo "--------------------------------------------------"
    else
        echo "No changes needed for: $filename"
        echo "--------------------------------------------------"
    fi
done

echo "Verification and update complete."
```

Notes:
- If the original dylib has LC_RPATH (`@rpath/...`) references, clean them up
  with `install_name_tool -rpath` or `-delete_rpath`. Finally check
  `otool -l <f> | grep -A2 LC_RPATH` to confirm no brew rpath remains.
- After rewriting, the **code signature is broken** (it becomes ad-hoc).
  Depending on the distribution form you may need to re-sign ad-hoc with
  `codesign --force --sign - <f>`. For internal or development use it often
  works as is.

---

## 5. Verification

### 5.1 No absolute paths remain in install_name

```bash
otool -L libOpenCvSharpExtern.dylib libopencv_*.dylib \
  | grep -v "@loader_path\|^/usr/lib\|^/System\|:$\|^$" \
  | grep -v "compatibility version"
# ^ OK if nothing is printed
# NG if even one brew path (/opt/homebrew/... or /usr/local/...) remains
```

### 5.2 Standalone dlopen test

```bash
# Temporarily drop brew's opencv from the path and check dlopen works with the bundled dylibs only
DYLD_LIBRARY_PATH="" \
  python3 -c "import ctypes; ctypes.CDLL('./libOpenCvSharpExtern.dylib'); print('OK')"
```

Success if `OK` is printed. On error, the missing dylib is shown in the
message, so copy & rewrite it as well.

### 5.3 Verification via the .NET build

```bash
cd <repo>
dotnet publish FPVMacSideCore/FPVMacsideCore.csproj \
  -c Release -r osx-arm64 --self-contained true \
  -o bin/Release-publish/osx-arm64

ls bin/Release-publish/osx-arm64/runtimes/osx-arm64/native/
# csproj is applied correctly if libOpenCvSharpExtern.dylib and all libopencv_*.dylib are listed
```

After running, success if the following appears in `TimingLog.txt`:

```
[ArUco-Debug] native-probe: FOUND   .../runtimes/osx-arm64/native/libOpenCvSharpExtern.dylib ...
[ArUco-Debug] Cv2.GetVersionString() OK, OpenCV 4.11.0
[ArUco-Debug] CvAruco.GetPredefinedDictionary(Dict4X4_50) OK.
```

---

## 6. Commit & PR

```bash
cd <repo>
git checkout -b aruco-bundle-opencv-dylibs origin/aruco-debug

# Add the dylibs under this directory
git add Timing/native/osx-arm64/

# libOpenCvSharpExtern.dylib has also changed because of the install_name
# rewrite, so confirm it is staged
git status

git commit -m "ArUco: bundle OpenCV dylibs for macOS arm64 (unify @loader_path references)"
git push origin aruco-bundle-opencv-dylibs
```

Please target the PR at the `aruco-debug` branch of
`zubon2003/FPVTracksideCore`.

On the csproj side (`Timing/Timing.csproj`), the plan is to bundle
`native/osx-arm64/*.dylib` via a glob, so no edit fixing individual file
names is needed (the csproj glob change can be handled before or after the
PR is received).

---

## 7. What to look at when stuck

- `otool -L <dylib>` — references (install_name and LC_LOAD_DYLIB)
- `otool -D <dylib>` — its own LC_ID_DYLIB
- `otool -l <dylib> | grep -A2 LC_RPATH` — the rpath list
- Runtime dlopen failure log: launch with `DYLD_PRINT_LIBRARIES=1` and the
  tty shows which dylib it tried to load, in what order, and where it failed
- `codesign -dv <dylib>` — check the signing state

---

## 8. Rough file sizes

For reference, the size of brew opencv@4.11:

- `libopencv_core.411.dylib` ≈ 8 MB
- `libopencv_imgproc.411.dylib` ≈ 5 MB
- `libopencv_aruco.411.dylib` ≈ 200 KB
- `libopencv_calib3d.411.dylib` ≈ 2 MB
- `libopencv_features2d.411.dylib` ≈ 800 KB
- The total comes to roughly **20-40 MB** (adding dnn makes it +60MB).

Modules unnecessary for ArUco such as `dnn` / `videoio` / `gapi` do not need
to be bundled if they do not appear in `otool -L libOpenCvSharpExtern.dylib`,
so it should ultimately fit within about 20MB.

-> After much trial and error, the dylib files ended up being 171 files and
about 280MB. Most of that is presumably not actually used, but it is required
at library load time.
