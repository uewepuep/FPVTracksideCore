ArUco Timing System - calibration folder
=========================================

To enable lens-distortion correction for ArUco marker detection, place a
TVPAS2-compatible camera_calibration.json file in this folder (next to the
FPVTrackside executable):

  <FPVTrackside install>/Aruco/camera_calibration.json

Format:

  {
    "mtx": [[fx,  0, cx],
            [ 0, fy, cy],
            [ 0,  0,  1]],
    "dist": [[k1, k2, p1, p2, k3]],
    "resolution": [H, W]
  }

The "resolution" field is the calibration reference resolution in [height, width]
order. The detector auto-scales intrinsics to the detection frame size
(480x360 by default).

If this file is absent the detector runs without undistortion
(DetectMode = Original is effectively forced).
