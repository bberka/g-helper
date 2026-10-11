using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using static GHelper.Platform.Windows.Display.DisplayNative;

namespace GHelper.Platform.Windows.Display;

/// <summary>
/// Windows implementation of <see cref="IDisplayService"/>: CCD queries for the internal panel / external displays / HDR,
/// GDI refresh rate switching, WMI backlight, gamma ramp, ICC profile install, PnP touchscreen toggle, ELMB.
/// Lifted from legacy ScreenNative / ScreenCCD / ScreenBrightness / ScreenELMB / TouchscreenHelper.
/// </summary>
public sealed class WindowsDisplayService : IDisplayService
{
    public const string DefaultDevice = @"\\.\DISPLAY1";

    private readonly Func<IProcessService> process;
    private readonly Lazy<ElmbControl> elmb = new(() => new ElmbControl());

    public WindowsDisplayService(Func<IProcessService> process)
    {
        this.process = process;
    }

    // --- display configuration changes ---------------------------------------------------------

    private event Action? displaySettingsChanged;
    private int displayHooked;

    public event Action? DisplaySettingsChanged
    {
        add
        {
            displaySettingsChanged += value;
            HookDisplayEvents();
        }
        remove => displaySettingsChanged -= value;
    }

    private void HookDisplayEvents()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (Interlocked.Exchange(ref displayHooked, 1) == 1) return;

        try { Subscribe(); }
        catch (Exception ex) { Logger.WriteLine("Display events: " + ex.Message); }
    }

    [SupportedOSPlatform("windows")]
    private void Subscribe() => SystemEvents.DisplaySettingsChanged += (_, _) =>
    {
        try { displaySettingsChanged?.Invoke(); }
        catch (Exception ex) { Logger.WriteLine("Display settings handler: " + ex.Message); }
    };

    // --- internal / external displays -------------------------------------------------------

    public bool IsExternalDisplayConnected(bool log = false)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            string? internalName = AppConfig.GetString("internal_display");
            foreach (var device in GetAllTargets())
            {
                if (!DisplayLogic.IsInternal(device.outputTechnology, device.monitorFriendlyDeviceName, internalName))
                {
                    if (log) Logger.WriteLine("Found external screen: " + device.monitorFriendlyDeviceName + ":" + device.outputTechnology);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.ToString());
        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static (DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes, uint modeCount) QueryActivePaths()
    {
        int err = GetDisplayConfigBufferSizes(QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
        if (err != 0) throw new Win32Exception(err);

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
        err = QueryDisplayConfig(QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, nint.Zero);
        if (err != 0) throw new Win32Exception(err);

        return (paths, modes, modeCount);
    }

    private static DISPLAYCONFIG_TARGET_DEVICE_NAME NewTargetName(LUID adapter, uint id)
    {
        var name = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
        name.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_TARGET_NAME;
        name.header.size = (uint)Marshal.SizeOf(name);
        name.header.adapterId = adapter;
        name.header.id = id;
        return name;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<DISPLAYCONFIG_TARGET_DEVICE_NAME> GetAllTargets()
    {
        var (_, modes, modeCount) = QueryActivePaths();

        for (int i = 0; i < modeCount; i++)
        {
            if (modes[i].infoType != DISPLAYCONFIG_MODE_INFO_TYPE.TARGET) continue;

            var deviceName = NewTargetName(modes[i].adapterId, modes[i].id);
            int err = DisplayConfigGetDeviceInfo(ref deviceName);
            if (err == 0) yield return deviceName;
            else Logger.WriteLine("DisplayConfigGetDeviceInfo error: " + new Win32Exception(err).Message);
        }
    }

    public string? FindLaptopScreen(bool log = false)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            var (paths, _, _) = QueryActivePaths();
            string? internalName = AppConfig.GetString("internal_display");

            foreach (var path in paths)
            {
                var targetName = NewTargetName(path.targetInfo.adapterId, path.targetInfo.id);
                if (DisplayConfigGetDeviceInfo(ref targetName) != 0) continue;
                if (!DisplayLogic.IsInternal(targetName.outputTechnology, targetName.monitorFriendlyDeviceName, internalName)) continue;

                if (log) Logger.WriteLine(targetName.monitorDevicePath + " " + targetName.outputTechnology);
                AppConfig.Set("internal_display", targetName.monitorFriendlyDeviceName);

                // GDI device name straight from the source side of the path
                var sourceName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                sourceName.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_SOURCE_NAME;
                sourceName.header.size = (uint)Marshal.SizeOf(sourceName);
                sourceName.header.adapterId = path.sourceInfo.adapterId;
                sourceName.header.id = path.sourceInfo.id;

                if (DisplayConfigGetDeviceInfo(ref sourceName) == 0)
                    return DisplayLogic.ExtractDisplay(sourceName.viewGdiDeviceName);

                return DefaultDevice;
            }

            if (log) Logger.WriteLine("Internal screen off");
            return null;
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
            return null;
        }
    }

    // --- refresh rate -----------------------------------------------------------------------

    private static DEVMODE CreateDevmode()
    {
        var dm = new DEVMODE
        {
            dmDeviceName = new string(new char[32]),
            dmFormName = new string(new char[32]),
        };
        dm.dmSize = (short)Marshal.SizeOf(dm);
        return dm;
    }

    public int GetMaxRefreshRate(string? screen)
    {
        if (screen is null || !OperatingSystem.IsWindows()) return -1;

        var dm = CreateDevmode();
        int frequency = -1;
        int i = 0;
        while (EnumDisplaySettingsEx(screen, i, ref dm) != 0)
        {
            if (dm.dmDisplayFrequency > frequency) frequency = dm.dmDisplayFrequency;
            i++;
        }

        if (frequency > 0) AppConfig.Set("screen_max", frequency);
        else frequency = AppConfig.Get("screen_max");

        return frequency;
    }

    public int GetRefreshRate(string? screen)
    {
        if (screen is null || !OperatingSystem.IsWindows()) return -1;

        var dm = CreateDevmode();
        return EnumDisplaySettingsEx(screen, ENUM_CURRENT_SETTINGS, ref dm) != 0 ? dm.dmDisplayFrequency : -1;
    }

    public int SetRefreshRate(string screen, int frequency)
    {
        if (!OperatingSystem.IsWindows()) return -1;

        var dm = CreateDevmode();
        if (EnumDisplaySettingsEx(screen, ENUM_CURRENT_SETTINGS, ref dm) == 0) return -1;
        if (dm.dmDisplayFrequency == frequency) return 1; // nothing to change

        dm.dmDisplayFrequency = frequency;
        int result = ChangeDisplaySettingsEx(screen, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        Logger.WriteLine("Screen = " + frequency + "Hz : " + (result == 0 ? "OK" : result));
        return result == 0 ? 1 : result;
    }

    // --- backlight --------------------------------------------------------------------------

    public int GetBrightness()
    {
        if (!OperatingSystem.IsWindows()) return -1;

        try
        {
            return ReadBrightness();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Brightness: " + ex.Message);
            return -1;
        }
    }

    [SupportedOSPlatform("windows")]
    private static int ReadBrightness()
    {
        using var mclass = new ManagementClass("WmiMonitorBrightness") { Scope = new ManagementScope(@"\\.\root\wmi") };
        using var instances = mclass.GetInstances();
        foreach (ManagementObject instance in instances)
        {
            using (instance) return (byte)instance.GetPropertyValue("CurrentBrightness");
        }

        return -1;
    }

    public void SetBrightness(int brightness)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            WriteBrightness(brightness);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Brightness: " + ex.Message);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void WriteBrightness(int brightness)
    {
        using var mclass = new ManagementClass("WmiMonitorBrightnessMethods") { Scope = new ManagementScope(@"\\.\root\wmi") };
        using var instances = mclass.GetInstances();
        var args = new object[] { 1, brightness };
        foreach (ManagementObject instance in instances)
        {
            using (instance) instance.InvokeMethod("WmiSetBrightness", args);
        }
    }

    public int AdjustBrightness(int delta)
    {
        int brightness = GetBrightness();
        if (brightness < 0) return -1;

        brightness = Math.Min(100, Math.Max(0, brightness + delta));
        SetBrightness(brightness);
        return brightness;
    }

    // --- HDR --------------------------------------------------------------------------------

    public bool GetHdrStatus(out bool acm, bool log = true)
    {
        acm = false;
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            var (paths, _, _) = QueryActivePaths();
            string? internalName = AppConfig.GetString("internal_display");

            foreach (var path in paths)
            {
                var info = NewTargetName(path.targetInfo.adapterId, path.targetInfo.id);
                int err = DisplayConfigGetDeviceInfo(ref info);
                if (err != 0) continue;

                if (!DisplayLogic.IsInternal(info.outputTechnology, info.monitorFriendlyDeviceName, internalName)) continue;

                var colorInfo = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
                colorInfo.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_ADVANCED_COLOR_INFO;
                colorInfo.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>();
                colorInfo.header.adapterId = path.targetInfo.adapterId;
                colorInfo.header.id = path.targetInfo.id;
                err = DisplayConfigGetDeviceInfo(ref colorInfo);
                if (err != 0)
                {
                    Logger.WriteLine("HDR Detection Error: " + new Win32Exception(err).Message);
                    return false;
                }

                var (hdr, acmState) = DisplayLogic.DecodeAdvancedColor(colorInfo.value, colorInfo.bitsPerColorChannel);
                acm = acmState;
                if (log)
                    Logger.WriteLine(info.monitorFriendlyDeviceName + " HDR: " + hdr + " ACM: " + acm + " | " + colorInfo.advancedColorEnabled
                        + " " + colorInfo.bitsPerColorChannel + " " + colorInfo.value + " " + colorInfo.wideColorEnforced);
                return hdr;
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine("HDR Detection Error: " + ex.Message);
        }

        return false;
    }

    // --- gamma / colour profiles --------------------------------------------------------------

    public bool SupportsGammaRamp => OperatingSystem.IsWindows();

    public void SetGammaRamp(string? screen, ushort[] red, ushort[] green, ushort[] blue)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (red.Length != 256 || green.Length != 256 || blue.Length != 256) return;

        var ramp = new ushort[768];
        red.CopyTo(ramp, 0);
        green.CopyTo(ramp, 256);
        blue.CopyTo(ramp, 512);

        IntPtr dc = CreateDC("DISPLAY", screen ?? DefaultDevice, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return;

        try
        {
            if (!SetDeviceGammaRamp(dc, ramp)) Logger.WriteLine("SetDeviceGammaRamp failed: " + Marshal.GetLastWin32Error());
        }
        finally
        {
            DeleteDC(dc);
        }
    }

    private static string GetColorDirectory()
    {
        try
        {
            var buffer = new char[260];
            uint size = (uint)buffer.Length * 2;
            if (MscmsGetColorDirectory(null, buffer, ref size))
                return new string(buffer).TrimEnd('\0');
        }
        catch { }

        return Path.Combine(Environment.SystemDirectory, "spool", "drivers", "color");
    }

    public bool IsColorProfileInstalled(string profileName)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(profileName)) return false;
        return File.Exists(Path.Combine(GetColorDirectory(), Path.GetFileName(profileName)));
    }

    public Task InstallColorProfile(string profileName, byte[] iccData)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(profileName)) return Task.CompletedTask;

        return Task.Run(() =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "GHelper-icc-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, Path.GetFileName(profileName));
                File.WriteAllBytes(file, iccData);

                if (!MscmsInstallColorProfile(null, file))
                    Logger.WriteLine($"InstallColorProfile {profileName} failed: " + Marshal.GetLastWin32Error());
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Can't install colour profile {profileName}: {ex.Message}");
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch { }
            }
        });
    }

    // --- touchscreen ------------------------------------------------------------------------

    public bool? GetTouchscreenStatus()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            process().RunAsAdmin();
            string output = process().RunCommand("powershell", "(Get-PnpDevice -FriendlyName '*touch*screen*').Status");
            if (string.IsNullOrWhiteSpace(output)) return null;
            return output.Contains("OK");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Can't get touchscreen status: {ex.Message}");
            return null;
        }
    }

    public void SetTouchscreenStatus(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            process().RunAsAdmin();
            process().RunCommand("powershell",
                (enabled ? "Enable-PnpDevice" : "Disable-PnpDevice") + " -InstanceId (Get-PnpDevice -FriendlyName '*touch*screen*').InstanceId -Confirm:$false");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Can't toggle touchscreen: {ex.Message}");
        }
    }

    // --- ELMB -------------------------------------------------------------------------------

    public int GetElmbStatus()
    {
        if (!OperatingSystem.IsWindows()) return -1;
        try { return elmb.Value.Get(); }
        catch (Exception ex) { Logger.WriteLine("ELMB: " + ex.Message); return -1; }
    }

    public void SetElmbStatus(int status)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { elmb.Value.Set(status); }
        catch (Exception ex) { Logger.WriteLine("ELMB: " + ex.Message); }
    }
}
