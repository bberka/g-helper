using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GHelper.App.Controls;

namespace GHelper.App.Services;

/// <summary>
/// Confirmation / alert as a modal layer inside the owning window instead of a separate window: a dimmed backdrop over
/// the window's content with a centred card (title, wrapped message, Yes / No or OK). Lives in the window's
/// <see cref="OverlayLayer"/>, so it works for the main window and every side window without touching their layout,
/// follows the theme (DynamicResource palette keys) and, being no window, cannot be tiled, snapped or lost behind.
/// <para>
/// Modal behaviour: the backdrop swallows pointer input, a tunnelling key handler on the owner keeps the keyboard in the
/// card (Tab / arrows move between the buttons, Enter activates, Esc answers No / OK), the previously focused element
/// gets the focus back when the card goes away. <see cref="Run"/> blocks the caller with a nested dispatcher frame,
/// like the legacy MessageBox.Show did.
/// </para>
/// </summary>
internal sealed class InAppDialog
{
    public const string OverlayName = "GHelperDialogOverlay";
    public const string CardName = "GHelperDialogCard";
    public const string YesName = "GHelperDialogYes";
    public const string NoName = "GHelperDialogNo";
    public const string OkName = "GHelperDialogOk";

    private readonly Window _owner;
    private readonly OverlayLayer _layer;
    private readonly bool _confirm;
    private readonly string _title;
    private readonly string _text;
    private readonly Grid _root;
    private readonly List<RButton> _buttons = new();
    private readonly List<IDisposable> _subscriptions = new();
    private readonly DispatcherFrame _frame = new();
    private IInputElement? _previousFocus;
    private bool _finished;

    public bool Result { get; private set; }

    private InAppDialog(Window owner, OverlayLayer layer, string title, string text, bool confirm)
    {
        _owner = owner;
        _layer = layer;
        _confirm = confirm;

        title = string.IsNullOrEmpty(title) ? "G-Helper" : title;
        _title = title;
        _text = text;

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Resource(titleBlock, TextBlock.ForegroundProperty, "ForeMainBrush");

        var message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        Resource(message, TextBlock.ForegroundProperty, "ForeMainBrush");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 18, 0, 0),
        };

        if (confirm)
        {
            var (yes, no) = DialogTexts.YesNo();
            buttons.Children.Add(AddButton(yes, YesName, true, secondary: false));
            buttons.Children.Add(AddButton(no, NoName, false, secondary: true));
        }
        else
        {
            buttons.Children.Add(AddButton(DialogTexts.Ok(), OkName, true, secondary: false));
        }

        var card = new Border
        {
            Name = CardName,
            MinWidth = 300,
            MaxWidth = 440,
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 24, OffsetY = 6, Color = Color.FromArgb(120, 0, 0, 0) }),
            Child = new StackPanel { Children = { titleBlock, message, buttons } },
        };
        Resource(card, Border.BackgroundProperty, "FormBackBrush");
        Resource(card, Border.BorderBrushProperty, "BorderMainBrush");
        AutomationProperties.SetName(card, title);
        AutomationProperties.SetAutomationId(card, CardName);
        AutomationProperties.SetName(message, text);

        _root = new Grid
        {
            Name = OverlayName,
            Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
            Focusable = false,
            Children = { card },
        };
        AutomationProperties.SetName(_root, title);
        AutomationProperties.SetAutomationId(_root, OverlayName);
        _root.PointerPressed += (_, e) => e.Handled = true;
        _root.PointerReleased += (_, e) => e.Handled = true;
        _root.PointerWheelChanged += (_, e) => e.Handled = true;
    }

    /// <summary>The first (default) button is the affirmative one: Yes / OK.</summary>
    private RButton AddButton(string text, string name, bool result, bool secondary)
    {
        var button = new RButton
        {
            Name = name,
            Content = text,
            MinWidth = 90,
            Height = 32,
            Secondary = secondary,
            IsTabStop = true,
        };
        AutomationProperties.SetName(button, text);
        AutomationProperties.SetAutomationId(button, name);
        button.Click += (_, _) => Finish(result);
        _buttons.Add(button);
        return button;
    }

    /// <summary>Binds a brush property to a theme resource so the card follows the light / dark switch.</summary>
    private static void Resource(AvaloniaObject target, AvaloniaProperty property, string key)
    {
        if (target is not StyledElement element) return;
        element.Bind(property, element.GetResourceObservable(key));
    }

    /// <summary>
    /// True when <paramref name="owner"/> can host the card (it is visible and has an overlay layer). Shows the dialog and
    /// waits for the answer; Yes / OK = true.
    /// </summary>
    public static bool TryRun(Window owner, string title, string text, bool confirm, out bool result)
    {
        result = false;
        if (!owner.IsVisible) return false;

        var layer = OverlayLayer.GetOverlayLayer(owner);
        if (layer is null) return false;

        var dialog = new InAppDialog(owner, layer, title, text, confirm);
        result = dialog.RunModal();
        return true;
    }

    private bool RunModal()
    {
        Open();
        Dispatcher.UIThread.PushFrame(_frame);
        return Result;
    }

    // ---- life cycle ----------------------------------------------------------------------------------------

    private void Open()
    {
        _previousFocus = TopLevel.GetTopLevel(_owner)?.FocusManager?.GetFocusedElement();

        // the overlay layer is a canvas: size the backdrop to it (and keep it that size)
        void Fit(Rect bounds)
        {
            var size = bounds.Width > 0 && bounds.Height > 0 ? bounds.Size : _owner.ClientSize;
            _root.Width = size.Width;
            _root.Height = size.Height;
        }

        Fit(_layer.Bounds);
        _subscriptions.Add(_layer.GetObservable(Visual.BoundsProperty).Subscribe(new ActionObserver<Rect>(Fit)));

        // the window was hidden (tray click, side window closed with the main window): the card would be invisible and
        // the caller blocked, so the question moves to the fallback window
        _subscriptions.Add(_owner.GetObservable(Visual.IsVisibleProperty).Subscribe(new ActionObserver<bool>(visible =>
        {
            if (!visible) Dispatcher.UIThread.Post(Rehome, DispatcherPriority.Send);
        })));

        _layer.Children.Add(_root);

        _owner.AddHandler(InputElement.KeyDownEvent, OnOwnerKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        _owner.Closed += OnOwnerClosed;

        FocusDefault();
        Dispatcher.UIThread.Post(FocusDefault, DispatcherPriority.Loaded);
    }

    private void OnOwnerClosed(object? sender, EventArgs e) => Finish(!_confirm);

    private void Rehome()
    {
        if (_finished || _owner.IsVisible) return;

        // take the card down, ask in a window instead; its answer is ours
        _layer.Children.Remove(_root);
        var window = new MessageDialog(_title, _text, _confirm);
        window.Closed += (_, _) => Finish(window.Result);
        window.Show();
    }

    private void Finish(bool result)
    {
        if (_finished) return;
        _finished = true;
        Result = result;

        _owner.RemoveHandler(InputElement.KeyDownEvent, OnOwnerKeyDown);
        _owner.Closed -= OnOwnerClosed;
        foreach (var s in _subscriptions) s.Dispose();
        _subscriptions.Clear();
        _layer.Children.Remove(_root);

        try { _previousFocus?.Focus(); }
        catch { /* the element may be gone */ }

        _frame.Continue = false;
    }

    // ---- keyboard ------------------------------------------------------------------------------------------

    private void FocusDefault() => _buttons[0].Focus();

    private int FocusedIndex()
    {
        var focused = TopLevel.GetTopLevel(_owner)?.FocusManager?.GetFocusedElement();
        return focused is RButton b ? _buttons.IndexOf(b) : -1;
    }

    private void OnOwnerKeyDown(object? sender, KeyEventArgs e)
    {
        if (_finished) return;

        int index = FocusedIndex();

        switch (e.Key)
        {
            case Key.Escape:
                Finish(!_confirm);
                break;

            case Key.Enter:
                // Enter / Space on a focused button is that button, anywhere else the default (Yes / OK)
                Finish(index < 0 ? true : index == 0);
                break;

            case Key.Space:
                Finish(index < 0 ? true : index == 0);
                break;

            case Key.Tab:
            case Key.Left:
            case Key.Right:
                if (_buttons.Count > 1)
                {
                    bool back = e.Key == Key.Left || (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                    int next = index < 0 ? 0 : (index + (back ? _buttons.Count - 1 : 1)) % _buttons.Count;
                    _buttons[next].Focus();
                }
                else
                {
                    FocusDefault();
                }
                break;

            default:
                // nothing reaches the window behind the card; keep the focus inside it
                if (index < 0) FocusDefault();
                break;
        }

        e.Handled = true;
    }

    private sealed class ActionObserver<T> : IObserver<T>
    {
        private readonly Action<T> _next;
        public ActionObserver(Action<T> next) => _next = next;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => _next(value);
    }
}

/// <summary>
/// "Yes" / "No" / "OK" in the UI language. The legacy MessageBox let Windows localize them; the resource files carry
/// no such strings, so the common languages of the app are listed here.
/// </summary>
internal static class DialogTexts
{
    private static readonly Dictionary<string, (string Yes, string No, string Ok)> Texts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = ("Yes", "No", "OK"),
        ["ar"] = ("نعم", "لا", "موافق"),
        ["cs"] = ("Ano", "Ne", "OK"),
        ["da"] = ("Ja", "Nej", "OK"),
        ["de"] = ("Ja", "Nein", "OK"),
        ["es"] = ("Sí", "No", "Aceptar"),
        ["fr"] = ("Oui", "Non", "OK"),
        ["hu"] = ("Igen", "Nem", "OK"),
        ["id"] = ("Ya", "Tidak", "OK"),
        ["it"] = ("Sì", "No", "OK"),
        ["ja"] = ("はい", "いいえ", "OK"),
        ["ko"] = ("예", "아니요", "확인"),
        ["lt"] = ("Taip", "Ne", "Gerai"),
        ["pl"] = ("Tak", "Nie", "OK"),
        ["pt"] = ("Sim", "Não", "OK"),
        ["ro"] = ("Da", "Nu", "OK"),
        ["tr"] = ("Evet", "Hayır", "Tamam"),
        ["uk"] = ("Так", "Ні", "OK"),
        ["vi"] = ("Có", "Không", "OK"),
        ["zh"] = ("是", "否", "确定"),
    };

    private static (string Yes, string No, string Ok) Current
    {
        get
        {
            string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (CultureInfo.CurrentUICulture.Name.Equals("zh-TW", StringComparison.OrdinalIgnoreCase))
                return ("是", "否", "確定");
            return Texts.TryGetValue(lang, out var t) ? t : Texts["en"];
        }
    }

    public static (string Yes, string No) YesNo() => (Current.Yes, Current.No);

    public static string Ok() => Current.Ok;
}
