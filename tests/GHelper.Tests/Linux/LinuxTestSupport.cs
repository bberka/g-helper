using System.Runtime.CompilerServices;
using GHelper.Platform.Linux;

namespace GHelper.Tests.Linux;

/// <summary>Keeps the platform code from writing into the real log while tests run.</summary>
internal static class LinuxTestInit
{
    [ModuleInitializer]
    public static void Init()
    {
        LinuxLog.Sink = _ => { };
    }
}

/// <summary>A throw-away directory standing in for "/" (sysfs, procfs, dmi, ...).</summary>
internal sealed class FakeSysfs : IDisposable
{
    public string Path { get; }
    public SysfsRoot Root { get; }

    public FakeSysfs()
    {
        // short on purpose: unix domain socket paths are limited to ~108 characters
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gh-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
        Root = new SysfsRoot(Path);
    }

    public string Physical(string logical) => Root.Map(logical);

    public FakeSysfs File(string logicalPath, string content)
    {
        string physical = Physical(logicalPath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(physical)!);
        System.IO.File.WriteAllText(physical, content);
        return this;
    }

    public FakeSysfs Bytes(string logicalPath, byte[] content)
    {
        string physical = Physical(logicalPath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(physical)!);
        System.IO.File.WriteAllBytes(physical, content);
        return this;
    }

    public FakeSysfs Dir(string logicalPath)
    {
        Directory.CreateDirectory(Physical(logicalPath));
        return this;
    }

    public string Read(string logicalPath) => System.IO.File.ReadAllText(Physical(logicalPath)).Trim();

    public bool Exists(string logicalPath) => System.IO.File.Exists(Physical(logicalPath));

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { }
    }
}

/// <summary>Scripted ICommandRunner: records calls, answers by "file arg arg" prefix.</summary>
internal sealed class FakeRunner : ICommandRunner
{
    public readonly List<string> Calls = new();
    public readonly Dictionary<string, CommandResult> Responses = new();
    public readonly HashSet<string> Installed = new();
    public readonly List<(string file, IReadOnlyList<string> args, Action<string> sink)> LineStreams = new();

    public FakeRunner Has(params string[] commands)
    {
        foreach (string c in commands) Installed.Add(c);
        return this;
    }

    public FakeRunner Reply(string commandLine, string stdout, int exitCode = 0)
    {
        Responses[commandLine] = new CommandResult(true, exitCode, stdout, "");
        return this;
    }

    public CommandResult Run(string fileName, IReadOnlyList<string> args, int timeoutMs = 5000, string? stdin = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        string line = string.Join(' ', new[] { fileName }.Concat(args));
        lock (Calls) Calls.Add(line);
        if (!Installed.Contains(fileName) && !Responses.ContainsKey(line)) return CommandResult.NotStarted;
        return Responses.TryGetValue(line, out CommandResult r) ? r : new CommandResult(true, 0, "", "");
    }

    public bool CommandExists(string fileName) => Installed.Contains(fileName);

    public IDisposable? StartLineStream(string fileName, IReadOnlyList<string> args, Action<string> onLine)
    {
        if (!Installed.Contains(fileName)) return null;
        LineStreams.Add((fileName, args, onLine));
        return new Noop();
    }

    public IDisposable? StartBinaryStream(string fileName, IReadOnlyList<string> args, Action<byte[], int> onData)
        => Installed.Contains(fileName) ? new Noop() : null;

    public int StartDetached(string fileName, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? environment = null)
    {
        lock (Calls) Calls.Add("detached:" + string.Join(' ', new[] { fileName }.Concat(args)));
        return 1234;
    }

    private sealed class Noop : IDisposable
    {
        public void Dispose() { }
    }
}
