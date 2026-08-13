# Changelog

All notable changes to this package are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.4.0] - 2026-08-13

### Added

- **Simulated hand tracking** for Meta Interaction SDK rigs, driving the ISDK hand data pipeline from the simulated hand poses so the hand interactor branches run without a headset. Fingers curl procedurally off the rig's own rest skeleton: the index trigger bends the index finger and drives the pinch (thumb + index), the hand trigger closes the remaining fingers. The visible hand mesh follows the injected data, so hands open and close on screen.
- **Hand Simulation** setting (settings window, *Hands* section) selecting which interactor branch is live on a rig that carries both controller and hand interactors:
  - **Off** (default) - controllers only, `Controller and No Hand`. Byte-for-byte the previous behaviour.
  - **With Controllers** - controllers held in tracked hands, `Controller and Hand`. This is what a real headset reports with controllers in hand, and what distance hand grab needs.
  - **Hands Only** - the controllers report as disconnected, `Hand and No Controller`: hand ray, hand poke, hand grab and the microgesture interactors.
- Simulated ISDK head pose. `FromOVRHmdDataSource` is as dead as the hand and controller sources without a headset, so everything that positions itself off the head sat at the rig root.

### Fixed

- **Distance grab never found a candidate.** A distance grab candidate has to fall inside the head frustum, and that frustum is placed by a `CenterEyeOffset` reading the ISDK HMD data source - which reported an untracked identity pose, leaving the frustum at the rig root pointing wherever the rig happened to face. With the head pose injected, distance hand grab hovers and selects normally. On a comprehensive interaction rig the distance grab interactors live in the hand branches, so this also needs *Hand Simulation* to be on.

### Tested

- Unity 6000.2.9f1 with Meta XR SDK Core / Interaction OVR / MR Utility Kit 203.0.0 on the Meta Building Blocks comprehensive interaction rig: distance hand grab hovers and selects a `DistanceHandGrabInteractable` and pulls it into the hand; hand poke selects a UI canvas; the hand ray hovers a canvas and selects it on pinch; with *Hand Simulation* off the controller ray still hovers the same canvas.

## [1.3.0] - 2026-07-31

### Added

- Unity 6.3 support for the toolbar status light. Unity 6.3 rejects elements injected into the main toolbar visual tree - it detects them, logs a warning and moves them into its "Unsupported User Elements" group, which is docked on the far left. On 6.3 and newer the light is now registered through the supported `MainToolbarElement` API and docks immediately right of the Play buttons; Unity 6.0-6.2 keep the previous injection path. The two implementations are selected at compile time by an asmdef `versionDefines` entry, so only one is ever built.
- **Tools > SMS > Open Settings** menu item, opening the simulator settings window. This does not depend on the toolbar, so the settings stay reachable regardless of editor version or toolbar layout.

### Fixed

- Toolbar status light never appeared on Unity 6.1 and newer. It searched for the main toolbar as an `EditorWindow` named `UnityEditor.MainToolbarWindow`, but the main toolbar is the internal `UnityEditor.Toolbar` (a `GUIView`, not an `EditorWindow`), so the lookup always failed. It also targeted a container class that no longer exists. The light is now anchored to the Play controls themselves rather than to a named container, and its placement is re-validated so a toolbar rebuild cannot leave it orphaned.
- Toolbar light on Unity 6.3 never changed state - it kept the icon it was first built with, staying green even when the simulator was disabled. Assigning the element's content does not repaint the docked overlay; the light now refreshes the element when its state changes.

### Changed

- The orange "real headset detected" light now appears **only in Play mode**, not while editing. `OVRPlugin` is not initialized outside Play mode, so `hmdPresent` / `userPresent` read false in the editor even with Quest Link running, and no other OVRPlugin signal carries live connection state (`GetSystemHeadsetType()` reports the last known headset even when Link is fully disconnected, which pinned the light orange in every state). The Play mode behaviour - the simulator yielding to a real headset - is unchanged; only the pre-Play warning described in 1.1.0 is not achievable and has been removed.

## [1.2.0] - 2026-07-24

### Added

- Hand/controller rotation in the single-hand move targets. When the active target is **Left** or **Right** (Tab-cycled), hold the **middle mouse button** and move the mouse to rotate that hand/controller (yaw / pitch). The gate is a remappable `RotateModifier` input action (default `<Mouse>/middleButton`); rotation follows the same mouse delta as head look, with its own speed and pitch-invert settings.
- **Hand Rotate Sensitivity** and **Invert Hand Rotate Y** settings (settings window, *Hands* section; defaults `0.15` / off) controlling the hand-rotation speed and pitch direction independently of the head-look settings.

## [1.1.1] - 2026-07-14

### Fixed

- Meta Building Blocks rig (`OVRComprehensiveInteractionRig`): all controller skins showed at once and the pressed-button animation did not play after updating to the Meta XR SDK 203 / XR Interaction Toolkit 3.3 package set. The updated rig activates its `OVRControllerVisualLeft`/`OVRControllerVisualRight` objects a few frames after the ISDK data sources resolve, so the one-shot model selection ran too early (pruning nothing, caching no animator) and never retried. Model selection now retries until every in-scene `OVRControllerHelper` has been pruned while active (bounded), leaving one controller model per hand and driving its animator.

### Tested

- Unity 6000.3.13f1 with Meta XR SDK Core / Interaction OVR / MR Utility Kit 203.0.0, XR Interaction Toolkit 3.3.2, XR Meta OpenXR 2.5.1, OpenXR Plugin 1.16.1, Input System 1.19.0, on the Autohand rig and the Meta Building Blocks (`OVRComprehensiveInteractionRig`) rig.

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
