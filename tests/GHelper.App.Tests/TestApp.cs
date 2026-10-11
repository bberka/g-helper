using Avalonia;
using Avalonia.Headless;
using GHelper.App;

[assembly: AvaloniaTestApplication(typeof(GHelper.App.Tests.TestAppBuilder))]

namespace GHelper.App.Tests;

/// <summary>
/// Headless Avalonia with the real Skia renderer, so windows can be rendered to PNG (CaptureRenderedFrame) and
/// inspected. Uses the production <see cref="App"/>: theme palette and control styles come from App.axaml.
/// </summary>
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<GHelper.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
