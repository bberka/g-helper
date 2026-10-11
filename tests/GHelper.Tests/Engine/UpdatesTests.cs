using GHelper.AutoUpdate;
using GHelper.Platform;
using Xunit;

namespace GHelper.Tests.Engine;

public class UpdatesControllerTests : EngineTestBase
{
    public UpdatesControllerTests(EngineFixture fixture) : base(fixture) { }

    private const string Json = """
    {"Result":{"Obj":[
      {"Name":"BIOS","Files":[
        {"Title":"BIOS GA402RJ","Version":"V320","DownloadUrl":{"Global":"https://dl/bios.zip"},"ReleaseDate":"2023/01/01","HardwareInfoList":[]}
      ]},
      {"Name":"Driver","Files":[
        {"Title":"Realtek LAN Driver","Version":"V10.60.1.0","DownloadUrl":{"Global":"https://dl/lan.zip"},"ReleaseDate":"2023/02/02",
         "HardwareInfoList":[{"hardwareid":"PCI\\VEN_10EC&DEV_8168&REV_15"}]},
        {"Title":"Realtek LAN Driver","Version":"V10.50.0.0","DownloadUrl":{"Global":"https://dl/lan-old.zip"},"ReleaseDate":"2022/02/02","HardwareInfoList":null},
        {"Title":"MyASUS","Version":"V1.0","DownloadUrl":{"Global":"https://dl/myasus.zip"},"ReleaseDate":"d","HardwareInfoList":null},
        {"Title":"Armoury Crate Service","Version":"V5.0","DownloadUrl":{"Global":"u"},"ReleaseDate":"d","HardwareInfoList":null}
      ]}
    ]}}
    """;

    private UpdatesController Controller(params string[] responses)
    {
        int call = 0;
        return new UpdatesController(
            (url, token) => Task.FromResult(responses[Math.Min(call++, responses.Length - 1)]),
            P);
    }

    [Fact]
    public async Task FetchParsesVersionsAndDropsSkippedTitles()
    {
        var updates = await Controller(Json).FetchUpdates("https://api/updates?x=1");

        Assert.Equal(2, updates.Count);   // MyASUS + Armoury Crate are skipped, the repeated LAN title is dropped

        var bios = updates[0];
        Assert.Equal("BIOS", bios.categoryName);
        Assert.Equal("320", bios.version);
        Assert.Equal("https://dl/bios.zip", bios.downloadUrl);
        Assert.Equal(UpdatesController.STATUS_NOT_FOUND, bios.status);
        Assert.Empty(bios.hardwares);

        var lan = updates[1];
        Assert.Equal("Realtek LAN Driver 10", lan.title);
        Assert.Equal("10.60.1.0", lan.version);
        Assert.Equal(new[] { "PCI\\VEN_10EC&DEV_8168" }, lan.hardwares);   // &REV_ stripped
    }

    [Fact]
    public async Task FetchRetriesBuggyEmptyResult()
    {
        var urls = new List<string>();
        var controller = new UpdatesController((url, token) =>
        {
            urls.Add(url);
            return Task.FromResult(urls.Count == 1 ? "{\"Result\":\"\"}" : Json);
        }, P);

        var updates = await controller.FetchUpdates("https://api/updates?x=1");

        Assert.Equal(2, urls.Count);
        Assert.StartsWith("https://api/updates?x=1&tag=", urls[1]);
        Assert.Equal(2, updates.Count);
    }

    [Fact]
    public async Task BiosStatusComparesNumericVersion()
    {
        var controller = Controller(Json);
        var updates = await controller.FetchUpdates("u");

        controller.ResolveStatus(updates, type: 1, bios: "310");
        Assert.Equal(UpdatesController.STATUS_NEW, updates[0].status);
        Assert.Contains("Installed: 310", updates[0].tip);

        controller.ResolveStatus(updates, type: 1, bios: "320");
        Assert.Equal(UpdatesController.STATUS_UPTODATE, updates[0].status);
    }

    [Fact]
    public async Task DriverStatusUsesPlatformInventory()
    {
        P.Installed = new List<InstalledDriver>
        {
            new("PCI\\VEN_10EC&DEV_8168", "10.50.0.0", false, "Realtek PCIe GbE Family Controller"),
        };
        var controller = Controller(Json);
        var updates = await controller.FetchUpdates("u");

        controller.ResolveStatus(updates, type: 0, bios: "");

        var lan = updates[1];
        Assert.Equal(UpdatesController.STATUS_NEW, lan.status);
        Assert.Equal("Download: 10.60.1.0\nInstalled: 10.50.0.0", lan.tip);
    }

    [Fact]
    public async Task DriverStatusUpToDateWhenInstalledIsNewer()
    {
        P.Installed = new List<InstalledDriver>
        {
            new("PCI\\VEN_10EC&DEV_8168", "10.60.1.0", false, "Realtek PCIe GbE Family Controller"),
        };
        var controller = Controller(Json);
        var updates = await controller.FetchUpdates("u");

        controller.ResolveStatus(updates, type: 0, bios: "");

        Assert.Equal(UpdatesController.STATUS_UPTODATE, updates[1].status);
    }

    [Fact]
    public async Task DriverStatusFallsBackToStagedDriverStore()
    {
        P.Staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["PCI\\VEN_10EC&DEV_8168"] = "10.40.0.0" };
        var controller = Controller(Json);
        var updates = await controller.FetchUpdates("u");

        try
        {
            controller.ResolveStatus(updates, type: 0, bios: "");
            // major versions equal (10) -> staged version is used
            Assert.Equal(UpdatesController.STATUS_NEW, updates[1].status);
        }
        finally
        {
            P.Staged = new();
        }
    }

    [Fact]
    public async Task McuFirmwareComparesAgainstInventory()
    {
        const string mcu = """
        {"Result":{"Obj":[{"Name":"Firmware","Files":[
          {"Title":"ROG MCU Firmware","Version":"V8","DownloadUrl":{"Global":"u"},"ReleaseDate":"d","HardwareInfoList":[]}]}]}}
        """;
        P.McuVersion = "5";
        var controller = Controller(mcu);
        var updates = await controller.FetchUpdates("u");

        controller.ResolveStatus(updates, type: 1, bios: "999");

        Assert.Equal(UpdatesController.STATUS_NEW, updates[0].status);
        P.McuVersion = null;
    }

    [Fact]
    public void SerialNumberIsTrimmed()
    {
        Assert.Equal("SN12345", new UpdatesController().GetSerialNumber());
    }
}

public class AutoUpdateControlTests : EngineTestBase
{
    public AutoUpdateControlTests(EngineFixture fixture) : base(fixture)
    {
        CleanConfig("update_repo", "skip_version", "skip_updates");
    }

    private const string Release = """
    {"tag_name":"v9.9.9","html_url":"https://github.com/bberka/g-helper/releases/tag/v9.9.9",
     "assets":[{"browser_download_url":"https://github.com/x/GHelper.msi"},{"browser_download_url":"https://github.com/x/GHelper.zip"},{"browser_download_url":"https://github.com/x/GHelper.sha"}]}
    """;

    [Fact]
    public void RepoDefaultsToForkAndCanBeOverridden()
    {
        Assert.Equal("bberka/g-helper", AutoUpdateControl.Repo);
        Assert.Equal("https://api.github.com/repos/bberka/g-helper/releases/latest", AutoUpdateControl.LatestReleaseApiUrl);
        Assert.Equal("https://github.com/bberka/g-helper/releases", AutoUpdateControl.ReleasesPageUrl);

        AppConfig.Set("update_repo", "someone/else");
        Assert.Equal("someone/else", AutoUpdateControl.Repo);
        Assert.Equal("https://api.github.com/repos/someone/else/releases/latest", AutoUpdateControl.LatestReleaseApiUrl);
    }

    [Fact]
    public void ParseReleasePicksLastZipForSelfUpdate()
    {
        var info = AutoUpdateControl.ParseRelease(Release, preferZip: true);

        Assert.Equal("9.9.9", info.Tag);
        Assert.Equal(new Version(9, 9, 9), info.Version);
        Assert.Equal("https://github.com/x/GHelper.zip", info.DownloadUrl);
        Assert.Equal("https://github.com/bberka/g-helper/releases/tag/v9.9.9", info.PageUrl);
    }

    [Fact]
    public void ParseReleaseUsesReleasePageWhenNotSelfUpdating()
    {
        var info = AutoUpdateControl.ParseRelease(Release, preferZip: false);

        Assert.Equal("https://github.com/bberka/g-helper/releases/tag/v9.9.9", info.DownloadUrl);
    }

    [Fact]
    public void ParseReleaseFallsBackToFirstAssetOrPage()
    {
        var noZip = """{"tag_name":"v1.2","assets":[{"browser_download_url":"https://x/a.msi"}]}""";
        Assert.Equal("https://x/a.msi", AutoUpdateControl.ParseRelease(noZip, preferZip: true).DownloadUrl);

        var noAssets = """{"tag_name":"v1.2","assets":[]}""";
        Assert.Equal(AutoUpdateControl.ReleasesPageUrl, AutoUpdateControl.ParseRelease(noAssets, preferZip: true).DownloadUrl);
    }

    [Fact]
    public void VersionLabelShowsCurrentVersion()
    {
        var control = new AutoUpdateControl();

        Assert.Contains(Program.AppVersion.Major + "." + Program.AppVersion.Minor, control.VersionLabel);
        Assert.False(control.VersionLabelHighlight);
    }

    [Fact]
    public async Task NewerReleaseAnnouncesAndOpensReleasePageOnNonWindows()
    {
        var control = new AutoUpdateControl { FetchJson = _ => Task.FromResult(Release) };
        Ui.ConfirmAnswer = true;
        var labels = new List<(string text, bool highlight)>();
        control.VersionLabelChanged += (t, h) => labels.Add((t, h));

        await control.CheckForUpdatesAsync();

        Assert.True(control.update);
        Assert.Single(labels);
        Assert.True(labels[0].highlight);
        Assert.EndsWith("→ 9.9.9", labels[0].text);
        Assert.Equal(1, Ui.Confirms);
        Assert.Contains("OpenUrl:https://github.com/bberka/g-helper/releases/tag/v9.9.9", P.ProcessService.Calls);
        Assert.Contains("version", Ui.Refreshes);
    }

    [Fact]
    public async Task DeclinedUpdateIsRememberedPerVersion()
    {
        var control = new AutoUpdateControl { FetchJson = _ => Task.FromResult(Release) };
        Ui.ConfirmAnswer = false;

        await control.CheckForUpdatesAsync();
        Assert.Equal("9.9.9", AppConfig.GetString("skip_version"));
        Assert.Equal(1, Ui.Confirms);

        await control.CheckForUpdatesAsync();
        Assert.Equal(1, Ui.Confirms);   // not asked again for the same tag
    }

    [Fact]
    public async Task OlderOrEqualReleaseIsIgnored()
    {
        var control = new AutoUpdateControl { FetchJson = _ => Task.FromResult("""{"tag_name":"v0.1","assets":[]}""") };

        await control.CheckForUpdatesAsync();

        Assert.False(control.update);
        Assert.Equal(0, Ui.Confirms);
    }

    [Fact]
    public async Task SkipUpdatesFlagDisablesCheck()
    {
        AppConfig.Set("skip_updates", 1);
        bool called = false;
        var control = new AutoUpdateControl { FetchJson = _ => { called = true; return Task.FromResult(Release); } };

        await control.CheckForUpdatesAsync();

        Assert.False(called);
    }

    [Fact]
    public async Task NetworkFailureIsSwallowed()
    {
        var control = new AutoUpdateControl { FetchJson = _ => throw new HttpRequestException("offline") };

        await control.CheckForUpdatesAsync();

        Assert.False(control.update);
    }

    [Fact]
    public async Task ManualUpdateWithoutPendingReleaseOpensReleasesPage()
    {
        var control = new AutoUpdateControl();

        control.Update();
        Assert.Contains("OpenUrl:https://github.com/bberka/g-helper/releases", P.ProcessService.Calls);
        await Task.CompletedTask;
    }

    [Fact]
    public void EscapeStringProtectsPowerShellQuotes()
    {
        Assert.Equal("C:\\Program `[x`]\\it''s", AutoUpdateControl.EscapeString("C:\\Program [x]\\it's"));
    }
}
