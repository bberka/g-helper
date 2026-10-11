namespace GHelper.Platform.Linux;

/// <summary>/proc helpers (process lookup by name without spawning pgrep, open device users).</summary>
internal static class ProcFs
{
    /// <summary>pids whose comm (max 15 chars, kernel truncated) or executable name equals <paramref name="name"/>.</summary>
    public static List<int> FindByName(SysfsRoot root, string name)
    {
        var result = new List<int>();
        string comm = name.Length > 15 ? name[..15] : name;

        foreach (string dir in root.ListDirectories("/proc"))
        {
            string entry = Path.GetFileName(dir);
            if (!int.TryParse(entry, out int pid)) continue;

            string? value = root.ReadText(dir + "/comm");
            if (value is null) continue;
            if (value == comm || value == name) result.Add(pid);
        }
        return result;
    }

    public static bool IsRunning(SysfsRoot root, string name) => FindByName(root, name).Count > 0;

    /// <summary>pids with an open file descriptor to a path accepted by <paramref name="match"/> (root needed for foreign processes).</summary>
    public static List<int> FindFileUsers(SysfsRoot root, Func<string, bool> match, int ignorePid = -1)
    {
        var result = new List<int>();
        foreach (string dir in root.ListDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out int pid) || pid == ignorePid) continue;

            foreach (string name in root.ListNames(dir + "/fd"))
            {
                string? target = root.ReadLink(dir + "/fd/" + name);
                if (target is not null && match(target))
                {
                    result.Add(pid);
                    break;
                }
            }
        }
        return result;
    }
}
