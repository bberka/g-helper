using GHelper.Platform;
using System.Diagnostics;
using System.IO.Compression;

namespace GHelper.AnimeMatrix
{
    /// <summary>
    /// Finds / downloads the ROG matrix font. Core only manages the file and the family name, drawing is done by the
    /// <see cref="IMatrixTextRenderer"/> registered by the UI layer.
    /// </summary>
    public static class MatrixFont
    {
        const string PackageUrl = "https://dlcdnets.asus.com/pub/ASUS/GamingNB/AppforWin10/ROGFont/ROG_Font_V1.5.zip";

        static readonly string[] installedNames = { "AniMe Matrix Font", "ROG Fonts v1.5" };

        static string? family;
        static bool resolved;

        public static string FontFile
        {
            get
            {
                string directory = PlatformServices.IsRegistered
                    ? PlatformServices.Current.System.ConfigDirectory
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GHelper");
                return Path.Combine(directory, "matrix.otf");
            }
        }

        /// <summary>Family name of the matrix font, null when it is neither installed nor downloaded (or no renderer is registered yet).</summary>
        public static string? Family
        {
            get
            {
                if (resolved) return family;

                var renderer = MatrixRendering.TextRenderer;
                if (renderer is null) return null;

                family = FindInstalled(renderer) ?? LoadFile(renderer);
                resolved = true;
                return family;
            }
        }

        public static bool IsAvailable => Family is not null;

        static string? FindInstalled(IMatrixTextRenderer renderer)
        {
            foreach (var name in installedNames)
                try
                {
                    if (renderer.IsFontInstalled(name)) return name;
                }
                catch { }

            return null;
        }

        static string? LoadFile(IMatrixTextRenderer renderer)
        {
            string file = FontFile;
            if (!File.Exists(file)) return null;

            try
            {
                return renderer.LoadFontFile(file);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Matrix font: " + ex.Message);
                return null;
            }
        }

        public static async Task<bool> Download()
        {
            if (Family is not null) return true;

            string temp = Path.Combine(Path.GetTempPath(), "GHelperFont");

            try
            {
                Directory.CreateDirectory(temp);
                string zip = Path.Combine(temp, "font.zip");

                using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    Logger.WriteLine("Getting: " + PackageUrl);
                    await File.WriteAllBytesAsync(zip, await client.GetByteArrayAsync(PackageUrl));
                }

                ZipFile.ExtractToDirectory(zip, temp, true);
                string msi = Directory.GetFiles(temp, "*.msi", SearchOption.AllDirectories)[0];

                // administrative install just unpacks the font, without registering it in the system
                ProcessStartInfo unpack = OperatingSystem.IsWindows()
                    ? new ProcessStartInfo("msiexec", $"/a \"{msi}\" /qn TARGETDIR=\"{temp}\"") { CreateNoWindow = true }
                    : new ProcessStartInfo("msiextract", $"-C \"{temp}\" \"{msi}\"") { CreateNoWindow = true, UseShellExecute = false };

                using (var process = Process.Start(unpack))
                    if (process is not null) await process.WaitForExitAsync();

                string file = FontFile;
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.Copy(Directory.GetFiles(temp, "*.otf", SearchOption.AllDirectories)[0], file, true);

                resolved = false;
                Logger.WriteLine("Matrix font: " + Family);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Matrix font: " + ex.Message);
            }

            try { Directory.Delete(temp, true); } catch { }

            return Family is not null;
        }
    }
}
