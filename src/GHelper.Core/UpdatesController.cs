using GHelper.Platform;
using System.Net;
using System.Text.Json;

namespace GHelper
{
    /// <summary>
    /// ASUS driver / firmware update list (legacy UpdatesController). Fetching and version resolution logic is
    /// platform independent; what is installed on the machine comes from the optional <see cref="IDriverInventory"/>
    /// (Windows only). Without an inventory every entry stays <see cref="STATUS_NOT_FOUND"/>.
    /// </summary>
    public class UpdatesController
    {
        public const int STATUS_NEW = 1;
        public const int STATUS_UPTODATE = -1;
        public const int STATUS_NOT_FOUND = 2;
        public const int STATUS_HIDDEN = 3;

        public struct DriverUpdate
        {
            public string categoryName;
            public string title;
            public string version;
            public string downloadUrl;
            public string date;
            public string[] hardwares;
            public int status;
            public string tip;
        }

        static readonly string[] SkipList = { "Armoury Crate & Aura Creator Installer", "MyASUS", "ASUS Smart Display Control", "Aura Wallpaper", "Virtual Pet", "Virtual Pet- Ultimate Edition", "Armoury Crate Control Interface", "Virtual Assistant" };

        static readonly HttpClient _httpClient = CreateHttpClient();

        static HttpClient CreateHttpClient()
        {
            var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
            client.DefaultRequestHeaders.Add("User-Agent", "C# App");
            return client;
        }

        private readonly Func<string, CancellationToken, Task<string>> _download;
        private readonly IDriverInventory? _inventory;

        public UpdatesController() : this(null, null)
        {
        }

        /// <param name="download">Replaces the HTTP GET (tests). Null = shared HttpClient.</param>
        /// <param name="inventory">Replaces the platform inventory (tests). Null = optional platform service.</param>
        public UpdatesController(Func<string, CancellationToken, Task<string>>? download, IDriverInventory? inventory)
        {
            _download = download ?? ((url, token) => _httpClient.GetStringAsync(url, token));
            _inventory = inventory;
        }

        private IDriverInventory? Inventory => _inventory ?? PlatformCapabilities.GetOptional<IDriverInventory>();

        public async Task<List<DriverUpdate>> FetchUpdates(string url, CancellationToken token = default)
        {
            Logger.WriteLine(url);
            var json = await _download(url, token);
            var data = JsonSerializer.Deserialize<JsonElement>(json);
            var result = data.GetProperty("Result");

            // fallback for bugged API
            if (result.ToString() == "" || result.GetProperty("Obj").GetArrayLength() == 0)
            {
                var urlFallback = url + "&tag=" + new Random().Next(10, 99);
                Logger.WriteLine(urlFallback);
                json = await _download(urlFallback, token);
                data = JsonSerializer.Deserialize<JsonElement>(json);
            }

            var groups = data.GetProperty("Result").GetProperty("Obj");
            var updates = new List<DriverUpdate>();

            for (int i = 0; i < groups.GetArrayLength(); i++)
            {
                token.ThrowIfCancellationRequested();

                var categoryName = groups[i].GetProperty("Name").ToString();
                var files = groups[i].GetProperty("Files");
                var oldTitle = "";

                for (int j = 0; j < files.GetArrayLength(); j++)
                {
                    var file = files[j];
                    var title = file.GetProperty("Title").ToString();
                    var version = file.GetProperty("Version").ToString().Replace("V", "");
                    if (title.Contains("Realtek LAN")) title += " " + Major(version);

                    if (oldTitle != title && !SkipList.Contains(title) && !title.Contains("Armoury Crate"))
                    {
                        updates.Add(new DriverUpdate
                        {
                            categoryName = categoryName,
                            title = title,
                            version = version,
                            downloadUrl = file.GetProperty("DownloadUrl").GetProperty("Global").ToString(),
                            date = file.GetProperty("ReleaseDate").ToString(),
                            hardwares = ParseHardwares(file.GetProperty("HardwareInfoList")),
                            tip = version,
                            status = STATUS_NOT_FOUND,
                        });
                    }

                    oldTitle = title;
                }
            }

            return updates;
        }

        public void ResolveStatus(List<DriverUpdate> updates, int type, string bios, CancellationToken token = default)
        {
            var inventoryService = Inventory;

            if (type != 0)
            {
                for (int n = 0; n < updates.Count; n++)
                {
                    var u = updates[n];
                    var version = type != 1 ? null
                        : u.title.Contains("MCU") ? inventoryService?.GetMcuFirmwareVersion()
                        : u.title.Contains("Firmware") ? FirmwareVersion(inventoryService, u.title)
                        : bios;
                    if (version is not null && int.TryParse(u.version, out var sv) && int.TryParse(version, out var iv))
                    {
                        u.status = sv > iv ? STATUS_NEW : STATUS_UPTODATE;
                        u.tip = "Download: " + u.version + "\n" + "Installed: " + version;
                        Logger.WriteLine(u.title + " " + u.version + " vs " + version + " = " + u.status);
                    }
                    updates[n] = u;
                }
                return;
            }

            // no way to read installed drivers on this platform
            if (inventoryService is null) return;

            var inventory = BuildInventory(inventoryService);
            var installed = new string?[updates.Count];
            var needStaged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int n = 0; n < updates.Count; n++)
            {
                token.ThrowIfCancellationRequested();
                installed[n] = ResolveInstalledVersion(inventory, updates[n]);
                if (installed[n] is null && updates[n].hardwares.Length > 0)
                    foreach (var h in updates[n].hardwares) needStaged.Add(h);
            }

            HideAbsentAdapters(updates, inventory, installed, needStaged);

            var staged = needStaged.Count > 0 ? inventoryService.BuildStagedVersions(needStaged, token) : null;

            HashSet<string>? packages = null;
            for (int n = 0; n < updates.Count; n++)
            {
                var u = updates[n];
                var version = installed[n];
                if (version is null && staged is not null && u.hardwares.Length > 0)
                    version = MaxVersion(u.hardwares.Where(staged.ContainsKey).Select(h => staged[h]).Where(v => Major(v) == Major(u.version)));

                if (version is not null && Version.TryParse(u.version, out var sv) && Version.TryParse(version, out var iv))
                {
                    u.status = sv > iv ? STATUS_NEW : STATUS_UPTODATE;
                    u.tip = "Download: " + u.version + "\n" + "Installed: " + version;
                    Logger.WriteLine(u.title + " " + u.version + " vs " + version + " = " + u.status);
                }
                // store apps have no versions
                else if (u.version.Contains("store", StringComparison.OrdinalIgnoreCase) && IsStoreAppInstalled(packages ??= inventoryService.GetInstalledStorePackages(), u.title))
                {
                    u.status = STATUS_UPTODATE;
                    Logger.WriteLine(u.title + " [store package] = " + u.status);
                }

                updates[n] = u;
            }
        }

        static List<InstalledDriver> BuildInventory(IDriverInventory inventory)
        {
            try
            {
                return inventory.BuildInventory();
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.ToString());
                return new List<InstalledDriver>();
            }
        }

        static string? FirmwareVersion(IDriverInventory? inventory, string title)
        {
            var key = title.Split('_', ' ')[0];
            if (key.Length < 2) return null;
            return inventory?.GetFirmwareVersion(title);
        }

        static void HideAbsentAdapters(List<DriverUpdate> updates, List<InstalledDriver> inventory, string?[] installed, HashSet<string> needStaged)
        {
            foreach (var keys in new[] { new[] { "Bluetooth" }, new[] { "WLAN", "Wireless LAN", "Wi-Fi", "WiFi" }, new[] { "Realtek LAN" }, new[] { "Card Reader", "CardReader" } })
            {
                var members = Enumerable.Range(0, updates.Count)
                    .Where(n => updates[n].hardwares.Length > 0 && keys.Any(k => updates[n].title.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (members.Count < 2) continue;
                var radio = inventory.Where(d => !d.IsExtension && keys.Any(k => d.Entry.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(d => d.Entry.StartsWith("Microsoft")).FirstOrDefault();
                if (!members.Any(n => installed[n] is not null) && radio.Entry is null) continue;
                bool sameMajor = members.Any(n => installed[n] is not null && Major(updates[n].version) == Major(installed[n]!));

                foreach (var n in members.Where(n => installed[n] is null || (sameMajor && Major(updates[n].version) != Major(installed[n]!))))
                {
                    var u = updates[n];
                    u.status = STATUS_HIDDEN;
                    foreach (var h in u.hardwares) needStaged.Remove(h);
                    Logger.WriteLine(u.title + " = hidden, detected: " + radio.Entry);
                    updates[n] = u;
                }
            }
        }

        static string[] ParseHardwares(JsonElement hardwares)
        {
            if (hardwares.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var list = new List<string>();
            for (int k = 0; k < hardwares.GetArrayLength(); k++)
                list.Add(CleanupDeviceId(hardwares[k].GetProperty("hardwareid").ToString()));
            return list.ToArray();
        }

        static string? ResolveInstalledVersion(List<InstalledDriver> inventory, DriverUpdate item)
        {
            if (item.hardwares.Length > 0)
            {
                var matched = inventory.Where(d => item.hardwares.Any(id => d.MatchId.Contains(id, StringComparison.OrdinalIgnoreCase))).ToList();
                if (matched.Count == 0) return null;

                // match major version
                int major = Major(item.version);
                var pool = matched.Where(d => Major(d.Version) == major).ToList();
                if (pool.Count == 0) pool = matched.Where(d => !d.IsExtension || Major(d.Version) > major).ToList();
                if (pool.Count == 0) pool = matched;
                return MaxVersion(pool, item.title);
            }

            // no hardware id in the API (Dolby, displayHDR)
            var key = item.title.Split(' ')[0];
            if (key.Length < 3) return null;
            return MaxVersion(inventory.Where(d => d.IsExtension && Major(d.Version) == Major(item.version) && d.Entry.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList(), item.title);
        }

        static int Major(string version) => Version.TryParse(version, out var v) ? v.Major : -1;

        static string? MaxVersion(IEnumerable<string> versions)
        {
            Version? best = null;
            string? bestString = null;
            foreach (var version in versions)
                if (Version.TryParse(version, out var v) && (best is null || v > best))
                {
                    best = v;
                    bestString = version;
                }
            return bestString;
        }

        static string? MaxVersion(List<InstalledDriver> drivers, string title)
        {
            var version = MaxVersion(drivers.Select(d => d.Version));
            if (version is null) return null;
            var d = drivers.First(x => x.Version == version);
            Logger.WriteLine(title + ": " + (!d.IsExtension ? "driver " + d.MatchId : d.MatchId.Length == 0 ? "registry " + d.Entry : "extension " + d.Entry));
            return version;
        }

        static string CleanupDeviceId(string input)
        {
            int index = input.IndexOf("&REV_");
            return index != -1 ? input.Substring(0, index) : input;
        }

        static bool IsStoreAppInstalled(HashSet<string> packages, string title)
        {
            var key = title.Split(' ')[0];
            return key.Length >= 3 && packages.Any(p => p.Contains(key, StringComparison.OrdinalIgnoreCase));
        }

        public string GetSerialNumber()
        {
            try
            {
                return PlatformServices.Current.System.GetSerialNumber()?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.ToString());
            }
            return string.Empty;
        }
    }
}
