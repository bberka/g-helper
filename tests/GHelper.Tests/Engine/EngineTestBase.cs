using GHelper.Platform;
using Xunit;

namespace GHelper.Tests.Engine;

/// <summary>Base class: shared fake platform + a clean fake state for every test (new class instance per test).</summary>
[Collection("Engine")]
public abstract class EngineTestBase
{
    protected FakePlatform P => EngineFixture.Platform;
    protected FakeAsusBackend Asus => EngineFixture.Platform.AsusBackend;
    protected FakeUiHost Ui => EngineFixture.Ui;

    protected EngineTestBase(EngineFixture fixture)
    {
        _ = fixture;
        EngineFixture.Reset();
    }

    /// <summary>Config keys written by a test are removed again so tests stay independent (AppConfig is process wide).</summary>
    protected static void CleanConfig(params string[] keys)
    {
        foreach (var key in keys) AppConfig.Remove(key);
    }

    protected static bool WaitFor(Func<bool> condition, int timeoutMs = 3000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return condition();
    }
}
