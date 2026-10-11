# Implementation Plan: G-Helper cross-platform port (Avalonia, Windows + Linux, all CPU architectures)

Fork: https://github.com/bberka/g-helper (GPL-3.0, upstream copyright notices stay in LICENSE / headers).
Branch: `feat/avalonia-port`. Legacy WinForms app stays in `app/` as the **reference spec** and as an untouched mirror of upstream (we merge upstream regularly, see README "Upstream sync and releases"); it is not deleted (task P6.3 changed accordingly).

## Overview
G-Helper is a Windows-only WinForms tray app for ASUS laptops (ROG/TUF/Zenbook/Vivobook/Ally): performance modes, fan curves,
PPT limits, GPU mode (Eco/Standard/Ultimate), screen refresh/overdrive, battery charge limit, keyboard backlight / Aura,
AniMe Matrix / Slash, peripherals (mice/keyboards/headsets over HID), hotkeys, overlay, updates.
Goal: one code base that runs natively on **Windows (x64, x86, arm64)** and **Linux (x64, arm64, arm, musl-x64, musl-arm64)**
with an Avalonia UI. (macOS: out of scope, ASUS laptops don't run it. linux-riscv64: Avalonia/Skia has no build.)

## Architecture decisions
1. **Three layers, all `net10.0`** (no `-windows` TFM, so everything cross-compiles and cross-publishes from either OS;
   Windows-only API use is guarded with `OperatingSystem.IsWindows()` and CA1416 is suppressed):
   - `src/GHelper.Core` – all logic: AppConfig, AsusACPI facade, modes, fans, battery, GPU modes, Aura/USB/HID, AnimeMatrix/Slash,
     peripherals, input dispatching, display logic, updates, orchestration (`Program`). No UI toolkit, no OS-specific API.
   - `src/GHelper.Platform.Windows` / `src/GHelper.Platform.Linux` – implementations of the `GHelper.Platform.I*` contracts in
     `src/GHelper.Core/Platform/`. Only these two projects may contain P/Invoke, WMI, registry, sysfs, DBus, evdev ...
   - `src/GHelper.App` – Avalonia 12 head: bootstrap (registers platform), tray, windows, controls, theming, `IUiHost` implementation.
2. **Contracts are derived from the legacy static classes** (PowerNative, ScreenNative, Startup, AsusService, ProcessHelper, KeyboardHook ...).
   Core keeps the *legacy class names and signatures* as thin static facades that delegate to `PlatformServices.Current.*`, so ported
   logic compiles with minimal edits and behaviour stays diffable against `app/`.
3. **ASUS hardware access**: `IAsusBackend` (DSTS/DEVS by device id). Windows = `\\.\ATKACPI` DeviceIoControl (existing code).
   Linux = `asus-wmi` kernel driver: sysfs attributes mapped from device ids (throttle_thermal_policy/platform_profile, ppt_*, nv_*,
   dgpu_disable, gpu_mux_mode, panel_od, mini_led_mode, charge_control_end_threshold, kbd_backlight, fan curves via hwmon) with
   debugfs `dev_id/ctrl_param/devs/dsts` as raw fallback. The Linux backend takes a configurable filesystem root so it is unit-testable
   against a fake tree.
4. **Core ↔ UI**: Core never references UI types. `Program.Ui` (`IUiHost`) gives Core `Post/Toast/Confirm/Alert/ToggleSettings/Refresh(area)/Quit`.
   Legacy `Program.settingsForm.VisualiseXxx()` calls become `Program.Ui.Refresh("xxx")` or Core events.
5. **Keys**: `GHelper.Input.Keys` is a generated copy of `System.Windows.Forms.Keys` (same values) so ported hotkey code is unchanged.
6. **Config/log location** comes from `ISystemInfo.ConfigDirectory` (Windows `%APPDATA%\GHelper`, Linux `$XDG_CONFIG_HOME/ghelper`). config.json format unchanged,
   so a Windows config keeps working.
7. **Localization**: `Properties/Strings*.resx` moved to Core (same ResourceManager); crowdin.yml path updated in P6.
8. **Privileges on Linux**: writes to sysfs/debugfs/hidraw need root or udev rules. Ship `packaging/linux/99-ghelper.rules` +
   a polkit/`pkexec` relaunch path (`IProcessService.RunAsAdmin`) rather than requiring a root GUI.
9. **Updates** target `bberka/g-helper` GitHub releases (one constant, overridable by config key `update_repo`).
10. Risks we cannot fully verify without hardware: Linux sysfs semantics per model, Windows ARM64 (NVAPI/PawnIO are x64-only → graceful
    "unsupported"). Mitigation: fake-root unit tests, capability flags, every service degrades to no-op.

## Dependency graph
```
Phase 0 scaffold + contracts (DONE, this commit)
   ├─ W1-A core-engine ─┐
   ├─ W1-B core-hw     ─┤  (merge; delete src/GHelper.Core/_Stubs; make Core build)  ── Integration I1
   ├─ W1-C core-input-usb┤
   ├─ W1-D core-peripherals
   ├─ W1-E platform-windows ┐
   ├─ W1-F platform-linux   ┴─ (implement contracts; unit tests)
   └─ W1-G ui-shell (Avalonia head, theme, controls, tray, toast, main window layout, IUiHost)
Integration I1 ─→ W2: main window wiring, Fans/Extra, Matrix/Slash/Handheld/Updates/Overlay, peripheral windows,
                      packaging + CI, delete app/, docs
```

## Task list
Tracked in `tasks/todo.md` (checklist with acceptance criteria). Agent rules: `tasks/AGENT_RULES.md`.

## Open questions (do not block)
- Final app icon / branding for the fork (currently the legacy favicon).
- Whether to ship AppImage/Flatpak (needs udev/polkit story) or just .deb/.rpm/tar.gz. Plan: tar.gz + .deb first.
