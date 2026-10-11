using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GHelper.App.Controls;
using GHelper.App.Services;
using GHelper.App.Theme;
using GHelper.Platform;
using GHelper.Properties;

namespace GHelper.App.Views.Updates;

internal static class UpdatesWindowRegistration
{
    [ModuleInitializer]
    internal static void Register() => WindowRegistry.Register("updates", _ => new UpdatesWindow());
}

/// <summary>What the window needs to know about this laptop (BIOS version, model and whether it is a ROG).</summary>
public readonly record struct UpdatesTarget(string Bios, string Model, bool IsRog)
{
    /// <summary>Legacy LoadUpdates: model short name, NUC boards use the full short name.</summary>
    public static UpdatesTarget FromConfig()
    {
        var (bios, model) = AppConfig.GetBiosAndModel();
        if (AppConfig.ContainsModel("NUC")) model = AppConfig.GetModelShort();
        return new UpdatesTarget(bios, model, AppConfig.IsROG());
    }

    public string BiosUrl => $"https://rog.asus.com/support/webapi/product/GetPDBIOS?website=global&model={Model}&cpu={Model}{RogParam}";

    public string DriversUrl => $"https://rog.asus.com/support/webapi/product/GetPDDrivers?website=global&model={Model}&cpu={Model}&osid=52{RogParam}";

    private string RogParam => IsRog ? "&systemCode=rog" : "";
}

/// <summary>
/// BIOS and driver updates (legacy Updates form): two tables fed by <see cref="UpdatesController"/> with the available
/// version, its date and a download link coloured by the comparison against what is installed, a status line with the
/// number of new updates, a refresh button, the serial number and the colour legend.
/// </summary>
public sealed class UpdatesWindow : SideWindow
{
    internal const string SymbolUpdated = "•";
    internal const string SymbolNew = "⬤";

    private static long _lastUpdate;

    private readonly UpdatesController _controller;
    private readonly Func<UpdatesTarget> _target;
    private readonly Func<string> _serial;
    private readonly Action<string> _openUrl;

    private readonly TextBlock _titleBios = new() { Text = "BIOS", Classes = { "title" } };
    private readonly TextBlock _titleDrivers = new() { Classes = { "title" } };
    private readonly TextBlock _labelUpdates = new() { FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly RButton _buttonRefresh = new() { Secondary = true, Width = 52, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly SelectableTextBlock _textSerial = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _legendTitle = new() { FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _serialTitle = new() { FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _legendGray = new();
    private readonly TextBlock _legendGreen = new();
    private readonly TextBlock _legendRed = new();
    private readonly UpdateTable _tableBios = new();
    private readonly UpdateTable _tableDrivers = new();

    private CancellationTokenSource _cts = new();
    private int _updatesCount;
    private string _bios = "";

    /// <summary>Completes when the last started refresh finished (tests).</summary>
    internal Task LoadTask { get; private set; } = Task.CompletedTask;

    public UpdatesWindow() : this(
        new UpdatesController(),
        UpdatesTarget.FromConfig,
        () => new UpdatesController().GetSerialNumber(),
        OpenInBrowser)
    {
    }

    internal UpdatesWindow(UpdatesController controller, Func<UpdatesTarget> target, Func<string> serial, Action<string> openUrl,
        bool autoLoad = true)
    {
        _controller = controller;
        _target = target;
        _serial = serial;
        _openUrl = openUrl;

        Width = 660;
        MinWidth = 660;
        Height = App.Instance?.MainWindow is { Bounds.Height: > 0 } main ? main.Bounds.Height : 660;

        Content = BuildContent();

        _buttonRefresh.Click += (_, _) => LoadUpdates();
        Closed += (_, _) =>
        {
            _cts.Cancel();
            _cts.Dispose();
        };

        if (autoLoad) Opened += (_, _) => LoadUpdates(force: true);
    }

    private static void OpenInBrowser(string url)
    {
        // the link comes from a web API: never hand anything else than a web address to the shell
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return;
        try { PlatformServices.Current.Process.OpenUrl(uri.AbsoluteUri); }
        catch (Exception ex) { AppLog.Write("Open update link: " + ex.Message); }
    }

    // ---- layout ------------------------------------------------------------------------------------------

    private static Bitmap? LoadIcon(string file)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://GHelper/Assets/Icons/{file}"));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static Grid ColumnGrid() => new() { ColumnDefinitions = new ColumnDefinitions("23*,40*,15*,44,22*") };

    private Control SectionTitle(string iconFile, TextBlock title, bool withStatus)
    {
        var grid = ColumnGrid();
        grid.Margin = new Thickness(10, withStatus ? 10 : 4, 10, 2);
        grid.MinHeight = 40;

        var title2 = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TintedImage { Source = LoadIcon(iconFile), Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center }, title },
        };
        Grid.SetColumnSpan(title2, 2);
        grid.Children.Add(title2);

        if (withStatus)
        {
            // status line sits above the date column like the legacy label
            Grid.SetColumn(_labelUpdates, 2);
            Grid.SetColumnSpan(_labelUpdates, 2);
            grid.Children.Add(_labelUpdates);

            Grid.SetColumn(_buttonRefresh, 4);
            _buttonRefresh.Icon = LoadIcon("icons8-refresh-32.png");
            AutomationProperties.SetName(_buttonRefresh, "Refresh");
            grid.Children.Add(_buttonRefresh);
        }

        return grid;
    }

    private Control BuildContent()
    {
        _tableBios.Grid.Margin = new Thickness(10, 0);
        _tableDrivers.Grid.Margin = new Thickness(10, 0);

        var lists = new StackPanel
        {
            Children =
            {
                SectionTitle("icons8-processor-32.png", _titleBios, withStatus: true),
                _tableBios.Grid,
                SectionTitle("icons8-software-32.png", _titleDrivers, withStatus: false),
                _tableDrivers.Grid,
            },
        };
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = lists,
        };

        foreach (var label in new[] { _legendGray, _legendGreen, _legendRed })
        {
            // fixed-contrast chips: white text on the status colour in both themes
            label.Foreground = Brushes.White;
            label.Padding = new Thickness(6, 4);
            label.TextWrapping = TextWrapping.Wrap;
            label.VerticalAlignment = VerticalAlignment.Center;
        }

        var legend = new Grid
        {
            Margin = new Thickness(10, 6, 10, 14),
            ColumnDefinitions = new ColumnDefinitions("15*,28*,28*,28*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 8,
        };
        legend.Children.Add(_serialTitle);
        var serialBox = new Border
        {
            Child = _textSerial,
            Padding = new Thickness(8, 0),
            MinHeight = UiMetrics.ControlHeight,
            Margin = new Thickness(5, 0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
        };
        serialBox.Bind(Border.BackgroundProperty, serialBox.GetResourceObservable("InputBackBrush"));
        serialBox.Bind(Border.BorderBrushProperty, serialBox.GetResourceObservable("BorderMainBrush"));
        AutomationProperties.SetName(_textSerial, "Serial");
        Grid.SetColumn(serialBox, 1);
        Grid.SetColumnSpan(serialBox, 3);
        legend.Children.Add(serialBox);

        Grid.SetRow(_legendTitle, 1);
        legend.Children.Add(_legendTitle);
        AddChip(legend, _legendGray, 1, AppColors.Gray);
        AddChip(legend, _legendGreen, 2, AppColors.Eco);
        AddChip(legend, _legendRed, 3, AppColors.Turbo);

        _serialTitle.Text = "Serial";
        _titleDrivers.Text = Strings.DriverAndSoftware;
        _legendTitle.Text = Strings.Legend;
        _legendGray.Text = Strings.LegendGray;
        _legendGreen.Text = SymbolUpdated + " " + Strings.LegendGreen;
        _legendRed.Text = SymbolNew + " " + Strings.LegendRed;
        _labelUpdates.Text = Strings.NoNewUpdates;
        _labelUpdates.Foreground = new SolidColorBrush(AppColors.Eco);
        Title = Strings.BiosAndDriverUpdates;

        var root = new DockPanel();
        DockPanel.SetDock(legend, Dock.Bottom);
        root.Children.Add(legend);
        root.Children.Add(scroll);
        return root;
    }

    private static void AddChip(Grid grid, TextBlock label, int column, Color color)
    {
        var chip = new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(UiMetrics.Radius),
            MinHeight = UiMetrics.ControlHeight,
            Margin = new Thickness(5, 0),
            Child = label,
        };
        Grid.SetRow(chip, 1);
        Grid.SetColumn(chip, column);
        grid.Children.Add(chip);
    }

    // ---- loading -----------------------------------------------------------------------------------------

    /// <summary>Port of LoadUpdates: clear both tables and fetch BIOS + drivers (not more often than every 5 s unless forced).</summary>
    public void LoadUpdates(bool force = false)
    {
        long now = Environment.TickCount64;
        if (!force && Math.Abs(now - _lastUpdate) < 5000) return;
        _lastUpdate = now;

        var target = _target();
        _bios = target.Bios;

        _updatesCount = 0;
        _labelUpdates.Text = Strings.NoNewUpdates;
        _labelUpdates.Foreground = new SolidColorBrush(AppColors.Eco);
        _labelUpdates.TextDecorations = null;
        AutomationProperties.SetName(_tableBios.Grid, Strings.NoNewUpdates);

        Title = Strings.BiosAndDriverUpdates + ": " + target.Model + " " + target.Bios;

        _tableBios.Clear();
        _tableDrivers.Clear();

        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var bios = LoadTable(target.BiosUrl, 1, _tableBios, token);
        var drivers = LoadTable(target.DriversUrl, 0, _tableDrivers, token);
        var serial = LoadSerial(token);
        LoadTask = Task.WhenAll(bios, drivers, serial);
    }

    private async Task LoadSerial(CancellationToken token)
    {
        try
        {
            string serial = await Task.Run(_serial, token);
            if (!token.IsCancellationRequested) _textSerial.Text = serial;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write(ex.ToString()); }
    }

    private async Task LoadTable(string url, int type, UpdateTable table, CancellationToken token)
    {
        try
        {
            var updates = await Task.Run(() => _controller.FetchUpdates(url, token), token);
            if (token.IsCancellationRequested) return;
            RenderTable(updates, table);

            string bios = _bios;
            await Task.Run(() => _controller.ResolveStatus(updates, type, bios, token), token);
            if (!token.IsCancellationRequested) ApplyStatus(updates, table);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write(ex.ToString()); }
    }

    private void RenderTable(List<UpdatesController.DriverUpdate> updates, UpdateTable table)
    {
        foreach (var update in updates)
            AddDriverRow(update, table);
    }

    private void ApplyStatus(List<UpdatesController.DriverUpdate> updates, UpdateTable table)
    {
        for (int i = 0; i < updates.Count && i < table.Rows.Count; i++)
            SetDriverStatus(table.Rows[i], updates[i].status, updates[i].tip);

        int newCount = updates.Count(u => u.status == UpdatesController.STATUS_NEW);
        if (newCount > 0)
        {
            _updatesCount += newCount;
            _labelUpdates.Text = $"{Strings.NewUpdates}: {_updatesCount}";
            _labelUpdates.Foreground = new SolidColorBrush(AppColors.Turbo);
            _labelUpdates.TextDecorations = TextDecorations.Underline;
            AutomationProperties.SetName(_tableBios.Grid, _labelUpdates.Text);
        }
    }

    // ---- rows ----------------------------------------------------------------------------------------------

    private static TextBlock Cell(string text, int column, int row, Grid grid)
    {
        var label = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(5, 5),
        };
        ToolTip.SetTip(label, text);
        Grid.SetColumn(label, column);
        Grid.SetRow(label, row);
        grid.Children.Add(label);
        return label;
    }

    private void AddDriverRow(UpdatesController.DriverUpdate driver, UpdateTable table)
    {
        var grid = table.Grid;
        int row = table.Rows.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var category = Cell(driver.categoryName, 0, row, grid);
        var title = Cell(driver.title, 1, row, grid);
        var date = Cell(driver.date, 2, row, grid);

        var symbol = new TextBlock
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0, 5, 4, 5),
        };
        Grid.SetColumn(symbol, 3);
        Grid.SetRow(symbol, row);
        grid.Children.Add(symbol);

        var link = new LinkLabel
        {
            Text = driver.version.Replace("latest version at the ", ""),
            Foreground = new SolidColorBrush(AppColors.Eco),
            Padding = new Thickness(0, 5, 5, 5),
            Url = driver.downloadUrl,
        };
        AutomationProperties.SetName(link, driver.title);
        link.Activated += () => _openUrl(driver.downloadUrl);
        Grid.SetColumn(link, 4);
        Grid.SetRow(link, row);
        grid.Children.Add(link);

        table.Rows.Add(new UpdateRow(category, title, date, symbol, link));
    }

    private void SetDriverStatus(UpdateRow row, int status, string tip)
    {
        if (status == UpdatesController.STATUS_HIDDEN)
        {
            foreach (var c in row.All) c.IsVisible = false;
            return;
        }

        ToolTip.SetTip(row.Link, tip);

        if (status == UpdatesController.STATUS_NEW)
        {
            AutomationProperties.SetName(row.Link, row.Title.Text + " " + Strings.NewUpdates);
            row.Link.FontWeight = FontWeight.Bold;
            row.Link.Foreground = new SolidColorBrush(AppColors.Turbo);
            row.Symbol.Text = SymbolNew;
            row.Symbol.Foreground = new SolidColorBrush(AppColors.Turbo);
        }
        else if (status == UpdatesController.STATUS_NOT_FOUND)
        {
            row.Link.Foreground = new SolidColorBrush(Colors.Gray);
        }
        else
        {
            row.Symbol.Text = SymbolUpdated;
            row.Symbol.Foreground = new SolidColorBrush(AppColors.Eco);
        }
    }

    // ---- parts --------------------------------------------------------------------------------------------

    internal sealed class UpdateTable
    {
        public Grid Grid { get; } = ColumnGrid();
        public List<UpdateRow> Rows { get; } = new();

        public void Clear()
        {
            Grid.Children.Clear();
            Grid.RowDefinitions.Clear();
            Rows.Clear();
        }
    }

    internal sealed record UpdateRow(TextBlock Category, TextBlock Title, TextBlock Date, TextBlock Symbol, LinkLabel Link)
    {
        public IEnumerable<Control> All => new Control[] { Category, Title, Date, Symbol, Link };
    }

    /// <summary>Underlined, focusable text that opens a download page on click / Enter / Space.</summary>
    internal sealed class LinkLabel : TextBlock
    {
        public string Url { get; set; } = "";

        public event Action? Activated;

        public LinkLabel()
        {
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Hand);
            TextDecorations = Avalonia.Media.TextDecorations.Underline;
            TextWrapping = TextWrapping.Wrap;
            VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetAccessibilityView(this, AccessibilityView.Content);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new LinkLabelPeer(this);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                Focus();
                Activated?.Invoke();
                e.Handled = true;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key is Key.Enter or Key.Space)
            {
                Activated?.Invoke();
                e.Handled = true;
            }
        }

        private sealed class LinkLabelPeer : ControlAutomationPeer
        {
            public LinkLabelPeer(LinkLabel owner) : base(owner) { }
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Hyperlink;
            protected override string GetClassNameCore() => nameof(LinkLabel);
            protected override bool IsKeyboardFocusableCore() => true;
        }
    }
}
