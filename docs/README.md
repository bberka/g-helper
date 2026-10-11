# G-Helper - feature and usage reference

This is the feature reference of the cross-platform fork of [seerge/g-helper](https://github.com/seerge/g-helper)
(Windows and Linux, Avalonia UI). Installation, supported platforms, Linux requirements and build instructions are in
the [main README](../README.md); the Linux kernel interfaces behind each feature are listed in
[packaging/linux/README.md](../packaging/linux/README.md).

> [!NOTE]
> The feature list below describes the original Windows application, which the port follows. On Linux a feature is
> available only when the kernel driver exposes it, and the Linux backend has not been verified on real hardware yet.
> Windows-only implementations: NVAPI (NVIDIA overclocking) and AMD ADL (x64), PawnIO based Ryzen / Intel tuning
> (x64; Linux uses `ryzen_smu` / `msr`) and the ETW based FPS counter of the overlay.

Download: [releases](https://github.com/bberka/g-helper/releases). Wiki pages of the original project (Windows
behaviour): [FAQ](https://github.com/seerge/g-helper/wiki/FAQ),
[Setup and Requirements](https://github.com/seerge/g-helper/wiki/Requirements),
[Troubleshooting](https://github.com/seerge/g-helper/wiki/Troubleshooting),
[Power User Settings](https://github.com/seerge/g-helper/wiki/Power-user-settings).

## :gift: Advantages 

1. Seamless and automatic GPU switching
2. All performance modes can be fully customized with power limits and fan curves
3. Lightweight: a single self-contained file per platform, no installer on Windows. 
4. Simple and clean native UI with easy access to all settings
5. FN-Lock and custom hotkeys

<img width="1960" alt="G-Helper Screenshot" src="https://github.com/user-attachments/assets/9376fe90-fbb6-420a-abbd-0186189665e1" />

### :zap: Features

1. Performance modes: Silent - Balanced - Turbo (built-in, with default fan curves)
2. GPU modes: Eco - Standard - Ultimate - Optimized
3. Screen refresh rate control with display overdrive
4. Custom fan curve editor, power limits and turbo boost selection for every performance mode
5. Anime Matrix or Slash Lighting control including animated GIFs, clock and Audio visualizer
6. RGB Backlight animation modes and colors 
7. Hotkey handling
8. Monitor CPU and GPU temperature, fan speeds and battery status
9. Battery charge limit to preserve battery health
10. NVidia GPU overclocking and undervolting
11. XG Mobile Control
12. AMD CPU Undervolting and temp limits
13. BIOS and Driver Updates
14. Asus Mice controls
15. Mini-led multi-zone switch
16. Flicker-free dimming and Visual Modes

### :gear: Automation
- Performance Mode switching when on battery or plugged in
- Optimized GPU mode - disables dGPU on battery and enables when plugged in
- Auto Screen refresh rate (60Hz on battery and max Hz when plugged)
- Keyboard backlight timeout on battery or when plugged in

### :rocket: Performance Modes
>[!NOTE]
>All Modes are **baked into BIOS** along with default fan curves and power limits. And and they are the **same thing** as in the Armoury Crate.

Each default BIOS mode is paired with matching [Windows Power Mode](https://support.microsoft.com/en-us/windows/change-the-power-mode-for-your-windows-pc-c2aff038-22c9-f46d-5ca0-78696fdf2de8). You can adjust this setting under ``Fans + Power``

1. `Silent` in BIOS + `Best power efficiency` power mode
2. `Balanced` (Performance in AC) in BIOS  + `Balanced` power mode
3. `Turbo` in BIOS + `Best performance` power mode

<img width="2330" alt="Performance Modes" src="https://github.com/user-attachments/assets/254d33eb-2af1-4715-a097-ed8678c7e9db" />

### :video_game: GPU Tweaking

In each mode dedicated GPU can be tweaked to specific needs, including Core and Memory Clock offsets, GPU power, Dynamic Boost, Temperature Limit etc
<img width="2354" alt="GPU Tweaking" src="https://github.com/user-attachments/assets/48ce19a6-d149-40ed-9145-23f974c513f7" />

### 💻 GPU Modes

1. **Eco** : only low power integrated GPU enabled, iGPU drives built in display
2. **Standard** (MS Hybrid) : iGPU and dGPU enabled, iGPU drives built in display
3. **Ultimate**: iGPU and dGPU enabled, but dGPU drives built in display (supported on 2022+ models)
4. **Optimized**: disables dGPU on battery (Eco) and enables when plugged in (Standard)

<img width="1134" alt="GPU Modes" src="https://raw.githubusercontent.com/seerge/g-helper/main/docs/gpu-modes-en.svg" />

### 💿Driver Updates
The app includes an automatic BIOS and driver update checker that pulls directly from the official Asus website for your specific model, highlighting new downloads as they become available. All links in the `Updates` section point to official Asus downloads.
<img width="2302" alt="Driver Updates" src="https://github.com/user-attachments/assets/303dfce9-fbbd-4d15-b6d7-f21e7c2c59a4" />

### 🎮 Metrics Overlay
A built-in in-game overlay (OSD) that shows live **FPS, CPU / GPU temperatures, usage and power** right on top of your game - no extra tools needed. Works in DX10+ games. May not be visible over old titles that use exclusive full-screen mode, so set the game to windowed / borderless full-screen mode.

- ``Ctrl + Shift + Alt + O`` - Toggle in-game overlay ON / OFF
- ``Ctrl + Shift + Alt + Mouse Drag`` - Move overlay
- ``Ctrl + Shift + Alt + Mouse Click`` - Switch mode (Light / Default / Full / Complete)
- ``Ctrl + Shift + Alt + Wheel`` - Resize overlay
- ``Ctrl + Shift + Alt + Wheel Click`` - Reset overlay size

<img width="1690" alt="Metrics Overlay" src="https://github.com/user-attachments/assets/0752a704-e9a7-4e27-8587-39967f625fae" />
<img width="3840" alt="Metrics Overlay in a game" src="https://github.com/user-attachments/assets/8b063f80-d508-41e0-9978-154bc936d451" />

### :mouse: Asus Mouse and other peripherals support

<details>
<summary><a href="https://github.com/seerge/g-helper/discussions/900">Currently supported models</a> (click to expand)</summary>

- ROG Chakram
- ROG Chakram X
- ROG Chakram Core
- ROG Gladius II and Gladius II Origin (+ PNK LTD / COD editions)
- ROG Gladius II Wireless
- ROG Gladius III
- ROG Gladius III Wireless
- ROG Gladius III Aimpoint Wireless (+ EVA-02 Edition)
- ROG Harpe Ace Extreme
- ROG Harpe Ace Aim Lab Edition
- ROG Harpe Ace Mini
- ROG Harpe II Ace
- ROG Harpe II Extreme Edition 20
- ROG Keris and Keris Wireless (+ EVA Edition)
- ROG Keris Wireless Aimpoint
- ROG Keris II Ace
- ROG Keris II Origin (+ KJP edition)
- ROG Pugio
- ROG Pugio II
- ROG Spatha X
- ROG Strix Carry
- ROG Strix Evolve
- ROG Strix Impact
- ROG Strix Impact II (+ Electro Punk / Moonlight White editions)
- ROG Strix Impact II Wireless
- ROG Strix Impact III
- ROG Strix Impact III Wireless
- TUF Gaming M3
- TUF Gaming M3 Gen II
- TUF Gaming M4 Wireless
- TUF Gaming M4 AIR
- TUF Gaming M5
- TUF Gaming Mini Miku Edition
- TX Gaming Mouse
- TX Gaming Mouse Mini
- ASUS MD200

</details>

Huge thanks to [@IceStormNG](https://github.com/IceStormNG) 👑 for contribution and research (!).

<img width="2448" alt="Mouse and other peripherals" src="https://github.com/user-attachments/assets/fe2a766b-f514-42e9-8dff-4bcc915364d4" />

### :keyboard: Asus Keyboards support

<details>
<summary><a href="https://github.com/seerge/g-helper/discussions/5710">Currently supported models</a> (click to expand)</summary>

- ASUS TX 98
- ROG Azoth
- ROG Azoth Extreme
- ROG Azoth Extreme SE
- ROG Azoth X
- ROG Claymore II
- ROG Falchion
- ROG Falchion Ace
- ROG Falchion Ace HFX
- ROG Falchion RX
- ROG Falchion RX Low Profile
- ROG Strix Flare
- ROG Strix Flare COD
- ROG Strix Flare II
- ROG Strix Flare II Animate
- ROG Strix Flare PNK LTD
- ROG Strix Scope II
- ROG Strix Scope II RX
- ROG Strix Scope II 96
- ROG Strix Scope II 96 RX
- ROG Strix Scope RX
- ROG Strix Scope RX EVA Edition
- ROG Strix Scope RX EVA-02 Edition
- ROG Strix Scope RX TKL
- TUF Gaming K1
- TUF Gaming K3
- TUF Gaming K3 Gen II
- TUF Gaming K3 Gen II Miku Edition

</details>

<img width="2254" alt="Keyboards" src="https://github.com/user-attachments/assets/9da4116f-7fb1-4dae-ac3e-25f0f1266a74" />

### :headphones: Asus Headsets support

<details>
<summary><a href="https://github.com/seerge/g-helper/discussions/5964">Currently supported models</a> (click to expand)</summary>

- ROG Cetra RGB
- ROG Cetra True Wireless SpeedNova
- ROG Clavis
- ROG Delta II
- ROG Delta II KJP
- ROG Pelta

</details>

Battery level, lighting, equalizer presets, sidetone, microphone and AI noise cancellation, ANC and power settings - depending on the model.

<img width="1786" alt="Headsets" src="https://github.com/user-attachments/assets/42ddd286-ed4f-4573-953c-e41b7fc3602f" />

### ⌨️ Keybindings

- ``Fn + F5 / Fn + Shift + F5`` - Toggle Performance Modes forwards / backwards
- ``Ctrl + Shift + F5 / Ctrl + Shift + Alt + F5`` - Toggle Performance Modes forwards / backwards
- ``Ctrl + Shift + F12`` - Open G-Helper window
- ``Ctrl + M1 / M2`` - Screen brightness Down / Up
- ``Shift + M1 / M2`` - Backlight brightness Down / Up
- ``Fn + V`` - Visual Modes
- ``Fn + C`` / ``Fn + Esc`` - Fn-Lock
- ``Fn + Ctrl +  F7 / F8`` / ``Ctrl + Shift + Alt +  F7 / F8`` - Flicker-free dimming Down / Up
- ``Fn + Shift + F7 / F8`` - Matrix / Slash Lighting brightness Down / Up
- ``Fn + Shift + F7 / F8`` / ``Ctrl + Shift + Alt +  F7 / F8`` - Screenpad brightness Down / Up
- ``Ctrl + Shift + F20`` - Mute Microphone
- ``Ctrl + Shift + Alt + F13`` - Toggle Display Refresh Rate
- ``Ctrl + Shift + Alt + F14`` - Eco GPU Mode
- ``Ctrl + Shift + Alt + F15`` - Standard GPU Mode
- ``Ctrl + Shift + Alt + F16`` - Silent
- ``Ctrl + Shift + Alt + F17`` - Balanced
- ``Ctrl + Shift + Alt + F18`` - Turbo
- ``Ctrl + Shift + Alt + F19`` - Custom 1 (if exists)
- ``Ctrl + Shift + Alt + F20`` - Custom 2 (if exists)
- ``Ctrl + Shift + Alt + F21`` - Toggle XG Mobile
- [Custom keybindings / hotkeys](https://github.com/seerge/g-helper/wiki/Power-user-settings#custom-hotkey-actions)

### 🎮ROG Ally Bindings
- ``M + DPad Left / Right`` - Display Brightness
- ``M + DPad Up`` - Touch keyboard
- ``M + DPad Down`` - Show desktop
- ``M + Y`` - Toggle AMD overay
- ``M + X`` - Screenshot
- ``M + Right Stick Click`` - Controller Mode

------------------

> [!NOTE]
> ### 🔖 Important Notice
> G-Helper is **NOT** an operating system, firmware, or driver. It **DOES NOT** "run" your hardware in real-time anyhow.
> 
> It's an app that lets you select one of the predefined operating modes created by manufacturer (and stored in BIOS) and optionally(!) set some settings that already exist on your device same as Armoury Crate can. It does it by using the Asus System Control Interface "driver" that Armoury uses for it.
> 
> If you use equivalent mode/settings as in Armoury Crate - the performance or the behavior of your device won't be different.
> 
> The role of G-Helper for your laptop is similar to the role of a remote control for your TV.

### Libraries and projects used
- [Linux Kernel](https://github.com/torvalds/linux/blob/master/include/linux/platform_data/x86/asus-wmi.h) for some basic endpoints in ASUS ACPI/WMI interface
- [NvAPIWrapper](https://github.com/falahati/NvAPIWrapper) for accessing Nvidia API
- [Starlight](https://github.com/vddCore/Starlight) for anime matrix communication protocol
- [UXTU](https://github.com/JamesCJ60/Universal-x86-Tuning-Utility) for undervolting endpoints using Ryzen SMU
- [PawnIO](https://github.com/namazso/PawnIO) for access to RyzenSMU
- [AsusCtl](https://gitlab.com/asus-linux/asusctl) for inspiration and some reverse engineering
- [Avalonia](https://avaloniaui.net/) and [SkiaSharp](https://github.com/mono/SkiaSharp) for the cross-platform UI
- [HidSharp](https://github.com/IntergatedCircuits/HidSharpCore) for HID access (hidraw on Linux)
- [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) for some RGB protocol details for external keyboards support

### Code Signing Policy
Releases of this fork are not code signed. Releases of the original project were signed with a certificate provided by
[SignPath Foundation](https://signpath.org/).

### Privacy Policy
This program will not transfer any information to other networked systems

### License
GPL-3.0, see [LICENSE](../LICENSE). Original work by [Serge (seerge)](https://github.com/seerge/g-helper).

### Disclaimers
"Asus", "ROG", "TUF", and "Armoury Crate" are trademarked by and belong to AsusTek Computer, Inc. I make no claims to these or any assets belonging to AsusTek Computer and use them purely for informational purposes only.

THE SOFTWARE IS PROVIDED “AS IS” AND WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. MISUSE OF THIS SOFTWARE COULD CAUSE SYSTEM INSTABILITY OR MALFUNCTION.
