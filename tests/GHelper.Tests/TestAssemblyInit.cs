using System.Runtime.CompilerServices;
using GHelper.Platform;
using GHelper.Tests.Hw;

// Core keeps process wide static state, the suites swap fake platforms in and out: run them one after another.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace GHelper.Tests;

/// <summary>
/// Core's statics (AppConfig, Program, Logger) read PlatformServices.Current in their type initializers, and a type
/// initializer that throws poisons the type for the whole process. The test classes run in parallel, so any test that
/// touched one of them before another test registered a platform made ~200 unrelated tests fail. Register the shared
/// fake platform before any test code runs.
/// </summary>
internal static class TestAssemblyInit
{
    [ModuleInitializer]
    internal static void RegisterFakePlatform()
    {
        if (!PlatformServices.IsRegistered) HwTestPlatform.Ensure();
    }
}
