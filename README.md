# G-Helper (cross-platform fork)

A control tool for ASUS laptops: performance modes, fan curves, power limits, GPU mode, screen refresh rate and
overdrive, battery charge limit, keyboard backlight and lighting effects, AniMe Matrix / Slash, ASUS mice, keyboards and
headsets, hotkeys and a metrics overlay. It talks to the same firmware interfaces as Armoury Crate, without the
background services.

This repository is a fork of [seerge/g-helper](https://github.com/seerge/g-helper) (a Windows-only WinForms
application) that is being ported to Avalonia so that one code base runs on **Windows and Linux** on all common CPU
architectures. The original author's work, history and license are preserved; see [Credits](#credits-and-license).

> [!NOTE]
> **Status: work in progress.** The port is checked with unit tests and headless UI tests only. The Linux backend
> (asus-wmi sysfs, udev, polkit, DBus tools) has **not been tested on real ASUS hardware yet** and the Windows
> port has not had a full hardware regression against the original application. Expect missing features and bugs,
> and report them with the log file (see [Troubleshooting](#troubleshooting)).

## Supported platforms

| OS | Architecture | Runtime identifier | Release artifact | Notes |
|---|---|---|---|---|
| Windows 10 / 11 | x64 | `win-x64` | `GHelper-<version>-win-x64.zip` | all features |
| Windows 10 / 11 | x86 | `win-x86` | `GHelper-<version>-win-x86.zip` | no NVAPI / PawnIO (x64 only), those features report "unsupported" |
| Windows 11 | arm64 | `win-arm64` | `GHelper-<version>-win-arm64.zip` | no NVAPI / PawnIO (x64 only); untested on ASUS Snapdragon laptops |
| Linux (glibc) | x64 | `linux-x64` | `.tar.gz`, `.deb` (amd64), `.rpm` (x86_64) | kernel 6.x with asus-wmi recommended |
| Linux (glibc) | arm64 | `linux-arm64` | `.tar.gz`, `.deb` (arm64), `.rpm` (aarch64) | published for completeness; ASUS Snapdragon laptops have no asus-wmi interface, so hardware features will mostly be unavailable |
| Linux (glibc) | arm | `linux-arm` | `.tar.gz`, `.deb` (armhf), `.rpm` (armv7hl) | published for completeness, no known ASUS laptop |
| Linux (musl, e.g. Alpine) | x64, arm64 | `linux-musl-x64`, `linux-musl-arm64` | `.tar.gz` | no deb/rpm |

Not supported: macOS (ASUS laptops do not run it), linux-riscv64 (Avalonia/Skia ship no native build), 32-bit x86 Linux.

All artifacts are self-contained single files (.NET runtime and Avalonia included), nothing has to be installed first.
The download is large (about 45 to 50 MB) because trimming is not enabled, see [Build from source](#build-from-source).

## Install

### Windows

1. Download `GHelper-<version>-<rid>.zip` from the [releases](https://github.com/bberka/g-helper/releases), extract
   `GHelper.exe` anywhere and run it. The program does not install anything. It runs with normal user rights and asks
   for elevation only when a feature needs it.
2. Configuration and the log file are in `%APPDATA%\GHelper` (the same `config.json` as the original application).

The binary is not code signed (the original project's signing certificate does not apply to this fork), so
SmartScreen may warn on first start.

### Linux

Debian / Ubuntu:

```sh
sudo apt install ./ghelper_<version>_amd64.deb
```

Fedora / openSUSE / RHEL family:

```sh
sudo dnf install ./ghelper-<version>-1.x86_64.rpm
```

Any other distribution (also Alpine with the `musl` archives):

```sh
tar -xzf ghelper-<version>-linux-x64.tar.gz
cd ghelper-<version>-linux-x64
sudo ./install.sh        # copies root/ to /, creates the ghelper group, reloads udev; uninstall.sh reverts
```

Then log in again (or `sudo usermod -aG ghelper $USER`) and start **G-Helper** from the application menu or `ghelper`.
The package installs everything below `/usr/lib/ghelper` plus a udev rules file, a polkit action, a desktop entry and
an icon. There is no AppImage or Flatpak: the hardware access needs host-level udev and polkit integration that a
sandbox cannot provide.

#### Linux requirements

* An ASUS laptop whose kernel driver `asus-wmi` / `asus-nb-wmi` is loaded (`ls /sys/devices/platform/asus-nb-wmi`).
  Newer features need newer kernels: `platform_profile` class 6.14, `asus-armoury` PPT / GPU / panel attributes 6.19,
  charge limit 5.4, dGPU disable 5.17, GPU MUX 6.1, mini-LED 6.5, custom fan curves around 5.17. The complete mapping
  of every feature to its kernel interface is in [packaging/linux/README.md](packaging/linux/README.md).
* Desktop: X11 or Wayland (via XWayland), fontconfig, ICU (without it start with
  `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`; the UI is then English only).
* Optional helpers: `power-profiles-daemon` (power profiles), `pkexec` / polkit (run elevated), `wpctl` or `pactl`
  (microphone mute, audio visualizer), `xrandr`, `wlr-randr`, `kscreen-doctor` (refresh rate), `xdg-utils`.
* ryzen_smu (out of tree) for Ryzen tuning; `msr` / RAPL for Intel power readings.
* Do not run it together with `asusd` / `supergfxd` (asusctl); G-Helper detects them and can stop them.

#### Linux permissions

Writing to sysfs, hidraw, evdev and `/dev/uinput` needs permissions that the package grants through udev:

* the user logged in at the seat gets them automatically (`uaccess`), and
* members of the group `ghelper` get them too (`sudo usermod -aG ghelper $USER`, then log in again), which also covers
  SSH and autostart before login.

Everything that needs real root (debugfs raw access, ryzen_smu, `systemctl`) goes through `pkexec` with the
`org.ghelper.run-elevated` polkit action ("Run as administrator" in the application). Without any of this the
application still starts and every feature without access reports "unsupported". To keep the battery charge limit
across reboots enable `sudo systemctl enable ghelper-charge.service`.

## Features

Performance modes (Silent / Balanced / Turbo plus custom modes), fan curve editor, power limits (SPL / sPPT / fPPT,
APU, NVIDIA dynamic boost and temperature target), GPU modes (Eco / Standard / Ultimate / Optimized), refresh rate and
overdrive, battery charge limit, keyboard backlight and Aura modes, Anime Matrix / Slash, hotkeys (Fn keys, M keys,
custom actions), ROG Ally controller features, XG Mobile, ASUS mice / keyboards / headsets, metrics overlay, BIOS and
driver update check. Details and the key bindings: [docs/README.md](docs/README.md). Which of them exist on Linux
depends on the kernel driver; the table in [packaging/linux/README.md](packaging/linux/README.md) is authoritative.

## Build from source

Requirements: the [.NET 10 SDK](https://dotnet.microsoft.com/download). Building and testing works on Windows and
Linux, and any RID can be published from either OS.

```sh
dotnet build GHelper.sln -c Release
dotnet test GHelper.sln -c Release

# self-contained single file for one RID (output: artifacts/publish/<rid>)
dotnet publish src/GHelper.App/GHelper.App.csproj -r linux-x64 -c Release -o artifacts/publish/linux-x64

# all RIDs, optional archives (PowerShell 7)
./publish.ps1 [-Rid win-x64,linux-arm64] [-Archive]

# Linux packages (tar.gz, deb, rpm) from a published directory; run on Linux
packaging/linux/build-packages.sh --rid linux-x64
```

Publish settings live in `src/GHelper.App/GHelper.App.csproj` and apply whenever a runtime identifier is given:
self-contained, single file, native libraries extracted to `~/.net` (`%TEMP%\.net`) on first start, compressed bundle,
`app.manifest` embedded on Windows (`asInvoker`). **Trimming is off** because Core relies on reflection
(`System.Text.Json` config, HidSharp, WMI) and has not been verified trim safe; `-p:PublishTrimmed=true` currently
produces trim warnings and is not supported. ReadyToRun is opt in (`-p:PublishReadyToRun=true` or
`./publish.ps1 -ReadyToRun`): faster start up, about 35% larger. The Linux package layout and the .deb / .rpm
details are in [packaging/linux/README.md](packaging/linux/README.md).

| Path | Content |
|---|---|
| `src/GHelper.Core` | all logic: configuration, modes, fans, battery, GPU, Aura / USB / HID, Anime Matrix, peripherals, updates; no UI and no OS specific API |
| `src/GHelper.Platform.Windows` | Windows implementations of the `GHelper.Platform` contracts (ATKACPI, WMI, registry, NVAPI, PawnIO ...) |
| `src/GHelper.Platform.Linux` | Linux implementations (asus-wmi sysfs, hwmon, evdev / uinput, logind, hidraw ...) |
| `src/GHelper.App` | Avalonia 12 application: tray, windows, controls, theme |
| `tests/` | unit tests and headless UI tests |
| `packaging/linux` | udev rules, polkit action, systemd units, desktop entry, deb / rpm / tar.gz build |
| `app/` | the original WinForms application, kept as behavioural reference until the port reaches parity |

## Troubleshooting

* Log file: `%APPDATA%\GHelper\log.txt` on Windows, `${XDG_CONFIG_HOME:-~/.config}/ghelper/log.txt` on Linux.
* Linux: nothing works but the window opens: check `ls -l /sys/devices/platform/asus-nb-wmi` and
  `id` (group `ghelper`), see [packaging/linux/README.md](packaging/linux/README.md).
* Upstream wiki pages ([FAQ](https://github.com/seerge/g-helper/wiki/FAQ),
  [Troubleshooting](https://github.com/seerge/g-helper/wiki/Troubleshooting),
  [Power user settings](https://github.com/seerge/g-helper/wiki/Power-user-settings)) describe the Windows behaviour,
  which this fork keeps.

## Contributing

Issues and pull requests are welcome, in particular **hardware test reports for Linux** (model, kernel version,
`ls /sys/devices/platform/asus-nb-wmi /sys/class/firmware-attributes`, what worked). Please read
[.github/CONTRIBUTING.md](.github/CONTRIBUTING.md) first. Rules of the port (layering, where Windows- or
Linux-specific code may live) are in [tasks/AGENT_RULES.md](tasks/AGENT_RULES.md) and [tasks/plan.md](tasks/plan.md).

Translations are managed with Crowdin (`crowdin.yml`, source `src/GHelper.Core/Properties/Strings.resx`).

## Credits and license

G-Helper was created by [Serge](https://github.com/seerge) ([seerge/g-helper](https://github.com/seerge/g-helper));
this fork keeps his copyright notices and builds on the work of the original contributors, and on
[asusctl](https://gitlab.com/asus-linux/asusctl), the Linux asus-wmi driver, [Starlight](https://github.com/vddCore/Starlight),
[NvAPIWrapper](https://github.com/falahati/NvAPIWrapper), [PawnIO](https://github.com/namazso/PawnIO),
[UXTU](https://github.com/JamesCJ60/Universal-x86-Tuning-Utility), [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB),
[Avalonia](https://avaloniaui.net/) and [HidSharp](https://github.com/IntergatedCircuits/HidSharpCore).

Licensed under the [GNU General Public License v3.0](LICENSE).

"ASUS", "ROG", "TUF" and "Armoury Crate" are trademarks of ASUSTeK Computer Inc.; they are used for identification
only and this project is not affiliated with or endorsed by ASUS. The software is provided as is, without warranty;
changing power limits, fan curves and GPU settings can cause system instability.

This program does not transmit any information to other networked systems, apart from the update check against
GitHub releases and the optional BIOS / driver lookup against ASUS servers.
