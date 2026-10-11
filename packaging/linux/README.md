# G-Helper on Linux: installation inputs and kernel requirements

Files in this directory are inputs for the distribution packages (deb / rpm / tar.gz); they are not executed from
the source tree.

| File | Install location | Purpose |
|---|---|---|
| `99-ghelper.rules` | `/etc/udev/rules.d/` or `/usr/lib/udev/rules.d/` | hidraw (ASUS vendor `0b05`), input event nodes, `/dev/uinput` and sysfs attribute access for the logged in user / group `ghelper` |
| `ghelper-udev-perms` | `/usr/lib/ghelper/` (mode 0755) | helper run by the rules: makes the asus-wmi / asus-armoury / platform_profile / battery / LED / fan curve attributes group-writable |
| `ghelper.sysusers.conf` | `/usr/lib/sysusers.d/ghelper.conf` | creates the `ghelper` group |
| `org.ghelper.policy` | `/usr/share/polkit-1/actions/` | polkit action used by `pkexec` to relaunch G-Helper elevated |
| `ghelper-root-launcher` | `/usr/lib/ghelper/` (mode 0755) | the only program the polkit action authorises; starts `/usr/lib/ghelper/ghelper` with a whitelisted environment |
| `ghelper.desktop` | `/usr/share/applications/` | application menu entry (the autostart entry is written by the app itself) |
| `ghelper.service` | `/usr/lib/systemd/user/` (packages) or `~/.config/systemd/user/` | optional user unit (`GHELPER_STARTUP=systemd`) |
| `ghelper.png` | `/usr/share/icons/hicolor/256x256/apps/` | application icon (256 px image taken from `src/GHelper.App/favicon.ico`) |
| `ghelper-charge.service` | `/etc/systemd/system/` or `/usr/lib/systemd/system/` | re-applies the charge limit at boot |

The paths inside the rules, the policy and the units assume `/usr/lib/ghelper/ghelper` (the published apphost). Adjust
them together if the package installs somewhere else (for example `/opt/ghelper`).

Run without root: add the user to the group (`sudo usermod -aG ghelper $USER`, log in again) or rely on the `uaccess`
tag of the logged in seat user. Without either, G-Helper still starts and every feature that needs a missing
permission reports "unsupported"; "Run as administrator" in the app uses `pkexec`.

## Building the packages

```sh
# 1. self-contained single file for one RID (linux-x64, linux-arm64, linux-arm, linux-musl-x64, linux-musl-arm64)
dotnet publish src/GHelper.App/GHelper.App.csproj -r linux-x64 -c Release -o artifacts/publish/linux-x64
# 2. packages -> artifacts/dist
packaging/linux/build-packages.sh --rid linux-x64 [--version 0.286.0] [--formats tar,deb,rpm]
```

| Output | Notes |
|---|---|
| `ghelper-<version>-<rid>.tar.gz` | every RID. `root/` is the final file tree; `install.sh` copies it to `/` (or `$DESTDIR`) and reloads udev, `uninstall.sh` removes it |
| `ghelper_<version>_<amd64\|arm64\|armhf>.deb` | glibc RIDs. Assembled with `ar`/`tar` by the script itself (no `dpkg-deb`, no fakeroot); check it with `dpkg-deb --info/--contents` |
| `ghelper-<version>-1.<x86_64\|aarch64\|armv7hl>.rpm` | glibc RIDs, needs `rpmbuild` (`rpm` package on Debian/Ubuntu); spec in `rpm/ghelper.spec`, it repackages the tar.gz |

Installed file tree (identical for all three formats):

| Path | Content |
|---|---|
| `/usr/lib/ghelper/ghelper` | the published single file (`GHelper` renamed, the Avalonia/Skia native libraries are extracted to `~/.net` on first start) |
| `/usr/bin/ghelper` | symlink to the above |
| `/usr/lib/ghelper/ghelper-root-launcher`, `ghelper-udev-perms` | polkit entry point and udev helper |
| `/usr/lib/udev/rules.d/99-ghelper.rules`, `/usr/lib/sysusers.d/ghelper.conf` | device access, `ghelper` group |
| `/usr/share/polkit-1/actions/org.ghelper.policy` | `pkexec` action |
| `/usr/share/applications/ghelper.desktop`, `/usr/share/icons/hicolor/256x256/apps/ghelper.png` | menu entry and icon |
| `/usr/lib/systemd/system/ghelper-charge.service`, `/usr/lib/systemd/user/ghelper.service` | units, shipped disabled |
| `/usr/share/doc/ghelper/{README.md,copyright}` | this file and the GPL-3.0 text |

Maintainer scripts / scriptlets (`deb/postinst|prerm|postrm`, `%post|%preun|%postun` in the spec, `install.sh`): create
the `ghelper` group (`systemd-sysusers`, else `groupadd`), `udevadm control --reload-rules && udevadm trigger`,
`systemctl daemon-reload`, refresh the icon cache; on removal disable `ghelper-charge.service` and reload udev. The
group and `~/.config/ghelper` are never removed.

Runtime dependencies of the self-contained build: glibc (or musl for the `linux-musl-*` archives), `libstdc++`,
`zlib`, fontconfig, X11 (`libX11`, `libICE`, `libSM`). ICU is needed for localisation; on a system without it start
with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` (the UI then stays English). Weak dependencies: `pkexec`/polkit
(elevation), `power-profiles-daemon`, `xdg-utils`.

Not packaged, on purpose:

* **AppImage / Flatpak / Snap**: the udev rules, the polkit action, the `ghelper` group and `/dev/uinput`,
  `/sys/class/*`, `/sys/kernel/debug` access are host integration that a sandbox or a read-only image cannot
  install or reach. A Flatpak would need `--device=all --filesystem=host-os` and still no udev rules. The tar.gz runs
  from any directory, which is the portable option.
* **Arch / Alpine packages (PKGBUILD, apk)**: use the tar.gz (`install.sh` works on busybox); musl archives exist for
  `linux-musl-x64` and `linux-musl-arm64`.
* **Trimming / NativeAOT**: not enabled, see `src/GHelper.App/GHelper.App.csproj`.

## Tiling window managers

Every G-Helper window has a fixed size (`WM_NORMAL_HINTS` min == max, not resizable / maximizable) and sets
`WM_CLASS` = `ghelper` (instance and class; XWayland included). Side windows are transient for the main window, so
most tilers float them on their own. If yours tiles them anyway, match the class:

```
# i3
for_window [class="ghelper"] floating enable
# sway (XWayland client)
for_window [class="ghelper"] floating enable
# Hyprland
windowrulev2 = float, class:^(ghelper)$
# bspwm
bspc rule -a ghelper state=floating
# awesome (rc.lua, awful.rules)
{ rule = { class = "ghelper" }, properties = { floating = true } },
```

The settings window positions itself next to the taskbar / panel; on a tiling WM the compositor decides instead, use
a rule such as `move` / `anchor` of your WM if you want a fixed place.

## What G-Helper reads and writes

Everything goes through mainline kernel interfaces. Nothing is verified on hardware yet, the kernel versions below come
from the kernel ABI documentation (`Documentation/ABI/testing/sysfs-platform-asus-wmi`, `sysfs-class-platform-profile`)
and from the driver sources.

| Feature | Interface (path below `/sys`) | Kernel |
|---|---|---|
| Performance mode (Silent / Balanced / Turbo) | `class/platform-profile/platform-profile-*` named `asus-wmi` (`profile`, `choices`); `firmware/acpi/platform_profile` before the class existed; `devices/platform/asus-nb-wmi/throttle_thermal_policy` (only with `CONFIG_ASUS_WMI_DEPRECATED_ATTRS`) | policy file 5.6, platform_profile class 6.14 |
| Power profile (OS side) | `power-profiles-daemon` (`powerprofilesctl`, DBus `net.hadess.PowerProfiles`), platform_profile as fallback | daemon 0.10+ |
| Fan RPM | `class/hwmon/hwmon*` named `asus` (`fan1_input`..`fan3_input`) | asus-wmi hwmon, 5.x |
| Fan curves (CPU / GPU / mid) | `class/hwmon/hwmon*` named `asus_custom_fan_curve` (`pwmN_auto_pointM_temp`/`_pwm`, `pwmN_enable`) | asus-wmi custom fan curve support (5.17 era; check `ls /sys/class/hwmon/*/name`) |
| PPT limits (SPL / sPPT / fPPT / APU / platform), NVIDIA dynamic boost and temperature target, TGP | `class/firmware-attributes/asus-armoury/attributes/<name>/current_value`; before 6.19 the deprecated `devices/platform/asus-nb-wmi/ppt_*`, `nv_*` | attributes 6.5, asus-armoury 6.19 |
| dGPU disable (Eco), eGPU, GPU MUX (Ultimate) | `asus-armoury/attributes/{dgpu_disable,egpu_enable,gpu_mux_mode}`; before 6.19 `asus-nb-wmi/{dgpu_disable,egpu_enable,gpu_mux_mode}` | dgpu_disable / egpu 5.17, gpu_mux_mode 6.1 |
| Panel overdrive, mini-LED, panel resolution mode, auto brightness | `asus-armoury/attributes/{panel_overdrive,mini_led_mode,panel_hd_mode,screen_auto_brightness}`; before 6.19 `asus-nb-wmi/{panel_od,mini_led_mode}` | panel_od 5.17, mini_led_mode 6.5 |
| APU memory carve-out, boot sound, MCU power saving | `asus-armoury/attributes/{apu_mem,boot_sound,mcu_powersave}` | 6.19 (boot_sound / mcu_powersave 6.10 as platform attributes) |
| Charge limit | `class/power_supply/BAT*/charge_control_end_threshold` (asus-wmi battery extension) | 5.4+ |
| Charger type (barrel / USB-C) | `asus-armoury/attributes/charge_mode`, before 6.19 `asus-nb-wmi/charge_mode` | 6.5 |
| Keyboard backlight | `class/leds/asus::kbd_backlight/brightness` | asus-wmi / hid-asus, long standing |
| TUF RGB modes and power states | `class/leds/asus::kbd_backlight/{kbd_rgb_mode,kbd_rgb_state}` | TUF series, recent 6.x |
| ScreenPad brightness | `class/backlight/asus_screenpad` | Zenbook Duo / Pro Duo, 6.x |
| Hotkeys (ROG key, Fn+F5, M-keys, Aura key) | evdev nodes "Asus WMI hotkeys" and "Asus Keyboard" (`/proc/bus/input/devices`), `MSC_SCAN` = ATK event id | asus-nb-wmi sparse keymap, hid-asus (vendor page 0xFF31) |
| Anything else (`PPT_EDC/TDC`, fan hysteresis, GPU display connect, ...) | `/sys/kernel/debug/asus-nb-wmi/{dev_id,ctrl_param,dsts,devs}` (needs root and debugfs) | any asus-wmi kernel |
| Ryzen TDP / curve optimizer / thermal limit | out-of-tree module `ryzen_smu` (`/sys/kernel/ryzen_smu_drv`) | module builds on 5.x - 6.x |
| Intel package power | `class/powercap/intel-rapl:0/energy_uj` (root, or the `msr` module) | 3.13+ |
| Screen brightness / refresh rate | `class/backlight/*`; `xrandr` (X11), `wlr-randr`, `kscreen-doctor`, GNOME Mutter DBus | desktop dependent |
| Key injection | `/dev/uinput` | 4.5+ (`UI_DEV_SETUP`) |
| Sleep / lock / lid events | systemd-logind via `gdbus monitor`, UPower | systemd 230+ |
| Microphone / output mute, audio visualiser | `wpctl` or `pactl`, `parec` | PipeWire / PulseAudio |

## Known interaction with other tools

* `asusd` / `supergfxd` (asusctl, supergfxctl) drive the same attributes. G-Helper reports them
  (`LinuxVendorServices.DetectConflicts`) and can stop them with `StopAsusServices` when it runs as root. Use one
  tool, not both.
* On several models the kernel refuses PPT writes with `EBUSY` until a custom fan curve is active
  (`asus-armoury` `requires_fan_curve`); a PPT change that fails on such a model needs a custom fan curve first.
* The kernel resets `charge_control_end_threshold` to 100 whenever the battery is (re)registered; install and enable
  `ghelper-charge.service`.
* asus-wmi consumes some hotkeys itself and never reports them to user space: Fn+F5 / fan key (it cycles the
  platform profile), keyboard backlight up/down/toggle and Fn lock. The desktop environment handles brightness,
  volume, touchpad and rfkill keys. G-Helper does not forward the latter to its hotkey handler (they would act
  twice); the ROG / M keys, Aura key, camera, mic mute and unknown ATK codes (reported as `KEY_UNKNOWN` with the code in
  `MSC_SCAN`) are forwarded.
