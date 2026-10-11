using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GHelper.App.Controls;
using GHelper.App.Theme;
using GHelper.App.Views.Updates;

namespace GHelper.App.Tests.Layout;

/// <summary>Walks a laid out window and reports layout defects (unequal field heights, clipping, truncated text).</summary>
internal static class LayoutProbe
{
    private const double Tolerance = 0.51;

    // ------------------------------------------------------------------ driving a window

    /// <summary>Shows the window, lets async start-up work finish and runs layout at the given render scaling.</summary>
    public static async Task Open(Window window, double scaling = 1)
    {
        window.Show();
        if (scaling != 1) SetScaling(window, scaling);
        await Settle(window);
    }

    public static async Task Settle(Window window)
    {
        if (window is UpdatesWindow updates)
        {
            updates.LoadUpdates(force: true);
            for (int i = 0; i < 300 && !updates.LoadTask.IsCompleted; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
        }

        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(40);
        }

        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    /// <summary>Headless windows expose the render scaling through the explicit IHeadlessWindow member.</summary>
    public static void SetScaling(Window window, double scaling)
    {
        var impl = window.PlatformImpl ?? throw new InvalidOperationException("window not shown");
        var method = impl.GetType().GetMethod("Avalonia.Headless.IHeadlessWindow.SetRenderScaling",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("headless scaling not available");
        method.Invoke(impl, new object[] { scaling });
        Dispatcher.UIThread.RunJobs();
    }

    // ------------------------------------------------------------------ tree helpers

    public static bool IsShown(Visual v)
    {
        for (Visual? x = v; x is not null; x = x.GetVisualParent())
        {
            if (!x.IsVisible) return false;
            if (x is Layoutable l && x is not Window && (l.Bounds.Width <= 0 || l.Bounds.Height <= 0)) return false;
        }
        return true;
    }

    public static IEnumerable<T> Shown<T>(Visual root) where T : Visual =>
        root.GetVisualDescendants().OfType<T>().Where(IsShown);

    private static bool InTemplate(Visual v) => v is StyledElement { TemplatedParent: not null };

    public static bool IsField(Visual v) => v is RButton or RComboBox or RTextBox or RNumericUpDown;

    public static string Describe(Visual v)
    {
        string name = (v as StyledElement)?.Name;
        string text = v switch
        {
            RButton b => " '" + (b.Content as string ?? b.Content?.GetType().Name) + "'",
            TextBlock t => " '" + t.Text + "'",
            ContentControl c => " '" + c.Content + "'",
            _ => "",
        };
        string path = "";
        int depth = 0;
        for (Visual? p = v.GetVisualParent(); p is not null && depth < 3; p = p.GetVisualParent(), depth++)
            path += "<" + p.GetType().Name + (p is StyledElement { Name: { Length: > 0 } n } ? "#" + n : "");
        return v.GetType().Name + (string.IsNullOrEmpty(name) ? "" : "#" + name) + text + path +
               (v is Layoutable l ? $" [{l.Bounds.X:0.#},{l.Bounds.Y:0.#} {l.Bounds.Width:0.#}x{l.Bounds.Height:0.#}]" : "");
    }

    // ------------------------------------------------------------------ 1: equal field heights per row

    public static List<string> UnequalRows(Window window)
    {
        var problems = new List<string>();
        var fields = Shown<Control>(window).Where(v => IsField(v) && !InTemplate(v) && !v.Classes.Contains("flag")).ToList();

        foreach (var group in fields.GroupBy(f => f.GetVisualParent()))
        {
            var ordered = group.OrderBy(f => f.Bounds.Y).ToList();
            var rows = new List<List<Control>>();
            foreach (var f in ordered)
            {
                var row = rows.FirstOrDefault(r => r.Any(o => VerticalOverlap(o.Bounds, f.Bounds) > 0.5));
                if (row is null) rows.Add(new List<Control> { f });
                else row.Add(f);
            }

            foreach (var row in rows.Where(r => r.Count > 1))
            {
                double min = row.Min(f => f.Bounds.Height), max = row.Max(f => f.Bounds.Height);
                if (max - min > Tolerance)
                    problems.Add("unequal heights " + string.Join(" | ", row.Select(Describe)));
            }
        }

        return problems;
    }

    private static double VerticalOverlap(Rect a, Rect b)
    {
        double overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        double smaller = Math.Min(a.Height, b.Height);
        return smaller <= 0 ? 0 : Math.Max(0, overlap) / smaller;
    }

    // ------------------------------------------------------------------ 2: clipping

    public static List<string> Clipped(Window window)
    {
        var problems = new List<string>();
        foreach (var c in Shown<Control>(window))
        {
            if (InTemplate(c)) continue;
            if (c.GetVisualParent() is not Control parent) continue;
            if (parent is Canvas or ScrollContentPresenter) continue;
            if (c is Popup || parent is Popup) continue;

            var rect = c.Bounds;
            var allowed = new Rect(0, 0, parent.Bounds.Width, parent.Bounds.Height).Inflate(Tolerance);
            if (!allowed.Contains(rect.TopLeft) || !allowed.Contains(rect.BottomRight))
                problems.Add("exceeds parent (" + $"{parent.Bounds.Width:0.#}x{parent.Bounds.Height:0.#}" + ") " + Describe(c));
        }

        return problems;
    }

    // ------------------------------------------------------------------ 3: check boxes

    public static List<string> CheckBoxes(Window window)
    {
        var problems = new List<string>();
        foreach (var box in Shown<RCheckBox>(window))
        {
            if (box.Bounds.Height + Tolerance < UiMetrics.CheckRowHeight)
                problems.Add($"check row {box.Bounds.Height:0.#} < {UiMetrics.CheckRowHeight} " + Describe(box));

            var square = box.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "NormalRectangle");
            if (square is null)
            {
                problems.Add("no check square " + Describe(box));
                continue;
            }

            if (square.Bounds.Width + Tolerance < UiMetrics.CheckBoxSize || square.Bounds.Height + Tolerance < UiMetrics.CheckBoxSize)
                problems.Add($"check square {square.Bounds.Width:0.#}x{square.Bounds.Height:0.#} < {UiMetrics.CheckBoxSize} " + Describe(box));

            // the square must lie inside the control and every ancestor up to the window
            var topLeft = square.TranslatePoint(new Point(0, 0), box);
            var bottomRight = square.TranslatePoint(new Point(square.Bounds.Width, square.Bounds.Height), box);
            var inside = new Rect(0, 0, box.Bounds.Width, box.Bounds.Height).Inflate(Tolerance);
            if (topLeft is null || bottomRight is null || !inside.Contains(topLeft.Value) || !inside.Contains(bottomRight.Value))
                problems.Add("check square outside its control " + Describe(box));
        }

        return problems;
    }

    // ------------------------------------------------------------------ 4: truncated / clipped text

    public static List<string> TruncatedText(Window window)
    {
        var problems = new List<string>();
        foreach (var tb in Shown<TextBlock>(window))
        {
            string? text = tb.Text;
            if (string.IsNullOrEmpty(text) || tb.Inlines is { Count: > 0 }) continue;
            if (tb.GetVisualParent() is null) continue;
            // text boxes of the shell (combo box selection ...) are templated: the control reports itself
            if (tb is SelectableTextBlock) continue;

            var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, tb.FontSize,
                Brushes.Black);

            double width = tb.Bounds.Width, height = tb.Bounds.Height;
            if (tb.TextWrapping == TextWrapping.NoWrap)
            {
                double needed = formatted.WidthIncludingTrailingWhitespace;
                // multi-line text with explicit line breaks: widest line counts
                if (needed > width + 1.0)
                    problems.Add($"text cut {needed:0.#} > {width:0.#}: " + Describe(tb));
            }
            else
            {
                formatted.MaxTextWidth = Math.Max(1, width);
                if (formatted.Height > height + 1.0)
                    problems.Add($"wrapped text cut {formatted.Height:0.#} > {height:0.#}: " + Describe(tb));
            }
        }

        return problems;
    }

    // ------------------------------------------------------------------ misc

    /// <summary>First ScrollViewer on the way down from the window content (null = content is not scrollable).</summary>
    public static ScrollViewer? RootScroll(Window window) =>
        window.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => !InTemplate(s));
}
