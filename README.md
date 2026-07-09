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

## Installation

Add the package to the target project's `Packages/manifest.json`, pinned to a tag:

```json
"dev.gorny.sms": "https://github.com/zyndata/simple-meta-simulator.git#1.0.0"
```

Or via **Window > Package Manager > + > Install package from git URL**:

```
https://github.com/zyndata/simple-meta-simulator.git#1.0.0
```

Pin to a tag (`#1.0.0`) rather than a branch — UPM caches by commit hash, so a moving branch makes updates unpredictable.

## Usage

The simulator activates automatically in Play mode when no headset is present. Head and hand poses are driven from mouse and keyboard; controller button state is injected into `OVRInput` so ISDK grab interactions respond as they would on device.

Settings are available under the simulator settings window (see the toolbar button added by the Editor assembly).

## Default controls

All keys below are the out-of-the-box defaults. They live in an `InputActionAsset` and can be remapped without touching code.

### Look and move

| Input | Action |
| --- | --- |
| Hold **Right Mouse** + move mouse | Look around (yaw / pitch) |
| **W / A / S / D** | Move the active target forward / left / back / right (relative to head yaw) |
| **Q / E** | Move the active target down / up |

Movement is routed to the **active move target**. The default hand mode is *Cycle Key*, so **Tab** cycles the target:

**Both** → **Left hand** → **Right hand** → **Head** → **Both** …

- **Both** (default): head and both hands move together; hands stay locked in front of the head.
- **Head**: only the head moves; hands stay where they are.
- **Left** / **Right**: only that hand moves.

### Hands (controller buttons)

Grab and grip are **press-to-toggle** (tap once to hold, tap again to release). The four face buttons are **Held** by default (active only while the key is down); this can be switched to **Toggle** in the settings window.

| | Right hand | Left hand |
| --- | --- | --- |
| Grab — index trigger | **G** | **V** |
| Grip — hand trigger | **F** | **C** |
| Face button A / X | **H** (A) | **B** (X) |
| Face button B / Y | **J** (B) | **N** (Y) |

Grip (hand trigger) drives ISDK grab selection; the index trigger maps to the ISDK trigger/ray selector.

## License

MIT — see [LICENSE](LICENSE).
