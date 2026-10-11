using System.Globalization;
using System.Text;

namespace GHelper.Platform.Linux;

/// <summary>
/// Every file access of the Linux platform goes through this class. Paths are always written as absolute logical
/// Linux paths ("/sys/class/power_supply/BAT0/status"); <see cref="Map"/> re-roots them below <see cref="RootPath"/>,
/// which lets unit tests run against a fake directory tree. The default root is "/".
/// All read helpers return null (never throw) when the file is missing or unreadable.
/// </summary>
public sealed class SysfsRoot
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static SysfsRoot Default { get; } = new("/");

    public string RootPath { get; }
    public bool IsRealRoot { get; }

    public SysfsRoot(string rootPath = "/")
    {
        RootPath = string.IsNullOrEmpty(rootPath) ? "/" : rootPath;
        IsRealRoot = RootPath == "/";
    }

    /// <summary>Logical path to physical path.</summary>
    public string Map(string logicalPath)
    {
        if (IsRealRoot) return logicalPath;
        return Path.Combine(RootPath, logicalPath.TrimStart('/'));
    }

    public bool FileExists(string path)
    {
        try { return File.Exists(Map(path)); } catch { return false; }
    }

    public bool DirectoryExists(string path)
    {
        try { return Directory.Exists(Map(path)); } catch { return false; }
    }

    public bool Exists(string path) => FileExists(path) || DirectoryExists(path);

    /// <summary>File content with surrounding whitespace trimmed, null when unreadable.</summary>
    public string? ReadText(string path)
    {
        try
        {
            return File.ReadAllText(Map(path), Utf8NoBom).Trim();
        }
        catch
        {
            return null;
        }
    }

    public string[]? ReadLines(string path)
    {
        try { return File.ReadAllLines(Map(path), Utf8NoBom); } catch { return null; }
    }

    public byte[]? ReadBytes(string path)
    {
        try { return File.ReadAllBytes(Map(path)); } catch { return null; }
    }

    /// <summary>Decimal or 0x-prefixed hex integer (first whitespace separated token).</summary>
    public long? ReadLong(string path) => ParseLong(ReadText(path));

    public int? ReadInt(string path)
    {
        long? value = ReadLong(path);
        if (value is null || value < int.MinValue || value > int.MaxValue) return null;
        return (int)value;
    }

    public static long? ParseLong(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string token = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        try
        {
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt64(token[2..], 16);
            if (long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v))
                return v;
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Single write() of the text to an existing attribute (sysfs/debugfs semantics: open the existing file, truncate,
    /// write once). Returns false when the file does not exist or the kernel rejected the value.
    /// </summary>
    public bool TryWrite(string path, string value) => TryWriteBytes(path, Utf8NoBom.GetBytes(value));

    public bool TryWriteBytes(string path, byte[] data)
    {
        try
        {
            using var fs = new FileStream(Map(path), FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite, 1);
            fs.Write(data, 0, data.Length);
            fs.Flush();
            return true;
        }
        catch (Exception ex)
        {
            LinuxLog.Write($"write {path} failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Creates or overwrites a regular file (config style files, not kernel attributes).</summary>
    public bool TryCreateFile(string path, string content)
    {
        try
        {
            string physical = Map(path);
            Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
            File.WriteAllText(physical, content, Utf8NoBom);
            return true;
        }
        catch (Exception ex)
        {
            LinuxLog.Write($"create {path} failed: {ex.Message}");
            return false;
        }
    }

    public bool TryDelete(string path)
    {
        try
        {
            string physical = Map(path);
            if (File.Exists(physical)) File.Delete(physical);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Full logical paths of the sub directories (symlinked directories included), sorted by name.</summary>
    public string[] ListDirectories(string path)
    {
        try
        {
            string physical = Map(path);
            if (!Directory.Exists(physical)) return [];
            return Directory.EnumerateDirectories(physical)
                .Select(p => Combine(path, Path.GetFileName(p)))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }
        catch { return []; }
    }

    public string[] ListFiles(string path)
    {
        try
        {
            string physical = Map(path);
            if (!Directory.Exists(physical)) return [];
            return Directory.EnumerateFiles(physical)
                .Select(p => Combine(path, Path.GetFileName(p)))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }
        catch { return []; }
    }

    /// <summary>Entry names (files and directories) of a directory.</summary>
    public string[] ListNames(string path)
    {
        try
        {
            string physical = Map(path);
            if (!Directory.Exists(physical)) return [];
            return Directory.EnumerateFileSystemEntries(physical)
                .Select(p => Path.GetFileName(p))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }
        catch { return []; }
    }

    /// <summary>Target of a symbolic link or null.</summary>
    public string? ReadLink(string path)
    {
        try { return new FileInfo(Map(path)).LinkTarget; } catch { return null; }
    }

    public static string Combine(string dir, string name)
        => dir.EndsWith('/') ? dir + name : dir + "/" + name;
}

/// <summary>Diagnostic sink. Never throws; replaced by tests.</summary>
internal static class LinuxLog
{
    public static Action<string>? Sink { get; set; }

    public static void Write(string message)
    {
        try
        {
            if (Sink is not null) Sink(message);
            else Logger.WriteLine(message);
        }
        catch { }
    }
}
