using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using GHelper.App.Services;
using Xunit;

namespace GHelper.App.Tests.Layout;

/// <summary>
/// Layout invariants of every window (see <see cref="LayoutCases"/>): fields that share a row are the same height, nothing
/// sticks out of its parent, check boxes are fully visible, text is not cut off - in both themes, at render scaling 1 and 2
/// and in the languages with the longest strings. PNGs of every window are written next to the other snapshots
/// ("layout-*.png") for review by eye.
/// </summary>
public class LayoutTests
{
    public static IEnumerable<object[]> Names => LayoutCases.Names;

    /// <summary>Languages with the longest UI strings (German compounds, Ukrainian, French, Hungarian, Spanish, Polish).</summary>
    public static readonly string[] LongCultures = { "en", "de", "fr", "uk", "hu", "es", "pl", "pt-BR", "ro" };

    private static void UseTheme(ThemeVariant theme) => Application.Current!.RequestedThemeVariant = theme;

    private static string Report(string name, IEnumerable<string> problems) =>
        name + ":\n  " + string.Join("\n  ", problems.Distinct());

    private static async Task<List<string>> Run(string name, double scaling, Func<Window, List<string>> probe,
        ThemeVariant? theme = null)
    {
        UseTheme(theme ?? ThemeVariant.Dark);
        var window = LayoutCases.Get(name).Create();
        try
        {
            await LayoutProbe.Open(window, scaling);
            return probe(window);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Every_registered_window_has_a_layout_case()
    {
        var covered = LayoutCases.All.Where(c => c.Registry is not null).Select(c => c.Registry!).ToHashSet();
        foreach (string registered in LayoutCases.RegistryNames)
        {
            Assert.True(WindowRegistry.IsRegistered(registered), registered + " is not registered");
            Assert.Contains(registered, covered);
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Names))]
    public async Task Fields_in_one_row_have_the_same_height(string name)
    {
        foreach (double scaling in new[] { 1.0, 2.0 })
        {
            var problems = await Run(name, scaling, LayoutProbe.UnequalRows);
            Assert.True(problems.Count == 0, $"scale {scaling}: " + Report(name, problems));
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Names))]
    public async Task Nothing_sticks_out_of_its_parent(string name)
    {
        foreach (double scaling in new[] { 1.0, 2.0 })
        {
            var problems = await Run(name, scaling, LayoutProbe.Clipped);
            Assert.True(problems.Count == 0, $"scale {scaling}: " + Report(name, problems));
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Names))]
    public async Task Interactive_rows_do_not_touch(string name)
    {
        foreach (double scaling in new[] { 1.0, 2.0 })
        {
            var problems = await Run(name, scaling, LayoutProbe.TouchingRows);
            Assert.True(problems.Count == 0, $"scale {scaling}: " + Report(name, problems));
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Names))]
    public async Task Check_boxes_are_fully_visible(string name)
    {
        foreach (double scaling in new[] { 1.0, 2.0 })
        {
            var problems = await Run(name, scaling, LayoutProbe.CheckBoxes);
            Assert.True(problems.Count == 0, $"scale {scaling}: " + Report(name, problems));
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Names))]
    public async Task Text_is_not_cut_off_in_long_languages(string name)
    {
        var original = Thread.CurrentThread.CurrentUICulture;
        var all = new List<string>();
        try
        {
            foreach (string culture in LongCultures)
            {
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                var problems = await Run(name, 1, LayoutProbe.TruncatedText);
                all.AddRange(problems.Select(p => culture + ": " + p));
            }
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = original;
        }

        Assert.True(all.Count == 0, Report(name, all));
    }

    [AvaloniaTheory]
    [InlineData("fans-cpu")]
    [InlineData("fans-advanced")]
    [InlineData("extra")]
    [InlineData("keyboard")]
    [InlineData("mouse")]
    [InlineData("headset")]
    public async Task Content_scrolls_when_the_screen_is_shorter_than_the_window(string name)
    {
        UseTheme(ThemeVariant.Dark);
        var window = LayoutCases.Get(name).Create();
        try
        {
            await LayoutProbe.Open(window);
            // a work area shorter than the content: the window is limited (its Opened handlers have run by now)
            window.SizeToContent = SizeToContent.Manual;
            window.MaxHeight = 360;
            window.Height = 360;
            await LayoutProbe.Settle(window);

            Assert.True(window.Bounds.Height <= 360.5, name + " window height " + window.Bounds.Height);
            var scroll = LayoutProbe.RootScroll(window);
            Assert.NotNull(scroll);
            Assert.True(scroll!.Extent.Height > scroll.Viewport.Height + 1,
                $"{name}: extent {scroll.Extent.Height} viewport {scroll.Viewport.Height}");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Names))]
    public async Task Render_png_snapshots(string name)
    {
        foreach (var (theme, label) in new[] { (ThemeVariant.Dark, "dark"), (ThemeVariant.Light, "light") })
        foreach (double scaling in new[] { 1.0, 2.0 })
        {
            UseTheme(theme);
            var window = LayoutCases.Get(name).Create();
            try
            {
                await LayoutProbe.Open(window, scaling);
                var frame = MainWindowSnapshotTests.Render(window, $"layout-{name}-{label}-{scaling:0}x");
                Assert.True(frame.PixelSize.Width > 100 && frame.PixelSize.Height > 50);
            }
            finally
            {
                window.Close();
            }
        }
    }
}
