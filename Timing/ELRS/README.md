# ExpressLRS Backpack race control

This integration lets a race director start and stop FPV Trackside races with
the `DVR Rec` switch configured in an ExpressLRS transmitter backpack. It uses
the same MSP v2 messages as the RotorHazard
[VRxC ELRS plugin](https://github.com/i-am-grub/vrxc_elrs).

The backpack is a race-control accessory, not a lap timer. Configure a primary
timing system such as RotorHazard, then add **ELRS Backpack (Race Control)** in
Trackside's timing settings.

## Setup

1. Flash an ESP32/ESP8266 with the ExpressLRS `Race Timer` backpack target.
   Backpack firmware 1.5.0 or newer is recommended by the VRxC ELRS project.
2. Configure the backpack bind phrase and the transmitter's `DVR Rec` AUX
   channel according to the
   [VRxC ELRS race-control guide](https://github.com/i-am-grub/vrxc_elrs#control-the-race-from-the-race-directors-transmitter).
3. Connect the timer backpack to the Trackside computer over USB.
4. In Trackside timing settings, either:
   - select **Scan Systems** and add the detected ELRS Backpack, or
   - add **ELRS Backpack (Race Control)** and choose its serial port.
5. Keep the baud rate at `460800` unless the backpack firmware is configured
   differently.

The start command follows Trackside's normal start-button path, including video
checks, announcements, and configured start delay. The stop command follows the
normal stop-button path, including cancellation of a race that is still staging.

## Protocol

Communication uses MSP v2 over an 8-N-1 serial connection:

- `MSP_ELRS_GET_BACKPACK_VERSION` (`0x0010`) verifies the selected device.
- `MSP_ELRS_BACKPACK_SET_RECORDING_STATE` (`0x0305`) carries race control:
  - `0x01`: start race
  - `0x00`: stop race
- Packets use CRC-8/DVB-S2 with polynomial `0xD5`.

Trackside validates the version response and packet CRC before accepting a
device or command. The debounce setting suppresses repeated switch messages.

## Hardware verification

Automated builds can verify the parser and integration compile, but final
verification requires a timer backpack and transmitter:

1. Confirm the ELRS status changes to `Race control ready`.
2. Select a race and toggle `DVR Rec` on; Trackside should enter its normal
   staging/start sequence once.
3. Toggle `DVR Rec` off while staging and while racing; Trackside should cancel
   staging or stop the race through the normal UI workflow.

If the device does not connect, verify the data-capable USB cable, serial port,
460800 baud setting, backpack target, and bind phrase.
