# Changelog

All notable changes to this package are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.1.0] - 2026-07-10

### Added

- Quest Link guard: the simulator no longer starts when a real headset is reachable (`OVRPlugin.hmdPresent`, i.e. connected through Link even when not worn, in addition to the previous worn-only `userPresent` check). A `[SMS]` log line reports the yield.
- Toolbar status light now has three states: off (disabled), green (enabled), orange (enabled but a real headset/Quest Link is detected, so the simulator will not start).
- Grab/Grip Mode setting (Toggle/Held) for the index and hand triggers, next to the existing Face Button Mode. Default Toggle preserves the previous press-to-toggle behavior.
- `LICENSE.meta`, so consuming projects no longer warn about a missing meta file.
- This changelog.

### Changed

- OVRInput state injection is allocation-free in steady state: controller lookups, boxed controller states, and ISDK button-usage enum masks are cached and reused; unchanged input skips the write entirely (edges still produce clean single-frame GetDown/GetUp transitions).
- Rig rebinding scans (`FindFirstObjectByType`) are throttled to every 0.5 s while no `OVRCameraRig` is bound, instead of running every frame.
- The settings window accesses the config through plain properties instead of reflection.

### Removed

- Dead input surface: `HandPlanar` (arrow keys), `HandDepth` (PageUp/PageDown), `LeftHandModifier`/`RightHandModifier` (Shift keys) actions and their bindings, unused `*Pressed()` accessors, and the unused `handMoveSpeed`/`handDepthSpeed` settings. Hand movement is fully covered by the Tab-cycled move target (see README).

### Fixed

- The OVR disconnected event sequence is no longer raised on exit when the connected sequence never fired.

## [1.0.0] - 2026-07-09

### Added

- Initial release: in-editor XR simulator for Meta OVR rigs (head/hand pose driving, OVRInput state injection, ISDK controller data + interactor + animator driving, OVR lifecycle event raising, toolbar toggle, settings window, remappable input bindings).
