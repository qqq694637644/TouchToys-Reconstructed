# Function-equivalent reconstruction plan

## Source fidelity

The x64 and ARM64 `TouchToys.dll` assemblies were decompiled independently with ILSpyCmd 9.1.0.7988. The raw x64 decompilation produced 219 C# files. The application project retains the decompiled application code and excludes compiler-generated WinRT generic helpers that the current WinRT source generator regenerates.

The raw x64 and ARM64 decompilations were also compared against each other: both produced 219 C# files, and the only source differences were generated XAML/WinRT type-info and vtable-helper files. No application feature class differed between the two architecture builds. This supports using one shared C# application source tree for both RIDs while letting the SDK regenerate architecture-appropriate interop helpers.

As an additional structural check, ILSpy type inventories for the original x64 assembly and the rebuilt x64 assembly contain exactly the same 213 non-compiler-generated type names. The total metadata type counts differ by one because compiler-generated closure/async state-machine names are emitted differently by the current compiler; no user-defined type was missing or added in that comparison.

The current source intentionally stays close to the decompiler output. Manual changes are limited to:

- `TouchpadGesture.cs`: repair current/start contact coordinate references so movement, pinch distance, and swipe displacement are computed from the correct points.
- `OutsideInteractionMonitor.cs`: express pointer message constants as `nuint` patterns to match the native message type.
- `QrScannerWindow.cs`: remove an unassigned decompiler-generated no-op local.
- `App.cs`: redirect test artifacts to a dedicated writable directory and make smoke-test failures return a nonzero process exit code.
- `Properties/AssemblyInfo.cs`: remove compiler-generated safety metadata that the current compiler emits itself and expose internal pure-logic types to the regression test assembly.
- `App.xaml` / `tools/XbfCompiler`: reconstruct the compiled XBF 2.1 MUX resource into editable XAML and compile it back to XBF as part of the build.

The rest of the editable application source is kept as close as practical to the raw decompilation. This is a source reconstruction, not a claim that the original author's comments, formatting, or XAML source have been recovered.

## Recovered feature areas

| Area | Recovered implementation | Validation approach |
|---|---|---|
| Floating touch point and radial menu | `FloatingBall`, `WheelDrawing`, gesture dispatch in `App` | UI smoke captures and manual touch testing |
| Shortcut Ark and companion bubbles | `ShortcutArk`, `ArkBubble`, `ArkProfile` | Built-in Ark checks and drag/snap device tests |
| TT virtual touchpad | `VirtualTouchpad`, `TouchpadGesture`, `TouchpadMiddleGesture` | Deterministic tap, hold, pinch, 2/3-finger scroll and swipe tests; physical touch test |
| Global multi-touch | `GlobalTouchGestures`, `MultiTouchRecognizer`, `ShapeSequence` | Built-in relay simulation and touch injection tests; signed UIAccess device retest |
| Virtual Dial | `VirtualDial`, `DialMotion`, `DialSettings` | Built-in Dial checks and per-tool interaction review |
| Virtual joystick and gamepad | `VirtualGamepad`, `MouseJoystickWindow`, keyboard/mouse input services | Input packet tests and real foreground-app tests |
| Control center | `ControlCenterWindow`, `ControlCenterGeometry`, `ControlCenterLayout` | Four-side geometry regression and UI screenshots |
| Stage switcher and overview | `StageSwitcherWindow`, `WindowPreview`, bottom-edge state machine | Built-in simulated edge tests and interactive runner tests |
| Settings and localization | `SettingsWindow`, settings models/store, `Localization` | Navigation/editor smoke tests and import/export tests |
| QR scanner and media artwork | `QrScannerWindow`, `QrDecoder`, media services | Decoder fixture tests; camera/provider hardware retest |
| Windows integration | DWM, Win32 pointer APIs, taskbar, shell, system input services | Windows runner checks plus manual UIAccess and shell validation |

## Deterministic regression tests

`tests/TouchToys.RegressionTests` currently covers 20 deterministic cases:

- TT touchpad single click, double-tap binding, pinch-out, two-finger scroll, three-finger scroll, two-finger swipe, and hold.
- Middle-area double tap and hold.
- Global multi-touch two-finger swipe, hold, and rejection of an invalid wide contact set.
- Custom-shape normalization across translation/scale, single-stroke template matching, multi-contact recording completion, and two-finger path matching.
- Control-center geometry for all four sides.
- Bottom-edge thresholds and reveal/Dial geometry.
- Global touch relay idle-reset guard and retry backoff.

These tests exercise the recovered production classes directly; they do not replace tests of actual Windows pointer delivery, display composition, or hardware.

## Built-in application smoke suite

The original application assembly contains its own command-line test entry points. The main `--smoke-test` path checks window/task restoration, ball and Ark transitions, global relay simulation, control-center reveal, settings navigation and editors, Dial, task switcher, edge routing, launcher behavior, QR decoding, theme changes, and resource diagnostics. `--touch-smoke` adds injected touch checks. `--capture-ui` writes PNG captures through `TOUCHTOYS_TEST_OUTPUT_DIR`.

The test harness now treats an exception as a nonzero process exit code and removes stale output before each run, so a previous successful report cannot mask a later failure.

## Known limits and manual retests

- The original package contains compiled `App.xbf` but no original `.xaml` source. The recovered `App.xaml` has been round-trip checked through XbfKit; its comments and original formatting are not recoverable, and the remaining UI is predominantly constructed in C#.
- A hosted runner can test synthetic touch injection and UI layout, but it cannot prove behavior on every physical touchscreen, DPI configuration, GPU, or multi-monitor setup.
- The CI manifest intentionally omits `uiAccess`. Signed desktop redirection and elevated-window interaction must be retested with an appropriately trusted production certificate on a real Windows device.
- Camera capture, real media-provider integration, and system-shell taskbar/Start-menu behavior require device-level validation; the built-in tests explicitly identify several of these as not exercised.
- The 466-warning initial decompilation build includes async/no-await, nullable, and native-interop warnings. They require targeted review; suppressing them globally would hide useful parity defects.

Function parity is considered complete only after the deterministic suite, built-in UI/touch smoke suite, x64 and ARM64 package builds, and manual Windows-device checks all pass.
