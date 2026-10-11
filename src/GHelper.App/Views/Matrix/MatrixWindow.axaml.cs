using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GHelper.AnimeMatrix;
using GHelper.App.Services;
using GHelper.App.Theme;
using GHelper.Properties;

namespace GHelper.App.Views;

/// <summary>
/// AniMe Matrix editor (legacy Matrix form): mode tabs (picture / clock / audio / text), a live LED preview that can be
/// dragged to move the picture, text or clock, and the settings of the active mode. Everything is applied through
/// <see cref="AniMatrixControl"/>; the preview follows <see cref="AnimeMatrixDevice.OnPresent"/>.
/// </summary>
public partial class MatrixWindow : SideWindow
{
    private static readonly string[] ScalingNames =
    {
        "Default", "Low", "High", "Bilinear", "Bicubic", "NearestNeighbor", "HighQualityBilinear", "HighQualityBicubic",
    };

    private readonly AniMatrixControl _control;
    private readonly DispatcherTimer _textTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _dragTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private bool _loading = true;
    private bool _dragging;
    private double _xPos, _yPos;
    private int _dragX, _dragY, _baseX, _baseY, _dragLine;
    private int _previewPending;

    // picture loaded for dragging
    private MatrixImage? _picture;
    private string? _loadedPicture;
    private int _dragFrame;

    private bool _subscribed;

    /// <summary>Fetches the ROG matrix font when it is missing (legacy: on every open). Replaceable for tests.</summary>
    internal static Func<Task<bool>> FontDownloader { get; set; } = MatrixFont.Download;

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void RegisterWindow() =>
        WindowRegistry.Register("matrix", arg => Create(arg));

    private static Window Create(int arg)
    {
        var control = Program.matrixControl ?? AniMatrixControl.Instance;
        // the settings button of the main window asks for "matrix" on both device families
        return control.IsSlash ? new SlashWindow(control) : new MatrixWindow(control);
    }

    public MatrixWindow() : this(Program.matrixControl ?? AniMatrixControl.Instance)
    {
    }

    public MatrixWindow(int arg) : this(Program.matrixControl ?? AniMatrixControl.Instance)
    {
    }

    internal MatrixWindow(AniMatrixControl control)
    {
        _control = control;
        InitializeComponent();

        Title = Strings.AnimeMatrix;
        LabelZoomTitle.Text = Strings.Zoom;
        LabelScaling.Text = Strings.ScalingQuality;
        LabelRotation.Text = Strings.ImageRotation;
        LabelContrastTitle.Text = Strings.Contrast;
        LabelGammaTitle.Text = Strings.Brightness;
        LabelClockTime.Text = Strings.MatrixTimeFormat;
        LabelClockDate.Text = Strings.MatrixDateFormat;
        LabelAudioMode.Text = Strings.MatrixAudioMode;
        ButtonPicture.Content = Strings.PictureGif;
        ButtonReset.Content = Strings.Reset;
        CheckTextRunning.Content = Strings.MatrixRunningText;
        CheckClockBattery.Content = Strings.SlashBatteryLevel;
        CheckFlip.Content = Strings.MatrixFlip;
        CheckAutoOff.Content = Strings.TurnOffOnBattery;
        CheckLidOff.Content = Strings.DisableOnLidClose;

        ButtonPictureMode.Content = Strings.MatrixPicture;
        ButtonClockMode.Content = Strings.MatrixClock;
        ButtonAudioMode.Content = Strings.MatrixAudio;
        ButtonTextMode.Content = Strings.MatrixText;
        var standard = AppColors.Standard;
        ButtonPictureMode.BorderColor = ButtonClockMode.BorderColor =
            ButtonAudioMode.BorderColor = ButtonTextMode.BorderColor = standard;

        Preview.Device = Device;

        // ---- initial values (events are hooked up afterwards, like the legacy constructor) ----
        TrackZoom.Value = Math.Min(TrackZoom.Maximum, AppConfig.Get("matrix_zoom", 100));
        TrackContrast.Value = Math.Min(TrackContrast.Maximum, AppConfig.Get("matrix_contrast", 100));
        TrackGamma.Value = Math.Min(TrackGamma.Maximum, AppConfig.Get("matrix_gamma", 0));
        VisualiseSliders();

        ComboScaling.ItemsSource = ScalingNames;
        ComboScaling.SelectedIndex = Math.Clamp(AppConfig.Get("matrix_quality", 0), 0, ScalingNames.Length - 1);

        ComboRotation.ItemsSource = new[] { "Straight", "Diagonal" };
        ComboRotation.SelectedIndex = Math.Clamp(AppConfig.Get("matrix_rotation", 0), 0, 1);

        ComboAudioMode.ItemsSource = new[] { "Bars", "Spectrogram" };
        ComboAudioMode.SelectedIndex = Math.Clamp(AppConfig.Get("matrix_audio_mode", 0), 0, 1);

        TextMatrix.Text = AppConfig.GetString("matrix_text", "Hello!");
        TextMatrix2.Text = AppConfig.GetString("matrix_text2", "");

        InitFontCombo(ComboTextFont, "matrix_text_font");
        InitFontCombo(ComboTextFont2, "matrix_text2_font");

        NumTextSize.Value = Math.Clamp(AppConfig.Get("matrix_text_size", 15), 8, 30);
        NumTextSize2.Value = Math.Clamp(AppConfig.Get("matrix_text2_size", 15), 8, 30);
        CheckTextRunning.IsChecked = AppConfig.Is("matrix_text_running");

        TextClockTime.Text = AppConfig.GetString("matrix_time", "HH:mm");
        TextClockDate.Text = AppConfig.GetString("matrix_date", "yy.MM.dd");
        CheckClockBattery.IsChecked = AppConfig.Is("matrix_clock_battery");
        TextClockDate.IsEnabled = CheckClockBattery.IsChecked != true;
        CheckFlip.IsChecked = AppConfig.Is("matrix_flip");

        CheckAutoOff.IsChecked = AppConfig.Is("matrix_auto");
        CheckLidOff.IsChecked = AppConfig.Is("matrix_lid");

        // ---- events ----
        ButtonPicture.Click += (_, _) => _ = PickPictureAsync();
        ButtonReset.Click += (_, _) => ResetCurrentMode();

        ButtonPictureMode.Click += ButtonPictureMode_Click;
        ButtonClockMode.Click += (_, _) => SetRunningMode(MatrixMode.Clock);
        ButtonAudioMode.Click += (_, _) => SetRunningMode(MatrixMode.Audio);
        ButtonTextMode.Click += (_, _) => SetRunningMode(MatrixMode.Text);

        Preview.PointerPressed += Preview_PointerPressed;
        Preview.PointerMoved += Preview_PointerMoved;
        Preview.PointerReleased += Preview_PointerReleased;
        Preview.PointerCaptureLost += Preview_CaptureLost;

        TrackZoom.ValueChanged += (_, _) => VisualiseSliders();
        TrackContrast.ValueChanged += (_, _) => VisualiseSliders();
        TrackGamma.ValueChanged += (_, _) => VisualiseSliders();
        TrackZoom.ValueCommitted += (_, _) => { AppConfig.Set("matrix_zoom", (int)TrackZoom.Value); ApplyPicture(); };
        TrackContrast.ValueCommitted += TrackMatrix_Committed;
        TrackGamma.ValueCommitted += TrackMatrix_Committed;

        ComboScaling.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            AppConfig.Set("matrix_quality", ComboScaling.SelectedIndex);
            ApplyPicture();
        };
        ComboRotation.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            AppConfig.Set("matrix_rotation", ComboRotation.SelectedIndex);
            ApplyPicture();
        };
        ComboAudioMode.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            AppConfig.Set("matrix_audio_mode", ComboAudioMode.SelectedIndex);
            SetRunningMode(MatrixMode.Audio);
        };

        TextMatrix.TextChanged += (_, _) => Debounce(_textTimer);
        TextMatrix2.TextChanged += (_, _) => Debounce(_textTimer);
        _textTimer.Tick += (_, _) => { _textTimer.Stop(); ApplyText(); };

        TextClockTime.TextChanged += (_, _) => Debounce(_clockTimer);
        TextClockDate.TextChanged += (_, _) => Debounce(_clockTimer);
        _clockTimer.Tick += (_, _) => { _clockTimer.Stop(); ApplyClock(); };

        _dragTimer.Tick += DragTimer_Tick;

        ComboTextFont.SelectionChanged += TextSettings_Changed;
        ComboTextFont2.SelectionChanged += TextSettings_Changed;
        NumTextSize.ValueChanged += TextSettings_Changed;
        NumTextSize2.ValueChanged += TextSettings_Changed;
        CheckTextRunning.IsCheckedChanged += TextSettings_Changed;

        CheckClockBattery.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            AppConfig.Set("matrix_clock_battery", CheckClockBattery.IsChecked == true ? 1 : 0);
            TextClockDate.IsEnabled = CheckClockBattery.IsChecked != true;
            Device?.PresentClock();
        };
        CheckFlip.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            AppConfig.Set("matrix_flip", CheckFlip.IsChecked == true ? 1 : 0);
            Device?.PresentClock();
        };

        CheckAutoOff.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            AppConfig.Set("matrix_auto", CheckAutoOff.IsChecked == true ? 1 : 0);
            _control.SetBatteryAuto();
        };
        CheckLidOff.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            AppConfig.Set("matrix_lid", CheckLidOff.IsChecked == true ? 1 : 0);
            _control.SetLidMode(true);
        };

        if (Device is not null) Device.OnPresent = RequestPreview;

        Opened += OnOpened;
        Closed += OnClosed;
        SizeChanged += (_, _) => { if (IsVisible) AttachToMain(); };

        _loading = false;
        VisualiseMode();
    }

    private AnimeMatrixDevice? Device => _control.deviceMatrix;

    /// <summary>The picture currently loaded for dragging (null when none / not a picture mode).</summary>
    internal MatrixImage? LoadedPicture => _picture;

    // ------------------------------------------------------------------ setup / teardown

    private static void InitFontCombo(ComboBox combo, string param)
    {
        var items = new List<string> { "Default" };
        items.AddRange(AniMatrixControl.TextFonts.Where(f => f.Length > 0));
        combo.ItemsSource = items;
        combo.SelectedIndex = Math.Clamp(AppConfig.Get(param, 0), 0, items.Count - 1);
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (!_subscribed && App.Instance?.Host is { } host)
        {
            host.AreaRefreshed += OnAreaRefreshed;
            _subscribed = true;
        }

        try
        {
            VisualiseMode();

            // gated: render for preview, otherwise just load the picture for dragging
            if (_control.IsGated) _control.SetDevice();
            else if (AniMatrixControl.Mode == MatrixMode.Picture) VisualiseMatrix(AppConfig.GetString("matrix_picture"));

            if (!AnimeMatrixDevice.HasDefaultFont && await FontDownloader() && IsVisible)
                _control.SetDevice();
        }
        catch (Exception ex)
        {
            AppLog.Write("Matrix window: " + ex.Message);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_subscribed && App.Instance?.Host is { } host) host.AreaRefreshed -= OnAreaRefreshed;
        _subscribed = false;

        if (Device is not null) Device.OnPresent = null;

        _textTimer.Stop();
        _clockTimer.Stop();
        _dragTimer.Stop();

        _picture = null;
        _loadedPicture = null;

        // re-apply gating (the editor kept rendering previews while the matrix was switched off)
        try
        {
            if (_control.IsGated) _control.SetDevice();
        }
        catch (Exception ex)
        {
            AppLog.Write("Matrix window close: " + ex.Message);
        }
    }

    private void OnAreaRefreshed(string area)
    {
        if (!string.Equals(area, "matrix", StringComparison.OrdinalIgnoreCase)) return;
        RefreshFromConfig();
    }

    /// <summary>The running mode / picture changed outside of this window (main window combo, Core, hotkey).</summary>
    public void RefreshFromConfig()
    {
        VisualiseMode();
        if (AniMatrixControl.Mode == MatrixMode.Picture) VisualiseMatrix(AppConfig.GetString("matrix_picture"));
    }

    // ------------------------------------------------------------------ mode / preview

    /// <summary>Port of Matrix.VisualiseMode: tab highlight and the settings panel of the active mode.</summary>
    public void VisualiseMode()
    {
        MatrixMode running = AniMatrixControl.Mode;
        bool text = running == MatrixMode.Text;
        bool clock = running == MatrixMode.Clock;
        bool audio = running == MatrixMode.Audio;
        bool pic = !text && !clock && !audio;

        ButtonPictureMode.Activated = running == MatrixMode.Picture;
        ButtonClockMode.Activated = clock;
        ButtonAudioMode.Activated = audio;
        ButtonTextMode.Activated = text;

        PanelPictureSettings.IsVisible = pic;
        PanelTextSettings.IsVisible = text;
        PanelClockSettings.IsVisible = clock;
        PanelAudioSettings.IsVisible = audio;

        PanelButtons.IsVisible = !audio;
        ButtonPicture.IsVisible = pic;

        VisualisePicture();
    }

    /// <summary>Safe from any thread: repaints the LED preview (coalesced).</summary>
    private void RequestPreview()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Preview.InvalidateVisual();
            return;
        }

        if (Interlocked.Exchange(ref _previewPending, 1) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref _previewPending, 0);
            Preview.InvalidateVisual();
        });
    }

    public void VisualisePicture() => RequestPreview();

    private void VisualiseSliders()
    {
        LabelZoom.Text = (int)TrackZoom.Value + "%";
        LabelContrast.Text = (int)TrackContrast.Value + "%";
        LabelGamma.Text = (int)TrackGamma.Value + "%";
    }

    /// <summary>Port of Matrix.VisualiseMatrix(fileName): decode the picture once so it can be dragged.</summary>
    public void VisualiseMatrix(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            VisualisePicture();
            return;
        }

        // same file already loaded for dragging, skip the re-read
        if (fileName == _loadedPicture && _picture is not null)
        {
            VisualisePicture();
            return;
        }

        _picture = null;
        _loadedPicture = null;

        try
        {
            var image = MatrixRendering.ImageDecoder?.Load(fileName);
            if (image is not null)
            {
                _picture = image;
                _dragFrame = 0;
                _loadedPicture = fileName;
                if (image.Frames.Count > 1)
                    _dragTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(10, AniMatrixControl.PictureFrameDelay(image)));
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }

        VisualisePicture();
    }

    // ------------------------------------------------------------------ mode switching / commands

    private void SetRunningMode(MatrixMode mode)
    {
        // legacy SettingsForm.SetMatrixRunning
        AppConfig.Set("matrix_running", (int)mode);
        _control.SetDevice();
        Program.Ui.Refresh("matrix");
        VisualiseMode();
    }

    private void ButtonPictureMode_Click(object? sender, RoutedEventArgs e)
    {
        if (AppConfig.GetString("matrix_picture") is null)
        {
            _ = PickPictureAsync();
            return;
        }

        SetRunningMode(MatrixMode.Picture);
    }

    private async Task PickPictureAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Strings.PictureGif,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Image Files") { Patterns = new[] { "*.bmp", "*.jpg", "*.jpeg", "*.png", "*.gif" } },
                },
            });

            if (files.Count == 0) return;

            string? path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            _control.OpenMatrixPicture(path);
            RefreshFromConfig();
        }
        catch (Exception ex)
        {
            AppLog.Write("Matrix picture: " + ex.Message);
        }
    }

    private void ApplyPicture()
    {
        string? path = AppConfig.GetString("matrix_picture");
        if (path is null) return;

        if (AniMatrixControl.Mode != MatrixMode.Picture) SetRunningMode(MatrixMode.Picture);
        else _control.SetMatrixPicture(path);
    }

    private void TrackMatrix_Committed(object? sender, EventArgs e)
    {
        AppConfig.Set("matrix_contrast", (int)TrackContrast.Value);
        AppConfig.Set("matrix_gamma", (int)TrackGamma.Value);
        ApplyPicture();
    }

    private static void Debounce(DispatcherTimer timer)
    {
        timer.Stop();
        timer.Start();
    }

    private void TextSettings_Changed(object? sender, EventArgs e)
    {
        if (_loading) return;
        ApplyText();
    }

    private void ApplyText()
    {
        AppConfig.Set("matrix_text", TextMatrix.Text ?? "");
        AppConfig.Set("matrix_text2", TextMatrix2.Text ?? "");
        AppConfig.Set("matrix_text_font", ComboTextFont.SelectedIndex);
        AppConfig.Set("matrix_text2_font", ComboTextFont2.SelectedIndex);
        AppConfig.Set("matrix_text_size", (int)(NumTextSize.Value ?? 15));
        AppConfig.Set("matrix_text2_size", (int)(NumTextSize2.Value ?? 15));
        AppConfig.Set("matrix_text_running", CheckTextRunning.IsChecked == true ? 1 : 0);

        if (AniMatrixControl.Mode != MatrixMode.Text) SetRunningMode(MatrixMode.Text);
        else _control.SetMatrixText();
    }

    private void ApplyClock()
    {
        // only save formats that actually parse
        string time = TextClockTime.Text ?? "", date = TextClockDate.Text ?? "";
        try { DateTime.Now.ToString(time); if (time.Length > 0) AppConfig.Set("matrix_time", time); } catch { }
        try { DateTime.Now.ToString(date); if (date.Length > 0) AppConfig.Set("matrix_date", date); } catch { }

        Device?.PresentClock();
    }

    private void ResetCurrentMode()
    {
        MatrixMode running = AniMatrixControl.Mode;

        if (running == MatrixMode.Text)
        {
            AppConfig.Set("matrix_text_x", 0);
            AppConfig.Set("matrix_text_y", 0);
            AppConfig.Set("matrix_text2_x", 0);
            AppConfig.Set("matrix_text2_y", 0);

            _loading = true;
            NumTextSize.Value = 15;
            NumTextSize2.Value = 15;
            _loading = false;

            ApplyText();
            return;
        }

        if (running == MatrixMode.Clock)
        {
            AppConfig.Set("matrix_clock_x", 0);
            AppConfig.Set("matrix_clock_y", 0);
            AppConfig.Set("matrix_time", "HH:mm");
            AppConfig.Set("matrix_date", "yy.MM.dd");

            _loading = true;
            TextClockTime.Text = "HH:mm";
            TextClockDate.Text = "yy.MM.dd";
            CheckClockBattery.IsChecked = false;
            TextClockDate.IsEnabled = true;
            _loading = false;

            AppConfig.Set("matrix_clock_battery", 0);
            Device?.PresentClock();
            return;
        }

        AppConfig.Set("matrix_gamma", 0);
        AppConfig.Set("matrix_contrast", 100);
        AppConfig.Set("matrix_zoom", 100);
        AppConfig.Set("matrix_x", 0);
        AppConfig.Set("matrix_y", 0);

        TrackZoom.Value = 100;
        TrackContrast.Value = 100;
        TrackGamma.Value = 0;

        ApplyPicture();
    }

    // ------------------------------------------------------------------ dragging

    private void GeneratePicture(int matrixX, int matrixY)
    {
        if (_picture is null || Device is null) return;

        var frame = _picture.Frames[Math.Max(0, _dragFrame) % _picture.Frames.Count];
        _control.GeneratePictureFrame(frame, matrixX, matrixY);
        VisualisePicture();
    }

    private void DragTimer_Tick(object? sender, EventArgs e)
    {
        if (!_dragging || _picture is null) return;
        _dragFrame++;
        GeneratePicture(_baseX + _dragX, _baseY + _dragY);
    }

    private void Preview_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(Preview);
        if (!point.Properties.IsLeftButtonPressed) return;

        var device = Device;
        if (device is null) return;

        MatrixMode running = AniMatrixControl.Mode;
        if (running != MatrixMode.Picture && running != MatrixMode.Clock && running != MatrixMode.Text) return;

        var pos = point.Position;

        if (running == MatrixMode.Text)
        {
            _control.StopMatrixTimer();
            _dragLine = device.HitTestText((int)(pos.X / Preview.CellWidth), (int)(pos.Y / Preview.CellHeight));
            string prefix = AnimeMatrixDevice.TextPrefix(_dragLine);
            _baseX = AppConfig.Get(prefix + "_x", 0);
            _baseY = AppConfig.Get(prefix + "_y", 0) / 2 * 2;
            device.PresentText(_dragLine, _baseX, _baseY);
        }
        else if (running == MatrixMode.Clock)
        {
            _control.StopMatrixTimer();
            _baseX = AppConfig.Get("matrix_clock_x", 0);
            _baseY = AppConfig.Get("matrix_clock_y", 0) / 2 * 2;
        }
        else
        {
            if (_picture is null) return;
            _control.StopMatrixTimer();
            device.ClearFrames();
            _baseX = AppConfig.Get("matrix_x", 0);
            _baseY = AppConfig.Get("matrix_y", 0);
            // freeze long gifs while dragging
            if (_picture.Frames.Count > 1 && _picture.Frames.Count < 60) _dragTimer.Start();
        }

        _dragging = true;
        _xPos = pos.X;
        _yPos = pos.Y;
        _dragX = _dragY = 0;
        e.Pointer.Capture(Preview);
        e.Handled = true;
    }

    private void Preview_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;

        var device = Device;
        if (device is null) return;

        MatrixMode running = AniMatrixControl.Mode;
        var pos = e.GetPosition(Preview);

        double cols = (pos.X - _xPos) / Preview.CellWidth;
        double rows = (pos.Y - _yPos) / Preview.CellHeight;

        int dX, dY;

        if (running == MatrixMode.Text || running == MatrixMode.Clock)
        {
            // odd row shifts look jagged on the staggered panel, move by 2
            dX = (int)Math.Round(cols);
            dY = 2 * (int)Math.Round(rows / 2);
        }
        else if ((MatrixRotation)AppConfig.Get("matrix_rotation", 0) == MatrixRotation.Planar)
        {
            dX = (int)Math.Round(-cols * 3);
            dY = (int)Math.Round(-rows);
        }
        else
        {
            // diagonal pan axes: convert cell shift to pan delta
            dX = (int)Math.Round(-cols - rows / 2);
            dY = -dX - (int)Math.Round(rows);
        }

        if (dX == _dragX && dY == _dragY) return;

        _dragX = dX;
        _dragY = dY;

        if (running == MatrixMode.Text) device.PresentText(_dragLine, _baseX + _dragX, _baseY + _dragY);
        else if (running == MatrixMode.Clock) device.PresentClock(_baseX + _dragX, _baseY + _dragY);
        else GeneratePicture(_baseX + _dragX, _baseY + _dragY);
    }

    private void Preview_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        EndDrag();
    }

    private void Preview_CaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _dragTimer.Stop();

        MatrixMode running = AniMatrixControl.Mode;

        if (running == MatrixMode.Text)
        {
            string prefix = AnimeMatrixDevice.TextPrefix(_dragLine);
            AppConfig.Set(prefix + "_x", _baseX + _dragX);
            AppConfig.Set(prefix + "_y", _baseY + _dragY);
            _dragX = _dragY = 0;

            _control.SetMatrixText();
        }
        else if (running == MatrixMode.Clock)
        {
            AppConfig.Set("matrix_clock_x", _baseX + _dragX);
            AppConfig.Set("matrix_clock_y", _baseY + _dragY);
            _dragX = _dragY = 0;

            _control.SetMatrixClock();
        }
        else
        {
            AppConfig.Set("matrix_x", _baseX + _dragX);
            AppConfig.Set("matrix_y", _baseY + _dragY);
            _dragX = _dragY = 0;

            if (_picture is { Frames.Count: > 1 }) ApplyPicture();
            else Device?.Present();
        }
    }
}
