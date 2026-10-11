using GHelper.Platform;

namespace GHelper.App.Tests.Peripherals;

/// <summary>
/// The peripheral classes of Core read / write AppConfig and log through the platform services. Register a platform whose
/// config directory is a throw-away temp folder (nothing else of the platform is needed by the peripheral code paths).
/// </summary>
internal static class TestPlatform
{
    private static readonly object Gate = new();

    public static void Ensure()
    {
        lock (Gate)
        {
            if (PlatformServices.IsRegistered) return;
            PlatformServices.Register(new MinimalPlatform());
        }
    }

    private sealed class MinimalPlatform : IPlatform
    {
        public PlatformKind Kind => PlatformKind.Linux;
        public ISystemInfo System { get; } = new TempSystemInfo();
        public IAsusBackend Asus => null!;
        public IPowerService Power => null!;
        public IDisplayService Display => null!;
        public IInputService Input => null!;
        public IStartupService Startup => null!;
        public IAudioService Audio => null!;
        public IProcessService Process => null!;
        public IVendorServices Vendor => null!;
        public ICpuTuning CpuTuning => null!;
        public IGpuProvider Gpu => null!;
        public ISystemSensors Sensors => null!;
        public IFpsMonitorFactory Fps => null!;
    }

    private sealed class TempSystemInfo : ISystemInfo
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "ghelper-peripheral-tests-" + Guid.NewGuid().ToString("N"));

        public PlatformKind Kind => PlatformKind.Linux;
        public string OsDescription => "test";
        public string ConfigDirectory => _dir;
        public string SharedConfigDirectory => _dir + "-shared";
        public string GetModel() => "Test";
        public (string Bios, string ModelShort) GetBiosAndModel() => ("", "");
        public string GetSerialNumber() => "";
        public string? GetCpuName() => null;
        public bool IsElevated => false;
        public bool IsRunningAsSystem => false;
        public bool? IsDarkTheme() => null;
        public bool IsDesktop => false;
        public bool? IsOledPanel() => null;
    }
}
