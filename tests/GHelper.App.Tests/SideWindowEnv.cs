using System.Reflection;
using System.Runtime.CompilerServices;
using GHelper.AnimeMatrix;
using GHelper.AnimeMatrix.Communication.Platform;
using GHelper.Platform;

namespace GHelper.App.Tests;

/// <summary>Records every feature report a matrix / slash device sends, answers reads from a callback.</summary>
internal sealed class RecordingUsb : UsbProvider
{
    private readonly object _lock = new();
    private readonly List<byte[]> _sets = new();

    public RecordingUsb() : base(0x0B05, 0x193B)
    {
    }

    public Func<byte[], byte[]>? OnGet { get; set; }

    public int SetCount
    {
        get { lock (_lock) return _sets.Count; }
    }

    public List<byte[]> Sets
    {
        get { lock (_lock) return _sets.ToList(); }
    }

    public override void Set(byte[] data)
    {
        lock (_lock) _sets.Add((byte[])data.Clone());
    }

    public override byte[] Get(byte[] data) => OnGet is null ? (byte[])data.Clone() : OnGet((byte[])data.Clone());

    public override void Read(byte[] data) { }

    public override void Write(byte[] data) { }

    public override void Dispose() { }
}

/// <summary>
/// Core's AppConfig / Logger need registered platform services. The window tests only care about the config round trip
/// and the USB reports, so every service is a do-nothing proxy; config lives in a throw away directory.
/// </summary>
internal static class SideWindowEnv
{
    public static readonly string ConfigDir;

    static SideWindowEnv()
    {
        ConfigDir = Path.Combine(Path.GetTempPath(), "ghelper-app-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ConfigDir);

        PlatformServices.Register(StubProxy.CreatePlatform(ConfigDir));
    }

    /// <summary>Forces the static constructor (platform registration) to run.</summary>
    public static void Ensure() => RuntimeHelpers.RunClassConstructor(typeof(SideWindowEnv).TypeHandle);

    public static (AniMatrixControl Control, RecordingUsb Usb, AnimeMatrixDevice Device) MatrixControl(AnimeType model = AnimeType.GA402)
    {
        Ensure();
        // not gated, no hardware: plain defaults for the keys the editor reads
        AppConfig.Set("matrix_brightness", 3);
        AppConfig.Set("matrix_auto", 0);
        AppConfig.Set("matrix_lid", 0);

        var usb = new RecordingUsb();
        var control = new AniMatrixControl();
        var device = new AnimeMatrixDevice(model, usb);
        control.deviceMatrix = device;
        return (control, usb, device);
    }

    public static (AniMatrixControl Control, RecordingUsb Usb, SlashDevice Device) SlashControl(bool longModel = false)
    {
        Ensure();
        AppConfig.Set("matrix_brightness", 3);

        var usb = new RecordingUsb();
        var control = new AniMatrixControl();
        var device = new SlashDevice(usb, longModel);
        control.deviceSlash = device;
        return (control, usb, device);
    }

    public class StubProxy : DispatchProxy
    {
        private static readonly MethodInfo CreateMethod =
            typeof(DispatchProxy).GetMethods().First(m => m.Name == nameof(DispatchProxy.Create) && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);

        private readonly Dictionary<string, object?> _values = new();

        public static IPlatform CreatePlatform(string configDir)
        {
            var platform = Create<IPlatform>();
            ((StubProxy)(object)platform)._values["get_Kind"] = PlatformKind.Linux;
            ((StubProxy)(object)platform)._values["get_System"] = CreateSystem(configDir);
            return platform;
        }

        private static ISystemInfo CreateSystem(string configDir)
        {
            var system = Create<ISystemInfo>();
            var values = ((StubProxy)(object)system)._values;
            values["get_ConfigDirectory"] = configDir;
            values["get_SharedConfigDirectory"] = Path.Combine(configDir, "shared");
            values["get_Kind"] = PlatformKind.Linux;
            values["GetModel"] = "TEST MODEL";
            values["GetBiosAndModel"] = ("TEST.1", "TEST");
            return system;
        }

        private static T Create<T>() where T : class
            => DispatchProxy.Create<T, StubProxy>();

        private static object CreateOf(Type serviceInterface)
            => CreateMethod.MakeGenericMethod(serviceInterface, typeof(StubProxy)).Invoke(null, null)!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;
            if (_values.TryGetValue(targetMethod.Name, out var value)) return value;

            var type = targetMethod.ReturnType;
            if (type == typeof(void)) return null;

            // sub services of IPlatform
            if (type.IsInterface && type.Namespace == typeof(IPlatform).Namespace)
            {
                var service = CreateOf(type);
                _values[targetMethod.Name] = service;
                return service;
            }

            if (type == typeof(string)) return "";
            if (type == typeof(Task)) return Task.CompletedTask;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var inner = type.GetGenericArguments()[0];
                object? def = inner.IsValueType ? Activator.CreateInstance(inner) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, new[] { def });
            }

            if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 0);
            if (type.IsValueType) return Activator.CreateInstance(type);
            return null;
        }
    }
}
