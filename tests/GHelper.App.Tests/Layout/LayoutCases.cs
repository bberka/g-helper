using Avalonia.Controls;
using GHelper.AnimeMatrix;
using GHelper.App.Services;
using GHelper.App.ViewModels;
using GHelper.App.Views;
using GHelper.App.Views.Extra;
using GHelper.App.Views.Fans;
using GHelper.App.Views.Headset;
using GHelper.App.Views.Keyboard;
using GHelper.App.Views.Mouse;
using GHelper.App.Views.Overlay;
using GHelper.App.Views.Updates;
using GHelper.App.Controls;
using GHelper.App.Tests.Peripherals;
using GHelper.Peripherals.Keyboard.Models;
using GHelper.Platform;
using Avalonia.Media;

namespace GHelper.App.Tests.Layout;

/// <summary>One window of the app in a representative, fully populated state (fake backends only).</summary>
internal sealed record LayoutCase(string Name, string? Registry, Func<Window> Create)
{
    public override string ToString() => Name;
}

/// <summary>Every window of the app (registry names: fans, extra, matrix, slash, updates, handheld, overlay, mouse, keyboard,
/// headset + the main window and the colour picker dialog) built against fake backends.</summary>
internal static class LayoutCases
{
    public static readonly string[] RegistryNames =
        { "fans", "extra", "matrix", "slash", "updates", "handheld", "overlay", "mouse", "keyboard", "headset" };

    public static IReadOnlyList<LayoutCase> All { get; } = Build();

    public static IEnumerable<object[]> Names => All.Select(c => new object[] { c.Name });

    public static LayoutCase Get(string name) => All.First(c => c.Name == name);

    private static void Env()
    {
        SideWindowEnv.Ensure();
        TestPlatform.Ensure();
    }

    private static List<LayoutCase> Build()
    {
        var list = new List<LayoutCase>
        {
            new("main", null, () =>
            {
                Env();
                return new MainWindow { DataContext = MainViewModel.CreateSample() };
            }),
            new("main-full", null, () =>
            {
                Env();
                var vm = MainViewModel.CreateSample();
                vm.PeripheralsVisible = true;
                foreach (var p in vm.Peripherals) p.IsVisible = true;
                vm.Peripherals[0].Text = "ROG Harpe Ace\n85%";
                vm.Peripherals[1].Text = "ROG Strix Scope";
                vm.Peripherals[2].Text = "ROG Delta";
                vm.RearSectionVisible = true;
                vm.RearModes.Add(new ChoiceItem(0, "Static"));
                vm.RearModes.Add(new ChoiceItem(1, "Breathe"));
                vm.SelectedRearMode = vm.RearModes[0];
                vm.RearSwatch = Colors.Orange;
                vm.Armoury.IsVisible = true;
                vm.EnergySaver.IsVisible = true;
                vm.Donate.Badge = 3;
                return new MainWindow { DataContext = vm };
            }),
            new("main-ally", null, () =>
            {
                Env();
                return new MainWindow { DataContext = MainViewModel.CreateSample(true) };
            }),
            new("fans-cpu", "fans", () => { Env(); return new FansWindow(0, SampleFansBackend.Create()); }),
            new("fans-gpu", "fans", () => { Env(); return new FansWindow(1, SampleFansBackend.Create(xgm: true)); }),
            new("fans-advanced", "fans", () => { Env(); return new FansWindow(2, SampleFansBackend.Create()); }),
            new("extra", "extra", () =>
            {
                Env();
                var b = SampleExtraBackend.Create();
                b.Caps = b.Caps with { Debug = true };
                b.Strs["m1"] = "mute";
                b.Strs["m2"] = "custom";
                b.Strs["m2_custom"] = "notepad.exe";
                return new ExtraWindow(0, b);
            }),
            new("extra-ally", "extra", () =>
            {
                Env();
                var b = SampleExtraBackend.Create();
                b.Caps = new ExtraCaps { IsAlly = true };
                b.Cores = null;
                b.Apu = null;
                return new ExtraWindow(0, b);
            }),
            new("matrix-picture", "matrix", () => Matrix(MatrixMode.Picture)),
            new("matrix-clock", "matrix", () => Matrix(MatrixMode.Clock)),
            new("matrix-audio", "matrix", () => Matrix(MatrixMode.Audio)),
            new("matrix-text", "matrix", () => Matrix(MatrixMode.Text)),
            new("slash", "slash", () =>
            {
                var (control, _, _) = SideWindowEnv.SlashControl();
                var window = new SlashWindow(control);
                window.Closed += (_, _) => control.Dispose();
                return window;
            }),
            new("handheld", "handheld", () => { Env(); return new HandheldWindow(new NoActions()); }),
            new("updates", "updates", () =>
            {
                Env();
                var window = new UpdatesWindow(
                    new UpdatesController((url, _) => Task.FromResult(url.Contains("GetPDBIOS") ? BiosJson : DriversJson), null),
                    () => new UpdatesTarget("309", "GA403UV", IsRog: true),
                    () => "NCNRCX012345678",
                    _ => { },
                    autoLoad: false);
                window.Height = 560;
                return window;
            }),
            new("overlay", "overlay", () =>
            {
                Env();
                var store = new MemoryOverlayStore();
                store.Set("overlay", 1);
                store.Set("overlay_mode", 2);
                store.Set("overlay_scale_percent", 125);
                store.Set("overlay_alpha", 180);
                return new OverlayConfigWindow(store, () => { }, () => { }, () => { });
            }),
            new("mouse", "mouse", () => { Env(); return new MouseWindow(new FakeMouse { SpeedShift = true }); }),
            new("mouse-minimal", "mouse", () =>
            {
                Env();
                return new MouseWindow(new FakeMouse
                {
                    Battery_ = false, Profiles = false, DpiColors = false, ChangeDpiCount = false, Rgb = false,
                    AutoPowerOff = false, LowBattery = false, AngleSnap = false, AngleTune = false, Acceleration_ = false,
                    Deceleration_ = false, LiftOff = false, Debounce_ = false, MotionSync_ = false, ZoneMode_ = false,
                    Bindings = false,
                });
            }),
            new("keyboard", "keyboard", () => { Env(); return new KeyboardWindow(new StrixScopeII { TestMode = true }); }),
            new("keyboard-azoth", "keyboard", () => { Env(); return new KeyboardWindow(new Azoth { TestMode = true }); }),
            new("headset", "headset", () => { Env(); return new HeadsetWindow(new FakeHeadset()); }),
            new("colorpicker", null, () => { Env(); return new ColorPickerWindow(Color.FromRgb(255, 64, 0), allowRandom: true); }),
        };
        return list;
    }

    private static Window Matrix(MatrixMode mode)
    {
        var (control, _, _) = SideWindowEnv.MatrixControl();
        AppConfig.Set("matrix_running", (int)mode);
        AppConfig.Set("matrix_text", "G-Helper");
        AppConfig.Set("matrix_text2", "Second line");
        MatrixWindow.FontDownloader = () => Task.FromResult(false);
        var window = new MatrixWindow(control);
        window.Closed += (_, _) =>
        {
            control.Dispose();
            foreach (var key in new[] { "matrix_running", "matrix_text", "matrix_text2" }) AppConfig.Remove(key);
        };
        return window;
    }

    private sealed class NoActions : IControllerActions
    {
        public void SetDeadzones() { }
        public void ApplyMode() { }
        public void DisableController(bool disabled) { }
        public void SetVibrationMode() { }
    }

    private const string BiosJson = """
    {"Result":{"Obj":[{"Name":"BIOS","Files":[
      {"Title":"ROG Zephyrus G14 BIOS","Version":"V312","DownloadUrl":{"Global":"https://dlcdnets.asus.com/pub/BIOS_312.zip"},"ReleaseDate":"2025/02/11","HardwareInfoList":[]},
      {"Title":"MCU Firmware Update","Version":"V1204","DownloadUrl":{"Global":"https://dlcdnets.asus.com/pub/MCU.zip"},"ReleaseDate":"2024/11/02","HardwareInfoList":[]}
    ]}]}}
    """;

    private const string DriversJson = """
    {"Result":{"Obj":[
      {"Name":"Audio","Files":[
        {"Title":"Realtek Audio Driver","Version":"V6.0.9500.1","DownloadUrl":{"Global":"https://dlcdnets.asus.com/pub/audio.zip"},"ReleaseDate":"2025/01/20","HardwareInfoList":[]}
      ]},
      {"Name":"Chipset","Files":[
        {"Title":"AMD Chipset Driver","Version":"V6.2.7.2300","DownloadUrl":{"Global":"https://dlcdnets.asus.com/pub/chipset.zip"},"ReleaseDate":"2024/12/05","HardwareInfoList":[]}
      ]},
      {"Name":"Utilities","Files":[
        {"Title":"ASUS Mystery Utility","Version":"V1.0.0.5","DownloadUrl":{"Global":"https://dlcdnets.asus.com/pub/util.zip"},"ReleaseDate":"2024/10/10","HardwareInfoList":[]}
      ]}
    ]}}
    """;
}
