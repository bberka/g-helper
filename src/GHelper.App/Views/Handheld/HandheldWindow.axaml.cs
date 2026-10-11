using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using SkiaSharp;
using GHelper.Ally;
using GHelper.App.Controls;
using GHelper.App.Services;
using GHelper.App.Theme;
using GHelper.Properties;

namespace GHelper.App.Views;

/// <summary>What the controller window asks the hardware layer to do (AllyControl in production, a recorder in tests).</summary>
internal interface IControllerActions
{
    void SetDeadzones();
    void ApplyMode();
    void DisableController(bool disabled);
    void SetVibrationMode();
}

internal sealed class AllyControllerActions : IControllerActions
{
    public void SetDeadzones() => AllyControl.SetDeadzones();
    public void ApplyMode() => AllyControl.ApplyMode();
    public void DisableController(bool disabled) => AllyControl.DisableXBoxController(disabled);
    public void SetVibrationMode() => AllyControl.SetVibrationMode();
}

/// <summary>
/// ROG Ally controller window (legacy Handheld form): deadzones, vibration, "disable controller" and the per button
/// primary / secondary bindings with their repeat rate. Bindings are stored as "bind_x", "bind2_x", "turbo_x",
/// "turbo2_x" and applied through <see cref="AllyControl"/>.
/// </summary>
public partial class HandheldWindow : SideWindow
{
    private static readonly int[] TurboValues = { 0, 50, 100, 150, 200, 250, 300, 400, 500 };

    private readonly IControllerActions _actions;
    private readonly Dictionary<RButton, string> _bindings = new();
    private string _activeBinding = "";
    private RButton? _activeButton;
    private bool _updatingBindings;
    private bool _loading = true;

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void RegisterWindow() =>
        WindowRegistry.Register("handheld", arg => new HandheldWindow(arg));

    public HandheldWindow() : this(0)
    {
    }

    public HandheldWindow(int arg) : this(new AllyControllerActions())
    {
    }

    internal HandheldWindow(IControllerActions actions)
    {
        _actions = actions;
        InitializeComponent();

        Title = Strings.Controller;

        LabelLSTitle.Text = Strings.LSDeadzones;
        LabelRSTitle.Text = Strings.RSDeadzones;
        LabelLTTitle.Text = Strings.LTDeadzones;
        LabelRTTitle.Text = Strings.RTDeadzones;
        LabelVibraTitle.Text = Strings.VibrationStrength;
        CheckController.Content = Strings.DisableController;
        CheckVibra.Content = Strings.EnhancedVibration;
        ButtonReset.Content = Strings.Reset;

        LabelPrimary.Text = Strings.BindingPrimary;
        LabelSecondary.Text = Strings.BindingSecondary;

        Init();

        foreach (var slider in new[]
                 {
                     TrackLSMin, TrackLSMax, TrackRSMin, TrackRSMax,
                     TrackLTMin, TrackLTMax, TrackRTMin, TrackRTMax, TrackVibra,
                 })
        {
            slider.ValueChanged += (_, _) => { if (!_loading) VisualiseController(); };
            slider.ValueCommitted += Controller_Complete;
        }

        ButtonReset.Click += (_, _) => ResetController();

        ButtonBinding("m1", "M1", ButtonM1);
        ButtonBinding("m2", "M2", ButtonM2);

        ButtonBinding("a", "A", ButtonA);
        ButtonBinding("b", "B", ButtonB);
        ButtonBinding("x", "X", ButtonX);
        ButtonBinding("y", "Y", ButtonY);

        ButtonBinding("du", "DPad Up", ButtonDPU);
        ButtonBinding("dd", "DPad Down", ButtonDPD);

        ButtonBinding("dl", "DPad Left", ButtonDPL);
        ButtonBinding("dr", "DPad Right", ButtonDPR);

        ButtonBinding("rt", "Right Trigger", ButtonRT);
        ButtonBinding("lt", "Left Trigger", ButtonLT);

        ButtonBinding("rb", "Right Bumper", ButtonRB);
        ButtonBinding("lb", "Left Bumper", ButtonLB);

        ButtonBinding("rs", "Right Stick", ButtonRS);
        ButtonBinding("ll", "Left Stick", ButtonLS);

        ButtonBinding("vb", "View", ButtonView);
        ButtonBinding("mb", "Menu", ButtonMenu);

        InitBindingCombo(ComboPrimary);
        InitBindingCombo(ComboSecondary);

        InitTurboCombo(ComboTurboPrimary);
        InitTurboCombo(ComboTurboSecondary);

        CheckController.IsChecked = AppConfig.Is("controller_disabled");
        CheckController.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            bool disabled = CheckController.IsChecked == true;
            AppConfig.Set("controller_disabled", disabled ? 1 : 0);
            _actions.DisableController(disabled);
        };

        CheckVibra.IsVisible = AppConfig.IsXboxAlly();
        CheckVibra.IsChecked = AppConfig.Is("vibra_enhanced");
        CheckVibra.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            AppConfig.Set("vibra_enhanced", CheckVibra.IsChecked == true ? 1 : 0);
            _actions.SetVibrationMode();
        };

        ActualThemeVariantChanged += (_, _) => ApplyAllyImage();
        Opened += (_, _) => ApplyAllyImage();

        _loading = false;
    }

    private Bitmap? _allyLight, _allyDark;

    /// <summary>The picture is black artwork: dark theme shows it inverted (legacy ControlHelper.AdjustImage).</summary>
    private void ApplyAllyImage()
    {
        try
        {
            _allyLight ??= new Bitmap(AssetLoader.Open(new Uri("avares://GHelper/Assets/Icons/ally.png")));
            bool dark = ActualThemeVariant == ThemeVariant.Dark;
            if (dark) _allyDark ??= Inverted(new Uri("avares://GHelper/Assets/Icons/ally.png"));
            ImageAlly.Source = dark ? _allyDark ?? _allyLight : _allyLight;
        }
        catch (Exception ex)
        {
            AppLog.Write("Handheld picture: " + ex.Message);
        }
    }

    private static Bitmap? Inverted(Uri asset)
    {
        using var stream = AssetLoader.Open(asset);
        using var codec = SKCodec.Create(stream);
        if (codec is null) return null;

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success) return null;

        var pixels = bitmap.GetPixelSpan();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(255 - pixels[i]);
            pixels[i + 1] = (byte)(255 - pixels[i + 1]);
            pixels[i + 2] = (byte)(255 - pixels[i + 2]);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new Bitmap(new MemoryStream(data.ToArray()));
    }

    /// <summary>Binding code currently shown in the editor ("" when no button was picked yet).</summary>
    internal string ActiveBinding => _activeBinding;

    // ------------------------------------------------------------------ deadzones / vibration

    private void Init()
    {
        TrackLSMin.Value = AppConfig.Get("ls_min", 0);
        TrackLSMax.Value = AppConfig.Get("ls_max", 100);
        TrackRSMin.Value = AppConfig.Get("rs_min", 0);
        TrackRSMax.Value = AppConfig.Get("rs_max", 100);

        TrackLTMin.Value = AppConfig.Get("lt_min", 0);
        TrackLTMax.Value = AppConfig.Get("lt_max", 100);
        TrackRTMin.Value = AppConfig.Get("rt_min", 0);
        TrackRTMax.Value = AppConfig.Get("rt_max", 100);

        TrackVibra.Value = AppConfig.Get("vibra", 100);

        VisualiseController();
    }

    private void VisualiseController()
    {
        LabelLS.Text = $"{(int)TrackLSMin.Value} - {(int)TrackLSMax.Value}%";
        LabelRS.Text = $"{(int)TrackRSMin.Value} - {(int)TrackRSMax.Value}%";

        LabelLT.Text = $"{(int)TrackLTMin.Value} - {(int)TrackLTMax.Value}%";
        LabelRT.Text = $"{(int)TrackRTMin.Value} - {(int)TrackRTMax.Value}%";

        LabelVibra.Text = $"{(int)TrackVibra.Value}%";
    }

    /// <summary>Save all values and send them to the controller (legacy Scroll + ValueChanged).</summary>
    private void Controller_Complete(object? sender, EventArgs e)
    {
        AppConfig.Set("ls_min", (int)TrackLSMin.Value);
        AppConfig.Set("ls_max", (int)TrackLSMax.Value);
        AppConfig.Set("rs_min", (int)TrackRSMin.Value);
        AppConfig.Set("rs_max", (int)TrackRSMax.Value);

        AppConfig.Set("lt_min", (int)TrackLTMin.Value);
        AppConfig.Set("lt_max", (int)TrackLTMax.Value);
        AppConfig.Set("rt_min", (int)TrackRTMin.Value);
        AppConfig.Set("rt_max", (int)TrackRTMax.Value);

        AppConfig.Set("vibra", (int)TrackVibra.Value);

        VisualiseController();
        _actions.SetDeadzones();
    }

    private void ResetController()
    {
        _loading = true;
        TrackLSMin.Value = 0;
        TrackLSMax.Value = 100;
        TrackRSMin.Value = 0;
        TrackRSMax.Value = 100;

        TrackLTMin.Value = 0;
        TrackLTMax.Value = 100;
        TrackRTMin.Value = 0;
        TrackRTMax.Value = 100;

        TrackVibra.Value = 100;
        _loading = false;

        // uncheck first: the handler stores 0 and tells the controller, the key is removed right after
        CheckVibra.IsChecked = false;

        AppConfig.Remove("ls_min");
        AppConfig.Remove("ls_max");
        AppConfig.Remove("rs_min");
        AppConfig.Remove("rs_max");

        AppConfig.Remove("lt_min");
        AppConfig.Remove("lt_max");
        AppConfig.Remove("rt_min");
        AppConfig.Remove("rt_max");
        AppConfig.Remove("vibra");
        AppConfig.Remove("vibra_enhanced");

        VisualiseController();
        _actions.SetDeadzones();
    }

    // ------------------------------------------------------------------ bindings

    private sealed class BindingEntry
    {
        public BindingEntry(string code, string name, bool separator = false)
        {
            Code = code;
            Name = name;
            IsSeparator = separator;
        }

        public string Code { get; }
        public string Name { get; }
        public bool IsSeparator { get; }
        public override string ToString() => Name;
    }

    private static List<BindingEntry> BuildBindingItems()
    {
        var list = new List<BindingEntry>();
        foreach (var (groupLabel, items) in AllyControl.BindingGroups)
        {
            if (groupLabel != "") list.Add(new BindingEntry("", groupLabel, separator: true));
            foreach (var (code, name) in items) list.Add(new BindingEntry(code, name));
        }

        return list;
    }

    private void InitBindingCombo(ComboBox combo)
    {
        combo.ItemsSource = BuildBindingItems();
        combo.ItemTemplate = new FuncDataTemplate<BindingEntry>((item, _) => new TextBlock
        {
            Text = item?.Name ?? "",
            FontWeight = item is { IsSeparator: true } ? FontWeight.Bold : FontWeight.Normal,
            Margin = new Thickness(item is { IsSeparator: true } ? 2 : 10, 0, 0, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });
        combo.SelectionChanged += Binding_SelectionChanged;
    }

    private void InitTurboCombo(ComboBox combo)
    {
        combo.ItemsSource = TurboValues.Select(ms => ms == 0 ? "Off" : ms.ToString()).ToArray();
        combo.SelectedIndex = 0;
        combo.SelectionChanged += Turbo_SelectionChanged;
    }

    private void Binding_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingBindings || sender is not ComboBox combo) return;

        if (combo.SelectedItem is BindingEntry { IsSeparator: true })
        {
            // headers are not choices: move on to the first item of the group
            int next = combo.SelectedIndex + 1;
            if (combo.ItemsSource is IList<BindingEntry> items && next < items.Count && !items[next].IsSeparator)
                combo.SelectedIndex = next;
            return;
        }

        if (combo.SelectedItem is not BindingEntry item || _activeBinding == "") return;

        string binding = "bind" + (ReferenceEquals(combo, ComboPrimary) ? "" : "2") + "_" + _activeBinding;

        if (item.Code != "") AppConfig.Set(binding, item.Code);
        else AppConfig.Remove(binding);

        VisualiseButton(_activeButton, _activeBinding);

        _actions.ApplyMode();
    }

    private void Turbo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingBindings || sender is not ComboBox combo || _activeBinding == "") return;

        int index = Math.Clamp(combo.SelectedIndex, 0, TurboValues.Length - 1);
        int ms = TurboValues[index];
        string key = (ReferenceEquals(combo, ComboTurboPrimary) ? "turbo_" : "turbo2_") + _activeBinding;

        if (ms > 0) AppConfig.Set(key, ms);
        else AppConfig.Remove(key);

        _actions.ApplyMode();
    }

    private void SetComboValue(ComboBox combo, string value)
    {
        _updatingBindings = true;
        try
        {
            if (combo.ItemsSource is IEnumerable<BindingEntry> items)
            {
                foreach (var item in items)
                {
                    if (!item.IsSeparator && item.Code == value)
                    {
                        combo.SelectedItem = item;
                        return;
                    }
                }
            }

            combo.SelectedIndex = 0;
        }
        finally
        {
            _updatingBindings = false;
        }
    }

    private void SetTurboValue(ComboBox combo, int ms)
    {
        _updatingBindings = true;
        int index = Array.IndexOf(TurboValues, ms);
        combo.SelectedIndex = index < 0 ? 0 : index;
        _updatingBindings = false;
    }

    private static void VisualiseButton(RButton? button, string binding)
    {
        if (button is null) return;

        string primary = AppConfig.GetString("bind_" + binding, "");
        string secondary = AppConfig.GetString("bind2_" + binding, "");

        if (primary != "" || secondary != "")
        {
            button.BorderColor = AppColors.Standard;
            button.Activated = true;
        }
        else
        {
            button.Activated = false;
        }
    }

    private void ButtonBinding(string binding, string label, RButton button)
    {
        _bindings[button] = binding;
        button.Click += (_, _) => ShowBinding(button, binding, label);
        VisualiseButton(button, binding);
    }

    private void ShowBinding(RButton button, string binding, string label)
    {
        PanelBinding.IsVisible = true;

        _activeButton = button;
        _activeBinding = binding;

        LabelBinding.Text = Strings.Binding + ": " + label;

        SetComboValue(ComboPrimary, AppConfig.GetString("bind_" + binding, ""));
        SetComboValue(ComboSecondary, AppConfig.GetString("bind2_" + binding, ""));

        SetTurboValue(ComboTurboPrimary, AppConfig.Get("turbo_" + binding, 0));
        SetTurboValue(ComboTurboSecondary, AppConfig.Get("turbo2_" + binding, 0));
    }
}
