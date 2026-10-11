# Task list (G-Helper cross-platform port)

Legend: `[x]` done · `[~]` partly done / not verifiable here · `[ ]` open · owner = agent task id. Sizes: S/M/L. Acceptance criteria are what the integrator checks.
Wave 1 items are `[x]` when merged into `feat/avalonia-port` with unit tests; none of the hardware paths were run on ASUS hardware.

## Phase 0 — Scaffold (DONE)
- [x] P0.1 Solution `GHelper.sln`, `Directory.Build.props`, projects Core / Platform.Windows / Platform.Linux / App / Tests, all `net10.0`
- [x] P0.2 Platform contracts `src/GHelper.Core/Platform/*` (IPlatform, IAsusBackend, ISystemInfo, IPowerService, IDisplayService, IInputService, IStartupService, IAudioService, IProcessService, IVendorServices, ICpuTuning, IGpuProvider, ISystemSensors, IFpsMonitor, IUiHost)
- [x] P0.3 Foundation ported: Logger, AppConfig, Modes, AsusACPI facade over IAsusBackend, Keys enum, Strings resources, `Program` statics
- Acceptance: `dotnet build GHelper.sln` green; `dotnet test tests/GHelper.Tests` green.

## Wave 1 — parallel, each in its own git worktree

### W1-A core-engine (L) — owner: core-engine
Owns: `src/GHelper.Core/{Program*.cs,Mode/,Battery/,Fan/,HardwareControl.cs,AutoUpdate/,UpdatesController.cs,Helpers/}` (not Logger/AppConfig: already done; extend only).
- [x] Port `app/Program.cs` orchestration into `Program` partial (SetAutoModes, PowerSource handling, SchedulePowerCheck, OnPowerSettled, Charge, suspend/resume/session/lid handlers) driven by `IPowerService` events instead of SystemEvents/WndProc. UI parts (tray, SettingsToggle window mgmt, theme) stay out and go through `IUiHost`.
- [x] Port `Mode/ModeControl.cs`, `Mode/Modes.cs` (complete), `Mode/PowerNative.cs` as static facade over `IPowerService`.
- [x] Port `Battery/BatteryControl.cs`, `Fan/FanSensorControl.cs`, `HardwareControl.cs` (OS sensors via `ISystemSensors`, GPU via `IGpuProvider`, ACPI temps/fans via `Program.acpi`), `Helpers/{TempHelper,ColorUtilities,ClamshellModeControl,MemoryHelper,TouchscreenHelper,Keystone,DynamicLightingHelper}` as facades.
- [x] Facades with legacy names: `Startup` (IStartupService), `AsusService` (IVendorServices), `ProcessHelper` (IProcessService + ISystemInfo), `Audio` (IAudioService), `RestrictedProcessHelper`, `OnScreenKeyboard` (IInputService), `DeviceHelper` (IVendorServices.GetGpuError).
- [x] Port `UpdatesController.cs` + `AutoUpdate/AutoUpdateControl.cs` logic (UI-free); repo constant `bberka/g-helper`, overridable via `AppConfig.GetString("update_repo")`. Self-update on Linux = notify + open release URL.
- Acceptance: all above compile in Core against contracts; no Windows API / WinForms reference; facade signatures equal legacy ones; unit tests for Modes dictionary/AppConfig parsing/Fan curve fixing (`AsusACPI.FixFanCurve`) in `tests/GHelper.Tests/Engine/`.

### W1-B core-hw (L) — owner: core-hw
Owns: `src/GHelper.Core/{Gpu/,Ally/,Pawn/,Display/,Overlay/}`.
- [x] `Gpu/`: GPUModeControl (UI calls → IUiHost), IGpuControl impls that are portable: NvidiaGpuControl + NvmlHelper via NVML loaded with `NativeLibrary` (nvml.dll / libnvidia-ml.so.1), NvidiaSmi, AmdGpuControl logic that is not ADL P/Invoke; `IntelVram` as facade over `IGpuProvider`. NVAPI/ADL-specific code is *not* ported here (Windows task does it behind `IGpuProvider`).
- [x] `Pawn/`: `CpuInfo` cross-platform (CPUID/proc), `RyzenSmu`, `IntelMsr` logic kept in Core but their driver IO abstracted behind a small `IPawnIo`-like interface defined in Core (Windows task implements with PawnIO, Linux with ryzen_smu/msr); `ICpuTuning` is implemented on top by platform tasks.
- [x] `Ally/AllyControl.cs` (UI calls → IUiHost).
- [x] `Display/`: ScreenControl, VisualControl, ColorProfileHelper, ScreenELMB, ScreenNative/ScreenBrightness as facades over `IDisplayService`/`Program.acpi`; gamma/ICC math stays in Core.
- [x] `Overlay/`: portable part of HardwareOverlay (sensor sampling, text/format model, config), FPS via `IFpsMonitor`. Drawing is done by the UI task.
- Acceptance: compiles in Core, no OS APIs, tests for gamma/color math + RyzenSmu code-name/family tables + NVML-unavailable graceful path.

### W1-C core-input-usb (L) — owner: core-input-usb
Owns: `src/GHelper.Core/{Input/ (except Keys.cs),USB/ (except AuraMode.cs),AnimeMatrix/,Helpers/AudioVisualizer.cs}`.
- [x] `USB/`: AsusHid, Aura (remove AuraMode enum copy, it already exists), AsusLampArray, XGM — HidSharp only; UI calls → IUiHost. Windows-only bits via facades.
- [x] `AnimeMatrix/`: AnimeMatrixDevice, SlashDevice, AniMatrixControl (logic only), MatrixFont, Communication/*; replace `WindowsUsbProvider` with a cross-platform `HidSharpUsbProvider` (same code works on Linux hidraw). Bitmap rendering must use a portable pixel buffer (no System.Drawing Bitmap) — define a tiny `GHelper.AnimeMatrix.PixelBuffer` or use `Span<byte>` RGBA.
- [x] `Input/`: InputDispatcher, MKeyControl, NumberPad, KeyboardListener: ASUS key events from `Program.acpi.AtkEvent` + HID report listener (HidSharp), key injection through `IInputService`; no WinForms.
- [x] `AudioVisualizer`: FFT on samples from `IAudioService.StartLoopbackCapture` (FftSharp).
- Acceptance: compiles in Core; matrix frame encoding + Aura packet building covered by unit tests (golden byte arrays computed from legacy code reading).

### W1-D core-peripherals (M) — owner: core-peripherals
Owns: `src/GHelper.Core/Peripherals/**`.
- [x] Copy `app/Peripherals/**` (51 files: mouse models, keyboard, headset, provider, event listener, IPeripheral) into Core; remove Windows-isms (device-change notification → HidSharp `DeviceList.Local.Changed`, `Program.settingsForm` → IUiHost).
- Acceptance: compiles; tests for mouse DPI/packet encoders that are pure functions; no Windows API.

### W1-E platform-windows (L) — owner: platform-windows
Owns: `src/GHelper.Platform.Windows/**`, `tests/GHelper.Tests/Windows/**`.
- [x] `WindowsPlatform` + every service. Lift code from: AsusACPI.cs (ATKACPI IOCTL, WMI DEVS, ATK4001 event, AsusAtkWmiEvent), Mode/PowerNative.cs, Display/{DisplayNative,ScreenNative,ScreenBrightness,ScreenCCD,ScreenELMB}.cs, Input/{KeyboardHook,KeyboardListener}.cs low-level parts, NativeMethods.cs, Helpers/{AsusService,Audio,DeviceHelper,DynamicLightingHelper,Keystone,OnScreenKeyboard,ProcessHelper,RestrictedProcessHelper,Startup,MemoryHelper,TouchscreenHelper}.cs, Gpu/{NVidia/Nvapi bits,AMD/AmdAdl2.cs,IntelVram.cs}, Pawn/{PawnIOWrapper,...} + embedded `*.bin`, Overlay/EtwFpsMonitor.cs, HardwareControl.cs OS sensor parts (PerformanceCounter/WMI battery/CPU temp).
- [x] System events (power, lid, display, session, suspend, theme) via `Microsoft.Win32.SystemEvents` + a hidden message-only window using `RegisterPowerSettingNotification`.
- [x] Graceful degradation on arm64/x86 (NVAPI, PawnIO are x64 only): `IsSupported=false`, no exceptions.
- Acceptance: project builds; `new WindowsPlatform()` constructs on Linux CI without touching Windows APIs (lazy); unit tests of pure helpers (power-plan guid mapping, config parsing); manual-test notes in report.

### W1-F platform-linux (L) — owner: platform-linux
Owns: `src/GHelper.Platform.Linux/**`, `tests/GHelper.Tests/Linux/**`, `packaging/linux/**`.
- [x] `LinuxPlatform` + every service. `SysfsRoot` abstraction (default `/`) so tests run on a fake tree.
- [x] `LinuxAsusBackend`: map every `AsusACPI` device id used by Core (see constants in `src/GHelper.Core/AsusACPI.cs`) to asus-wmi sysfs / firmware-attributes / platform_profile / hwmon / power_supply; debugfs raw fallback; `AtkEvent` from the "Asus WMI hotkeys" / "Asus Keyboard" evdev nodes (/dev/input/event*).
- [x] Power: power-profiles-daemon (DBus via `busctl`/`gdbus` or Tmds.DBus) with platform_profile fallback; events from logind (PrepareForSleep, Lock/Unlock, LidClosed) + UPower (OnBattery).
- [x] Display: internal panel via DRM/sysfs backlight (`/sys/class/backlight`), refresh rate via `xrandr`/`wlr-randr`/`kscreen-doctor`/`gnome-randr` best effort, external display detection via DRM connector status.
- [x] Input: evdev (read) + uinput (inject) with `/dev/uinput`; global hotkeys best effort.
- [x] Startup: `~/.config/autostart/ghelper.desktop` + optional systemd user unit; Process: single instance via flock + unix socket; elevation via `pkexec`; open URL via `xdg-open`.
- [x] Audio: PipeWire/PulseAudio via `pactl`/`wpctl` (mic mute, mute state, loopback via `parec`).
- [x] GPU: NVML (shared Core code), amdgpu hwmon/`gpu_busy_percent`/`mem_info_vram_*`, Intel via i915/xe sysfs; CpuTuning: `/sys/kernel/ryzen_smu_drv` + `/dev/cpu/*/msr`; sensors: hwmon (k10temp/coretemp/asus), /proc/stat, /proc/meminfo, power_supply.
- [x] Vendor services: detect `asusd`/`supergfxd`/`power-profiles-daemon` conflicts, `StopAsusServices` = `systemctl` (only when elevated).
- [x] Packaging inputs: `packaging/linux/99-ghelper.rules` (udev: hidraw for ASUS vendor 0b05, input/uinput access), `ghelper.desktop`, polkit action, README for kernel requirements.
- Acceptance: builds; fake-sysfs unit tests for backend id mapping (fan curves, PPT, charge limit, platform profile); every service returns safely on a machine with no ASUS hardware.

### W1-G ui-shell (L) — owner: ui-shell
Owns: `src/GHelper.App/**`.
- [x] Avalonia 12.1.x bootstrap (`Entry`/`App.axaml`): registers `WindowsPlatform`/`LinuxPlatform` by `OperatingSystem`, then starts UI; single-instance handled via `IProcessService`; `--args` handling like legacy (charge, cpu, gpu, services, uv, colors).
- [x] Theme system replicating `app/UI/RForm.cs` palette (dark/light, follows OS), reusable controls ported from `app/UI/*` (RButton w/ border colour + secondary style, RCheckBox, Slider (custom drawn), RComboBox, RNumericUpDown, RTextBox, RTrackBar, color picker/button, fan-curve chart control replacing RChart/DataVisualization chart, badge button).
- [x] Tray icon (Avalonia `TrayIcon`), context menu (GPU modes, quit), icon swap by mode (assets from `app/Resources`), left click toggles window, near-taskbar positioning logic from legacy.
- [x] Toast/OSD window (port `Helpers/ToastForm.cs`, no focus steal, transparent, topmost).
- [x] Main window layout = visual port of `app/Settings.Designer.cs` (performance, GPU, screen, keyboard, battery, matrix/controller panels, footer buttons) bound to a `MainViewModel` exposing the same state the legacy `Visualise*` methods render. Behaviour wiring to Core is W2; here: layout, bindings, design-time mock, `IUiHost` implementation (`AvaloniaUiHost`).
- [x] Assets: copy used images from `app/Resources` to `src/GHelper.App/Assets` (AvaloniaResource).
- Acceptance: `dotnet build` green; app launches headless-safe (`Entry.Main` returns 0 for `--version`); screenshot of main window layout from an Avalonia headless test or description of manual check.

### Integration I1 (me)
- [x] Merge W1 branches, delete `src/GHelper.Core/_Stubs`, make `dotnet build GHelper.sln` + tests green (847 tests), publish smoke: all 8 RIDs publish (W2-E).

## Wave 2 (after I1)
Status as of the W2-E run. `[x]` = landed on `feat/avalonia-port` and covered by unit / headless tests; nothing here is
verified on ASUS hardware. `[~]` = partly done or not verifiable from the authoring host.
- [x] W2-A main window wiring (Settings.cs logic <-> Core): `MainBackend`, Core bootstrap, exit cleanup, headless `charge`
- [x] W2-B Fans + Extra windows (fans and power window, extra settings window)
- [x] W2-C Matrix, Slash, Handheld (Ally), Updates, OverlayConfig + HardwareOverlay window
- [ ] W2-D AsusMouse/Keyboard/Headset settings windows (Core `Peripherals/` is ported, the three windows are not)
- [x] W2-E packaging, CI, docs (owner: packaging-ci-docs)
  - [x] self-contained single-file publish for win-x64, win-x86, win-arm64, linux-x64, linux-arm64, linux-arm,
        linux-musl-x64, linux-musl-arm64 (verified with `dotnet publish -r <rid>` from a Windows host; only win-x64 was executed)
  - [x] `publish.ps1`; `packaging/linux/build-packages.sh` (tar.gz, deb, rpm), `install.sh`, `uninstall.sh`, maintainer scripts, rpm spec
  - [x] `.github/workflows/{build,release,codeql}.yml` (build + test on windows / ubuntu, matrix release for 8 RIDs, checksums, attestation)
  - [x] README.md, docs/README.md, crowdin.yml
  - [~] not verified: the workflows have not run on GitHub; the rpm build (no `rpmbuild` on the authoring host); the deb and
        tar.gz were produced and inspected on Windows only (file modes and the symlink need a Linux run, which `build.yml` does)
  - [ ] follow-ups for other owners: (a) `AutoUpdateControl.ParseRelease` picks the last `.zip` asset of a release; with one
        zip per Windows architecture it must select the `win-<arch>` asset; (b) Linux self-update is notify-only, the
        `.tar.gz` / `.deb` / `.rpm` assets are not consumed by the updater; (c) `docs/README.*.md` translations still link to
        upstream releases; (d) trimming needs source generated JSON in `AppConfig` and trim safe HidSharp / WMI use before it
        can be enabled (win-x64 probe: 51 MB -> 22 MB, 15 trim warnings)
- [ ] P6.3 final review. `app/` is KEPT as the upstream mirror (merge upstream into it, port into `src/`); keep the `legacy-winforms` job in `build.yml` only if upstream changes should still be compile-checked

## Checkpoints
- After W1: Core builds with all areas; platform projects implement all contracts; app shell launches.
- After W2-A: app runs on Windows with parity for the main window; on Linux shows detected hardware and applies modes.
- Final: all RIDs publish; tests green; README states supported hardware/kernels.
