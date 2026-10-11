namespace GHelper.Platform.Linux;

/// <summary>Reads systemd style configuration with drop-ins (logind.conf, sleep.conf).</summary>
internal static class SystemdConf
{
    /// <summary>
    /// Effective value of <paramref name="key"/> in <paramref name="section"/> of /etc/systemd/&lt;file&gt; honouring
    /// /usr/lib, /etc and the *.d drop-in directories (later files win).
    /// </summary>
    public static string? Read(SysfsRoot root, string file, string section, string key)
    {
        var sources = new List<string>
        {
            "/usr/lib/systemd/" + file,
            "/etc/systemd/" + file,
        };

        var dropIns = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string dir in new[] { "/usr/lib/systemd/" + file + ".d", "/run/systemd/" + file + ".d", "/etc/systemd/" + file + ".d" })
        {
            foreach (string path in root.ListFiles(dir))
                if (path.EndsWith(".conf", StringComparison.Ordinal))
                    dropIns[Path.GetFileName(path)] = path; // same name in /etc overrides /usr/lib
        }
        sources.AddRange(dropIns.Values);

        string? value = null;
        foreach (string source in sources)
        {
            string[]? lines = root.ReadLines(source);
            if (lines is null) continue;

            bool inSection = false;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] is '#' or ';') continue;
                if (line[0] == '[')
                {
                    inSection = line.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (line[..eq].Trim().Equals(key, StringComparison.Ordinal))
                    value = line[(eq + 1)..].Trim(); // empty value resets, like systemd
            }
        }
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
