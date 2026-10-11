using GHelper.Helpers;
using GHelper.Platform;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GHelper.AutoUpdate
{
    /// <summary>A GitHub release reduced to what the updater needs.</summary>
    public sealed record ReleaseInfo(string Tag, Version? Version, string DownloadUrl, string PageUrl);

    /// <summary>
    /// UI-free update check (legacy AutoUpdateControl). The version label / prompts go through <see cref="Program.Ui"/> and
    /// <see cref="VersionLabelChanged"/>. Windows replaces the exe from the release zip; other platforms only notify
    /// and open the release page.
    /// </summary>
    public class AutoUpdateControl
    {
        public const string DefaultRepo = "bberka/g-helper";

        /// <summary>GitHub "owner/name" releases are read from. Config key <c>update_repo</c> overrides the default.</summary>
        public static string Repo
        {
            get
            {
                string? repo = AppConfig.GetString("update_repo");
                return string.IsNullOrWhiteSpace(repo) ? DefaultRepo : repo.Trim();
            }
        }

        public static string ReleasesPageUrl => $"https://github.com/{Repo}/releases";
        public static string LatestReleaseApiUrl => $"https://api.github.com/repos/{Repo}/releases/latest";

        /// <summary>Opened by <see cref="LoadReleases"/>: the releases page, or the asset url once an update was found.</summary>
        public string versionUrl = ReleasesPageUrl;
        public bool update = false;

        /// <summary>Current text for the version label (legacy settings.SetVersionLabel).</summary>
        public string VersionLabel { get; private set; } = "";

        /// <summary>True when the label announces a new version (legacy second SetVersionLabel argument).</summary>
        public bool VersionLabelHighlight { get; private set; }

        /// <summary>Raised on a background thread with (label text, highlight).</summary>
        public event Action<string, bool>? VersionLabelChanged;

        static long lastUpdate;

        /// <summary>Test hook: replaces the GitHub API GET.</summary>
        public Func<string, Task<string>>? FetchJson { get; set; }

        /// <summary>True when this process can replace its own files from a release zip.</summary>
        public static bool SelfUpdateSupported =>
            OperatingSystem.IsWindows() && PlatformServices.IsRegistered && PlatformServices.Current.System.Kind == PlatformKind.Windows;

        public AutoUpdateControl()
        {
            var appVersion = Program.AppVersion;
            SetVersionLabel(Properties.Strings.VersionLabel + $": {appVersion.Major}.{appVersion.Minor}.{Math.Max(0, appVersion.Build)}", false);
        }

        private void SetVersionLabel(string text, bool highlight)
        {
            VersionLabel = text;
            VersionLabelHighlight = highlight;
            VersionLabelChanged?.Invoke(text, highlight);
            Program.Ui.Refresh("version");
        }

        public void CheckForUpdates()
        {
            // Run update once per 12 hours
            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeSeconds() - lastUpdate) < 43200) return;
            lastUpdate = DateTimeOffset.Now.ToUnixTimeSeconds();

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                await CheckForUpdatesAsync();
            });
        }

        public void Update()
        {
            if (update)
            {
                Task.Run(() => CheckForUpdatesAsync(true));
            } else
            {
                LoadReleases();
            }
        }

        public void LoadReleases()
        {
            try
            {
                PlatformServices.Current.Process.OpenUrl(versionUrl);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Failed to open releases page:" + ex.Message);
            }
        }

        /// <summary>
        /// Parses the GitHub "latest release" document. <paramref name="preferZip"/> selects the .zip asset (self update);
        /// otherwise the release page is the download url.
        /// </summary>
        public static ReleaseInfo ParseRelease(string json, bool preferZip)
        {
            var config = JsonSerializer.Deserialize<JsonElement>(json);
            var tag = config.GetProperty("tag_name").ToString().Replace("v", "");

            string page = config.TryGetProperty("html_url", out var html) && html.ValueKind == JsonValueKind.String
                ? html.GetString()!
                : ReleasesPageUrl;

            string? url = null;

            if (preferZip && config.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                for (int i = 0; i < assets.GetArrayLength(); i++)
                {
                    string asset = assets[i].GetProperty("browser_download_url").ToString();
                    if (asset.Contains(".zip")) url = asset;
                }

                if (url is null && assets.GetArrayLength() > 0)
                    url = assets[0].GetProperty("browser_download_url").ToString();
            }

            Version.TryParse(tag, out var version);
            return new ReleaseInfo(tag, version, url ?? page, page);
        }

        private async Task<string> GetJson(string url)
        {
            if (FetchJson is not null) return await FetchJson(url);

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "G-Helper App");
            return await httpClient.GetStringAsync(url);
        }

        public async Task CheckForUpdatesAsync(bool force = false)
        {

            if (AppConfig.Is("skip_updates")) return;

            try
            {
                var json = await GetJson(LatestReleaseApiUrl);
                var release = ParseRelease(json, SelfUpdateSupported);

                var appVersion = Program.AppVersion;
                //appVersion = new Version("0.50.0.0");

                if (release.Version is not null && release.Version.CompareTo(appVersion) > 0)
                {
                    versionUrl = release.DownloadUrl;
                    update = true;
                    SetVersionLabel(Properties.Strings.DownloadUpdate + $": {appVersion.Major}.{appVersion.Minor}.{Math.Max(0, appVersion.Build)} → {release.Tag}", true);

                    string[] args = Environment.GetCommandLineArgs();
                    if (force || args.Length > 1 && args[1] == "autoupdate")
                    {
                        await AutoUpdate(release.DownloadUrl);
                        return;
                    }

                    if (AppConfig.GetString("skip_version") != release.Tag)
                    {
                        bool yes = Program.Ui.Confirm("Update", Properties.Strings.DownloadUpdate + ": G-Helper " + release.Tag + "?");
                        if (yes)
                            await AutoUpdate(release.DownloadUrl);
                        else
                            AppConfig.Set("skip_version", release.Tag);
                    }

                }
                else
                {
                    Logger.WriteLine($"Latest version {appVersion}");
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Failed to check for updates:" + ex.Message);
            }

        }

        public static string EscapeString(string input)
        {
            return Regex.Replace(Regex.Replace(input, @"\[|\]", "`$0"), @"\'", "''");
        }

        async Task AutoUpdate(string requestUri)
        {
            // Nothing to replace in place: send the user to the release page
            if (!SelfUpdateSupported)
            {
                LoadReleases();
                return;
            }

            Uri uri = new Uri(requestUri);
            string zipName = Path.GetFileName(uri.LocalPath);

            string exeLocation = Environment.ProcessPath ?? "";
            string exeDir = Path.GetDirectoryName(exeLocation) ?? "";
            string exeName = Path.GetFileName(exeLocation);
            string zipLocation = Path.Combine(exeDir, zipName);

            using (HttpClient client = new HttpClient())
            {

                client.DefaultRequestHeaders.Add("User-Agent", "G-Helper App");
                Logger.WriteLine(requestUri);
                Logger.WriteLine(exeDir);
                Logger.WriteLine(zipName);
                Logger.WriteLine(exeName);

                try
                {
                    var bytes = await client.GetByteArrayAsync(uri);
                    File.WriteAllBytes(zipLocation, bytes);
                    Logger.WriteLine($"Downloaded {bytes.Length}b: {zipLocation} (exists={File.Exists(zipLocation)}, size={new FileInfo(zipLocation).Length})");
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(ex.Message);
                    if (!ProcessHelper.IsUserAdministrator())
                    {
                        ProcessHelper.RunAsAdmin("autoupdate");
                        Program.Ui.Quit();
                    } else
                    {
                        LoadReleases();
                    }
                    return;
                }

                string processName = Path.GetFileNameWithoutExtension(exeName);
                string command = $"$ErrorActionPreference = \"Stop\"; Set-Location -Path '{EscapeString(exeDir)}'; Wait-Process -Name \"{processName}\"; Expand-Archive \"{zipName}\" -DestinationPath . -Force; Remove-Item \"{zipName}\" -Force; \".\\{exeName}\"; ";
                Logger.WriteLine(command);

                try
                {
                    var cmd = new Process();
                    cmd.StartInfo.WorkingDirectory = exeDir;
                    cmd.StartInfo.UseShellExecute = false;
                    cmd.StartInfo.CreateNoWindow = true;
                    cmd.StartInfo.FileName = "powershell";
                    cmd.StartInfo.Arguments = command;
                    if (ProcessHelper.IsUserAdministrator()) cmd.StartInfo.Verb = "runas";
                    cmd.Start();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(ex.Message);
                }

                Program.Ui.Quit();
            }

        }

    }
}
