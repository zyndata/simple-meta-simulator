# Simple Meta Simulator (`dev.gorny.sms`)

In-editor XR simulator for Meta OVR rigs. Drives the head and both hands with mouse and keyboard when no headset is connected, so scenes built on the Meta OVR / ISDK rig can be exercised entirely inside the Editor.

Editor-only — the runtime assembly is gated behind `UNITY_EDITOR` and produces no build footprint.

## Why

`com.meta.xr.simulator` 81.x crashes on Unity 6.3 with a Vulkan swapchain error (`VK_ERROR_OUT_OF_DATE_KHR`) on focus loss/return. This package replaces it for in-Editor iteration, with `RealHandsCameraRig` (Meta Building Blocks) compatibility.

## Requirements

- Unity 6000.0 or newer (developed and tested against 6000.3)
- `com.meta.xr.sdk.core` 77.0.0+ (provides the `Oculus.VR` assembly)
- `com.unity.inputsystem` 1.14.0+

Both are declared as package `dependencies` and will be resolved by UPM. Meta XR SDK must be reachable from the target project (it is not on a public registry that UPM queries by default — if your project does not already have it, install it first).

### Tested against

Verified on both the Autohand rig and the Meta Building Blocks rig (`OVRComprehensiveInteractionRig`) with:

| Package | Version |
| --- | --- |
| Unity | 6000.3.13f1 |
| `com.meta.xr.sdk.core` | 203.0.0 |
| `com.meta.xr.sdk.interaction.ovr` | 203.0.0 |
| `com.meta.xr.mrutilitykit` | 203.0.0 |
| `com.unity.xr.interaction.toolkit` | 3.3.2 |
| `com.unity.xr.meta-openxr` | 2.5.1 |
| `com.unity.xr.openxr` | 1.16.1 |
| `com.unity.inputsystem` | 1.19.0 |

## Installation

Add the package to the target project's `Packages/manifest.json`, pinned to a tag:

```json
"dev.gorny.sms": "https://github.com/zyndata/simple-meta-simulator.git#1.2.0"
```

Or via **Window > Package Manager > + > Install package from git URL**:

```
https://github.com/zyndata/simple-meta-simulator.git#1.2.0
```

Pin to a tag (e.g. `#1.2.0`) rather than a branch — UPM caches by commit hash, so a moving branch makes updates unpredictable.

## Usage

The simulator activates automatically in Play mode when no headset is present. Head and hand poses are driven from mouse and keyboard; controller button state is injected into `OVRInput` so ISDK grab interactions respond as they would on device.

Settings are available under the simulator settings window, opened by clicking the status light the package adds to the main toolbar (next to the Play buttons):

| Toolbar icon | Meaning |
| --- | --- |
| ⚫ Off | Simulator disabled. Play mode uses whatever runtime is available. |
| 🟢 Green | Simulator enabled. Play mode starts the in-editor simulator. |
| 🟠 Orange | Simulator enabled, **but a real headset is detected** (Quest Link connected or headset worn) - the simulator yields and the real device drives the rig. |

### Quest Link

When a headset is reachable - `OVRPlugin.hmdPresent` (connected through Quest Link, even sitting on the desk) or `OVRPlugin.userPresent` (worn) - the simulator does not start, so the real controllers and HMD keep control of the rig. No setting change is needed when switching between Link and simulated iteration; the toolbar light turns orange to show the simulator is being bypassed.

## Default controls

All keys below are the out-of-the-box defaults. They live in an `InputActionAsset` and can be remapped without touching code.

### Look and move

| Input | Action |
| --- | --- |
| Hold **Right Mouse** + move mouse | Look around (yaw / pitch) |
| Hold **Middle Mouse** + move mouse | Rotate the active hand (Left / Right target only) |
| **W / A / S / D** | Move the active target forward / left / back / right (relative to head yaw) |
| **Q / E** | Move the active target down / up |

Movement is routed to the **active move target**. The default hand mode is *Cycle Key*, so **Tab** cycles the target:

**Both** → **Left hand** → **Right hand** → **Head** → **Both** …

- **Both** (default): head and both hands move together; hands stay locked in front of the head.
- **Head**: only the head moves; hands stay where they are.
- **Left** / **Right**: only that hand moves. Hold the **middle mouse button** and move the mouse to rotate that hand/controller (yaw / pitch). Rotation speed and pitch direction are set by *Hand Rotate Sensitivity* and *Invert Hand Rotate Y* in the settings window (*Hands* section).

### Hands (controller buttons)

Both button groups have a configurable input mode in the settings window (*Hands* section):

- **Grab/Grip Mode** — index + hand triggers. Default **Toggle**: tap once to hold, tap again to release.
- **Face Button Mode** — X/Y/A/B. Default **Held**: active only while the key is down.

| | Right hand | Left hand |
| --- | --- | --- |
| Grab — index trigger | **G** | **V** |
| Grip — hand trigger | **F** | **C** |
| Face button A / X | **H** (A) | **B** (X) |
| Face button B / Y | **J** (B) | **N** (Y) |

Grip (hand trigger) drives ISDK grab selection; the index trigger maps to the ISDK trigger/ray selector.

## Troubleshooting

- **Simulator does not start in Play mode** — check the toolbar light: orange means a real headset/Quest Link session was detected and the simulator deliberately yielded (see the `[SMS]` log line); off means the *Enabled* toggle is off. Also verify the scene contains an `OVRCameraRig`.
- **Simulator starts but nothing moves** — the rig is searched for repeatedly (every 0.5 s), so a rig spawned later is picked up automatically; if it never binds, confirm the rig really is an `OVRCameraRig` from `com.meta.xr.sdk.core`.
- **Grab does not release** — grab/grip default to *Toggle*: tap the key again to release, or switch *Grab/Grip Mode* to *Held* in the settings window.

## License

MIT — see [LICENSE](LICENSE).
