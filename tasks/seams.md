# Platform seams

The platform contracts (`src/GHelper.Core/Platform/*`) have two kinds of seams that compile without an implementation and
then silently do nothing: **C# default interface members** and **optional capability interfaces** that Core discovers with
`PlatformCapabilities.GetOptional<T>()` / `PlatformServices.Current.<Service> as T`.

`tests/GHelper.Tests/Seams/SeamCoverageTests.cs` holds the table below (kept in sync by hand):
* `WindowsPlatform` must provide every seam,
* `LinuxPlatform` must provide exactly the ones marked *yes* (a seam that is implemented but marked N/A fails as well),
* a new default member / optional interface without a row fails the build of the test project.

Rule of thumb used when reconciling: **Core's call site is the single source of truth**; the platform adapts to it, and
contract members nothing consumed were deleted (`GetElmbStatus`, `IPowerService.TurnOffDisplay`, `IVendorServices.InitDynamicLighting`,
`ICpuTuning.SmuFamily`, `IAmdApuControl`, ...).

## Default interface members

| Seam | Core consumer | Windows | Linux |
|---|---|---|---|
| `IDisplayService.SplendidDirectory` | `VisualControl.GetSplendidPath` (AsusSplendid.exe) | `AsusDriverPath` (WMI ATKWMIACPIIO folder) | N/A: ASUS Splendid is Windows ASUS software |
| `IDisplayService.GetGameVisualStatus` / `SetGameVisualStatus` | `VisualControl.IsEnabled` / `SetRegStatus` | HKCU Armoury Crate `ActiveGVStatus` | N/A: same |
| `IDisplayService.ElmbAccess` | `ScreenELMB` (model gate `AppConfig.IsELMB` stays in Core) | `ElmbControl` (Intel ControlLib / NVAPI DPCD 0x44C) | N/A: no portable AUX channel access |
| `IDisplayService.SetAsusRefreshFlag` | `ScreenControl.SetAsusRefreshFlag` (only when elevated) | HKLM WOW6432Node Uninstall `{8714A8D1-...}` `RefreshFlag` | N/A: ASUS Armoury registry key |
| `ICpuTuning.CreatePawnIo` | `PawnModules.CreateDriver` -> `RyzenSmuService` / `IntelMsr` defaults | `PawnCpuTuning.CreatePawnIo` -> `PawnIoDevice` | N/A: ryzen_smu exposes finished SMU commands, not the PawnIO module protocol; `LinuxCpuTuning` is a direct `ICpuTuning` with the same command table |
| `ICpuTuning.IsDriverInstalled` | `ModeControl.IsPawnInstalled` ("install PawnIO" hint) | PawnIO device present (even when access is denied) | default = `IsSupported` (ryzen_smu loaded / RAPL readable) is the right answer |
| `IGpuProvider.IntelVramSizes` / `IntelVramIndex` | `Gpu.IntelVram` facade | registry `DedicatedSegmentSize` | N/A: Windows driver registry setting |
| `IGpuProvider.GetDiscretePowerState` | `NvidiaGpuControl` (never wake a powered-down dGPU) | `NvapiGpu` (NVAPI_GPU_NOT_POWERED) | PCI `power/runtime_status` |
| `IGpuProvider.CreateAmdDriver` | `AmdGpuControl` (all ADL / amdgpu access) | `AdlAmdDriver` (ADL2) | `SysfsAmdGpuDriver` (amdgpu hwmon, DRM, `panel_power_savings` as the SCE state) |
| `IFpsMonitor.TargetPid` | `OverlayModel.Tick` pins the counter to the foreground game | `EtwFpsMonitor` | N/A: no FPS source on Linux (`IsSupported` false) |
| `IFpsMonitorFactory.GetForegroundProcessId` | `OverlayModel` (FPS pin, game-only visibility) | `GetForegroundWindow` + `GetWindowThreadProcessId` | N/A: only meaningful together with FPS |
| `IFpsMonitorFactory.ForegroundChanged` | `OverlayModel.Start/Stop` -> `OnForegroundChanged` | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` on a message window | N/A: same |

`IUiHost.ShowWindow` is a default member too but belongs to the UI head (GHelper.App), not to a platform.

## Optional capability interfaces

| Seam | Core consumer | Windows | Linux |
|---|---|---|---|
| `IDynamicLightingEffects` | `DynamicLightingHelper.SetEffect` (Aura "dynamic lighting only") | `WindowsVendorServices.SetEffect` | N/A: Windows 11 Dynamic Lighting registry |
| `IDriverInventory` | `UpdatesController.ResolveStatus` | `WindowsVendorServices` -> `DriverInventory` (CfgMgr32, HKLM ASUS, staged oem INF, Store packages) | N/A: Windows driver list; every update stays "not found" |
| `ICpuFamilyInfo` | `ModeControl` (periodic re-apply by SMU family) | `PawnCpuTuning` (`RyzenSmuService.Family` / `CpuCodeName`, after `Initialize`) | `LinuxCpuTuning` (CPUID family/model -> command family; no code name) |
| `IApuEnergySensors` | `HardwareControl` (APU / cores power split) | `WindowsSystemSensors` ("Energy Meter" channels) | N/A: Core falls back to amdgpu hwmon `AsicPower` / `GfxPower` |
| `IAsusChargeLimitSync` | `BatteryControl.SetAsusChargeLimit` | `WindowsVendorServices` (HKLM `ChargingRate`) | N/A: ASUS System Control Interface registry |
| `IPowerModeCatalog` | `PowerNative.powerModes` | `WindowsPowerService` (`PowerSchemes.Names`) | `LinuxPowerService` (power-saver / balanced / performance) |
| `IDisplaySettingsEvents` | `ClamshellModeControl` | `WindowsDisplayService` (`SystemEvents.DisplaySettingsChanged`) | `LinuxDisplayService` (DRM connector poll, 2 s while subscribed) |
| `ISessionEndingEvents` | `Program.OnSessionEnding` | `WindowsPowerService` (`SystemEvents.SessionEnding`) | `LinuxPowerService` (logind `PrepareForShutdown`) |
| `IGpuOverclock` | `NvidiaGpuControl` `GetClocks` / `SetClocks` / clock lock, preferred over NVML | `WindowsGpuProvider` -> `NvapiGpu` (PerformanceStates20, ClockBoostLock) | N/A: Core's NVML path (`nvmlDeviceSetGpcClkVfOffset`, locked clocks, nvidia-smi) is used directly |
| `IInputExtras` | `InputDispatcher` (backlight idle timeout, modifiers, screen off, touchpad state, slate mode, ASUS software path) | `WindowsInputService` | `LinuxInputService`: idle = Mutter IdleMonitor / `xprintidle` / evdev, modifiers from evdev, screen off via xset / kscreen-doctor / swaymsg / GNOME screensaver, touchpad via gsettings / xinput; `SetSlateMode`, `GetAsusSoftwarePath` are no-ops (Windows only) |
| `INumberPadDriver` | `NumberPad` | `WindowsInputService` (`\\.\AsusTPPC` IOCTL) | N/A: Windows ASUS driver |
| `IScreenCapture` | `Aura.ApplyAmbient` | `WindowsDisplayService` (`ScreenGrab`: GDI StretchBlt) | `LinuxDisplayService` (`X11ScreenGrab`: libX11 root grab; refused on Wayland) |

`IUiActionHandler` and `MatrixRendering.IMatrixTextRenderer` / `IMatrixImageDecoder` are implemented by the UI head and are not
platform seams.

## One path per feature

* **CPU tuning**: Core's `RyzenSmuService` / `IntelMsr` over `IPawnIo`, wrapped by `PawnCpuTuning` (Core). Windows only supplies
  `PawnIoDevice : IPawnIo`. Linux keeps its own `ICpuTuning` (see `ICpuTuning.CreatePawnIo` above).
* **GPU**: Core's `NvidiaGpuControl` (NVML, shared by Windows and Linux) and `AmdGpuControl` (over `IAmdGpuDriver`). Platform
  specific bits are only the seams above. Linux `SysfsGpuControl` remains for nouveau / nova_core and Intel dGPUs, which have
  neither NVML nor amdgpu.

## Contract members without a Core consumer (kept, implemented on both platforms)

`IDisplayService.AdjustBrightness`, `SupportsGammaRamp`, `SetGammaRamp`, `IsColorProfileInstalled`, `InstallColorProfile`,
`IInputService.MouseWheel`, `ListenKeys`, `IProcessService.OpenFolder`, `IPowerService.IsLidClosed`, `ISystemInfo.OsDescription`.
They are not default members (so they cannot silently no-op) and neither Core nor the legacy app call them; they were left for
the UI head / diagnostics rather than removed.
