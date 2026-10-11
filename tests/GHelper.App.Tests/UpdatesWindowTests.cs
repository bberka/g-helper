using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GHelper;
using GHelper.App.Theme;
using GHelper.App.Views.Updates;
using GHelper.Platform;
using Xunit;

namespace GHelper.App.Tests;

/// <summary>BIOS / driver updates window driven by a fake ASUS API and a fake driver inventory.</summary>
public class UpdatesWindowTests
{
    private const string BiosJson = """
    {"Result":{"Obj":[{"Name":"BIOS","Files":[
      {"Title":"ROG Zephyrus G14 BIOS","Version":"V312","DownloadUrl":{"Global":"https://dlcdnets.asus.com/pub/BIOS_312.zip"},"ReleaseDate":"2025/02/11","HardwareInfoList":[]},
      {"Title":"MCU Firmware Update","Version":"V1204","DownloadUrl":{"Global":"https://dlcdnets.asus.com/pub/MCU.zip"},"ReleaseDate":"2024/11/02","HardwareInfoList":[]}
    ]}]}}
    """;

    private const string DriversJson = """
    {"Result":{"Obj":[
      {"Name":"Audio","Files":[
        {"Title":"Realtek Audio Driver","Version":"V6.0.9500.1","DownloadUrl":{"Global":"https://dlcdnets.asus.com/pub/audio.zip"},"ReleaseDate":"2025/01/20","HardwareInfoList":[{"hardwareid":"HDAUDIO\\FUNC_01&VEN_10EC&DEV_0245&REV_1000"}]}
      ]},
      {"Name":"Chipset","Files":[
        {"Title":"AMD Chipset Driver","Version":"V6.2.7.2300","DownloadUrl":{"Global":"https://dlcdnets.asus.com/pub/chipset.zip"},"ReleaseDate":"2024/12/05","HardwareInfoList":[{"hardwareid":"PCI\\VEN_1022&DEV_14B9&REV_00"}]}
      ]},
      {"Name":"Utilities","Files":[
        {"Title":"ASUS Mystery Utility","Version":"V1.0.0.5","DownloadUrl":{"Global":"https://dlcdnets.asus.com/pub/util.zip"},"ReleaseDate":"2024/10/10","HardwareInfoList":[]},
        {"Title":"MyASUS","Version":"V3.0","DownloadUrl":{"Global":"https://dlcdnets.asus.com/pub/myasus.zip"},"ReleaseDate":"2024/10/10","HardwareInfoList":[]}
      ]}
    ]}}
    """;

    private sealed class FakeInventory : IDriverInventory
    {
        public List<InstalledDriver> BuildInventory() => new()
        {
            new InstalledDriver(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0245", "6.0.9400.1", false, "Realtek Audio"),
            new InstalledDriver(@"PCI\VEN_1022&DEV_14B9", "6.2.7.2300", false, "AMD Chipset"),
        };

        public string? GetMcuFirmwareVersion() => "1200";
        public string? GetFirmwareVersion(string title) => null;
        public Dictionary<string, string> BuildStagedVersions(HashSet<string> hardwareIds, CancellationToken token) => new();
        public HashSet<string> GetInstalledStorePackages() => new();
    }

    private static Task<string> FakeDownload(string url, CancellationToken token) =>
        Task.FromResult(url.Contains("GetPDBIOS") ? BiosJson : DriversJson);

    private static UpdatesWindow Create(List<string> opened, bool inventory = true) => new(
        new UpdatesController(FakeDownload, inventory ? new FakeInventory() : null),
        () => new UpdatesTarget("309", "GA403UV", IsRog: true),
        () => "NCNRCX012345678",
        opened.Add,
        autoLoad: false);

    private static void Pump(UpdatesWindow window)
    {
        for (int i = 0; i < 300 && !window.LoadTask.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.LoadTask.IsCompleted, "updates did not finish loading");
    }

    private static IEnumerable<T> All<T>(Visual root) where T : class =>
        root.GetVisualDescendants().OfType<T>();

    [Fact]
    public void Target_builds_the_asus_urls()
    {
        var t = new UpdatesTarget("309", "GA403UV", IsRog: true);
        Assert.Equal("https://rog.asus.com/support/webapi/product/GetPDBIOS?website=global&model=GA403UV&cpu=GA403UV&systemCode=rog", t.BiosUrl);
        Assert.Equal("https://rog.asus.com/support/webapi/product/GetPDDrivers?website=global&model=GA403UV&cpu=GA403UV&osid=52&systemCode=rog", t.DriversUrl);

        var plain = new UpdatesTarget("1", "X1504VA", IsRog: false);
        Assert.DoesNotContain("systemCode", plain.BiosUrl);
        Assert.DoesNotContain("systemCode", plain.DriversUrl);
    }

    [AvaloniaFact]
    public void Window_lists_updates_and_marks_new_ones()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var opened = new List<string>();
        var window = Create(opened);
        window.Show();
        window.LoadUpdates(force: true);
        Pump(window);

        var links = All<UpdatesWindow.LinkLabel>(window).ToList();
        Assert.Equal(5, links.Count); // BIOS, MCU, audio, chipset, utility (MyASUS is skipped)

        var texts = All<TextBlock>(window).Select(t => t.Text).ToList();
        Assert.Contains("ROG Zephyrus G14 BIOS", texts);
        Assert.Contains("2025/02/11", texts);
        Assert.Contains("NCNRCX012345678", texts);
        Assert.DoesNotContain("MyASUS", texts);

        // BIOS 312 > installed 309, MCU 1204 > 1200, audio 6.0.9500.1 > 6.0.9400.1: three new updates
        Assert.Contains(GHelper.Properties.Strings.NewUpdates + ": 3", texts);
        Assert.Equal(3, texts.Count(t => t == UpdatesWindow.SymbolNew));
        Assert.Equal(1, texts.Count(t => t == UpdatesWindow.SymbolUpdated)); // chipset is current

        var audio = links.Single(l => l.Url.EndsWith("audio.zip"));
        Assert.Equal(AppColors.Turbo, ((ISolidColorBrush)audio.Foreground!).Color);
        Assert.Equal(FontWeight.Bold, audio.FontWeight);

        var utility = links.Single(l => l.Url.EndsWith("util.zip"));
        Assert.Equal(Colors.Gray, ((ISolidColorBrush)utility.Foreground!).Color);

        var chipset = links.Single(l => l.Url.EndsWith("chipset.zip"));
        Assert.Equal(AppColors.Eco, ((ISolidColorBrush)chipset.Foreground!).Color);

        Assert.StartsWith(GHelper.Properties.Strings.BiosAndDriverUpdates, window.Title);
        Assert.Contains("GA403UV", window.Title);
        window.Close();
    }

    [AvaloniaFact]
    public void Window_without_inventory_leaves_drivers_unchecked_but_checks_bios()
    {
        var opened = new List<string>();
        var window = Create(opened, inventory: false);
        window.Show();
        window.LoadUpdates(force: true);
        Pump(window);

        var texts = All<TextBlock>(window).Select(t => t.Text).ToList();
        // only the BIOS entry can be compared (against the BIOS version of the machine)
        Assert.Contains(GHelper.Properties.Strings.NewUpdates + ": 1", texts);
        Assert.Equal(1, texts.Count(t => t == UpdatesWindow.SymbolNew));
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_version_opens_its_download_page()
    {
        var opened = new List<string>();
        var window = Create(opened);
        window.Show();
        window.LoadUpdates(force: true);
        Pump(window);

        var link = All<UpdatesWindow.LinkLabel>(window).First(l => l.Url.EndsWith("BIOS_312.zip"));
        var press = new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = InputElementKeyDown(),
            Key = Avalonia.Input.Key.Enter,
            Source = link,
        };
        link.RaiseEvent(press);

        Assert.Equal(new[] { "https://dlcdnets.asus.com/pub/BIOS_312.zip" }, opened);
        window.Close();
    }

    private static Avalonia.Interactivity.RoutedEvent<Avalonia.Input.KeyEventArgs> InputElementKeyDown() =>
        Avalonia.Input.InputElement.KeyDownEvent;

    [AvaloniaTheory]
    [InlineData("dark")]
    [InlineData("light")]
    public void Window_renders(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var window = Create(new List<string>());
        window.Height = 560;
        window.Show();
        window.LoadUpdates(force: true);
        Pump(window);

        var frame = MainWindowSnapshotTests.Render(window, "updates-" + theme);
        Assert.InRange(frame.PixelSize.Width, 650, 670);
        window.Close();
    }
}
