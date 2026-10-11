using GHelper.Platform;
using System.Diagnostics;
using System.Drawing;

namespace GHelper.Overlay
{
    /// <summary>
    /// UI-independent state machine of the hardware overlay (the portable half of legacy HardwareOverlay):
    /// 1 s sampling of the sensors, text formatting, power history for the chart, FPS through <see cref="IFpsMonitor"/>,
    /// "game only" visibility, battery column handling and option parsing.
    /// The UI task owns the window: it creates one <see cref="OverlayModel"/>, calls <see cref="Start"/>/<see cref="Stop"/>,
    /// draws from the public properties using <see cref="OverlayLayout"/> when <see cref="Updated"/> fires (timer thread,
    /// marshal with Program.Ui.Post), and handles drag / scale / position persistence.
    /// </summary>
    public sealed class OverlayModel : IDisposable
    {
        public const int HistoryLength = 60;
        private const int MinGameFps = 6;

        private static readonly bool _isAlly = SafeIsAlly();

        private static readonly HashSet<string> DesktopApps = new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "iexplore", "chromium", "librewolf", "arc", "waterfox", "thorium",
            "WindowsTerminal", "conhost", "cmd", "powershell", "pwsh", "alacritty", "wezterm-gui", "mintty",
            "discord", "slack", "Teams", "ms-teams", "Spotify", "WhatsApp", "Signal", "Telegram", "Code", "Notion", "obsidian", "zoom", "Skype", "Element", "Viber", "LINE", "WeChat", "Weixin", "WeChatAppEx",
            "notepad", "notepad++", "sublime_text", "devenv", "rider64", "idea64", "pycharm64", "webstorm64",
            "steam", "steamwebhelper", "EpicGamesLauncher", "Battle.net", "GalaxyClient", "EADesktop", "UbisoftConnect",
            "vlc", "mpv", "mpc-hc64", "mpc-be64", "PotPlayerMini64", "wmplayer", "smplayer", "foobar2000", "aimp",
            "WINWORD", "EXCEL", "POWERPNT", "OUTLOOK", "Acrobat", "AcroRd32", "SumatraPDF", "thunderbird", "Mailspring", "OneNote", "GitHubDesktop", "7zFM", "WinRAR", "SnippingTool",
            "explorer", "ShellExperienceHost", "ShellHost", "SearchHost", "StartMenuExperienceHost", "ApplicationFrameHost", "SystemSettings", "Taskmgr",
            "PowerToys.PowerLauncher", "PowerToys.Settings", "PowerToys.ColorPickerUI", "PowerToys.FancyZonesEditor", "PowerToys.Peek.UI", "PowerToys.PowerOCR", "PowerToys.MeasureToolUI", "PowerToys.ImageResizer", "PowerToys.PowerRename", "PowerToys.AdvancedPaste", "Microsoft.CmdPal.UI",
            // Linux desktop
            "konsole", "gnome-terminal-server", "kitty", "foot", "xterm", "nautilus", "dolphin", "thunar", "gnome-shell", "plasmashell",
            "gnome-system-monitor", "kate", "gedit", "evince", "okular", "libreoffice", "soffice.bin", "telegram-desktop",
        };

        private readonly System.Timers.Timer _timer = new(1000) { AutoReset = true };
        private readonly object _gate = new();

        private IFpsMonitor? _fps;
        private Task? _fpsTask;
        private int _lastFgPid;
        private int _shownPid;
        private bool _fgDesktop;
        private int _gameTicks;
        private int _overlayBattery = -1;
        private bool _disposed;
        private int _historyHead;

        private readonly float[] _cpuHistory = new float[HistoryLength];
        private readonly float[] _gpuHistory = new float[HistoryLength];

        public OverlayModel()
        {
            _timer.Elapsed += (_, _) => Tick();
        }

        private static bool SafeIsAlly()
        {
            try { return AppConfig.IsAlly(); } catch { return false; }
        }

        // ---- events -------------------------------------------------------------------------------

        /// <summary>A sample was taken, redraw. Raised on the timer thread.</summary>
        public event Action? Updated;

        /// <summary>The overlay must be hidden (true) or shown again (false): "game only" mode.</summary>
        public event Action<bool>? VisibilityChanged;

        /// <summary>Option set changed the block set / size: relayout (and re-anchor) the window.</summary>
        public event Action? SettingsChanged;

        /// <summary>Display configuration / theme changed while active: re-anchor the window on its screen.</summary>
        public event Action? ReanchorRequested;

        // ---- options (re-read by Refresh) -------------------------------------------------------------

        public bool Active { get; private set; }
        public bool GameOnly { get; private set; }

        /// <summary>Hidden by the "game only" logic, the UI window should not be shown.</summary>
        public bool Hidden { get; private set; }

        public OverlayMode Mode { get; private set; }
        public int ScalePercent { get; private set; } = 100;
        public float Scale => OverlayLayout.GetScale(ScalePercent);
        public int BackgroundAlpha { get; private set; } = OverlaySettings.DefaultAlpha;
        public Color GpuColor { get; private set; } = OverlaySettings.DefaultGpuColor;
        public Color CpuColor { get; private set; } = OverlaySettings.DefaultCpuColor;

        public bool ShowFps { get; private set; }
        public bool ShowTemp { get; private set; }
        public bool ShowFans { get; private set; }
        public bool ShowChart { get; private set; }
        public bool ShowPower { get; private set; }
        public bool ShowUsage { get; private set; }
        public bool ShowRam { get; private set; }
        public bool ShowNames { get; private set; }
        public bool ShowBattery { get; private set; }

        /// <summary>The device runs on battery (always true on Ally) or the battery block is forced on.</summary>
        public bool OnBattery { get; private set; }

        public OverlayBlocks Blocks => new(ShowFps, ShowTemp, ShowFans, ShowChart, ShowPower, ShowUsage, ShowRam, ShowNames, ShowBattery);

        public OverlayLayoutResult Layout => OverlayLayout.Compute(Scale, Blocks, ShowBattery && OnBattery);

        // ---- sampled values -----------------------------------------------------------------------

        /// <summary>"GPU: 62" including the trailing separator space.</summary>
        public string GpuTemp { get; private set; } = "";
        public string CpuTemp { get; private set; } = "";
        public string GpuFan { get; private set; } = "";
        public string CpuFan { get; private set; } = "";
        public string GpuPower { get; private set; } = "";
        public string CpuPower { get; private set; } = "";
        public int? GpuUsage { get; private set; }
        public int? CpuUsage { get; private set; }
        public int? VramUsage { get; private set; }
        public int? RamUsage { get; private set; }
        public int? VramUsedMb { get; private set; }
        public int? RamUsedMb { get; private set; }
        public string GpuShortName { get; private set; } = "";
        public string CpuShortName { get; private set; } = "";
        public string BatteryLevel { get; private set; } = "";
        public string BatteryRate { get; private set; } = "";
        public int BatteryPercent { get; private set; }
        public bool BatteryCharging { get; private set; }
        public int CurrentFps { get; private set; }

        /// <summary>Power history ring buffers (watts), oldest sample at <see cref="HistoryHead"/>.</summary>
        public IReadOnlyList<float> CpuHistory => _cpuHistory;
        public IReadOnlyList<float> GpuHistory => _gpuHistory;
        public int HistoryHead => _historyHead;

        // ---- lifecycle ------------------------------------------------------------------------------

        private static bool IsOnBatteryPower() =>
            PlatformServices.Current.Power.GetPowerSource() != PowerSource.AC;

        public void Refresh()
        {
            if (!Active) return;
            ApplyOptions();
            ApplySensorFlags();
            EnsureFpsMonitor();
            SettingsChanged?.Invoke();
        }

        private void ApplyOptions()
        {
            Mode = OverlaySettings.GetMode();
            ScalePercent = OverlaySettings.GetScalePercent();
            GpuColor = OverlaySettings.ParseColorKey("overlay_color_gpu", OverlaySettings.DefaultGpuColor);
            CpuColor = OverlaySettings.ParseColorKey("overlay_color_cpu", OverlaySettings.DefaultCpuColor);
            BackgroundAlpha = OverlaySettings.GetBackgroundAlpha();

            int m = (int)Mode;
            ShowFps = OverlaySettings.BlockShown("overlay_show_fps", m);
            ShowTemp = OverlaySettings.BlockShown("overlay_show_temp", m);
            ShowFans = OverlaySettings.BlockShown("overlay_show_fans", m);
            ShowChart = OverlaySettings.BlockShown("overlay_show_chart", m);
            ShowPower = OverlaySettings.BlockShown("overlay_show_power", m);
            ShowUsage = OverlaySettings.BlockShown("overlay_show_usage", m);
            ShowRam = OverlaySettings.BlockShown("overlay_show_ram", m);
            ShowNames = OverlaySettings.BlockShown("overlay_names", m);
            _overlayBattery = OverlaySettings.BatteryState(m);
            ShowBattery = _overlayBattery != 0;
        }

        // Don't pull sensors for blocks that aren't drawn (power feeds both the power and chart blocks).
        private void ApplySensorFlags()
        {
            HardwareControl.readFans = ShowFans;
            HardwareControl.readUsage = ShowUsage;
            HardwareControl.readMemory = ShowRam;
            HardwareControl.readPower = ShowPower || ShowChart;
            HardwareControl.readBattery = ShowBattery && OnBattery;
        }

        /// <summary>Click on the overlay: Light, Default, Full, Complete, Light. Persists and refreshes.</summary>
        public void CycleMode()
        {
            var next = Mode switch
            {
                OverlayMode.Light => OverlayMode.Default,
                OverlayMode.Default => OverlayMode.Full,
                OverlayMode.Full => OverlayMode.Complete,
                _ => OverlayMode.Light,
            };
            AppConfig.Set("overlay_mode", (int)next);
            Refresh();
        }

        /// <summary>Ctrl+Shift+Alt+wheel / middle click. Returns the clamped value actually applied.</summary>
        public int ApplyScale(int percent)
        {
            percent = Math.Clamp(percent, OverlaySettings.MinScalePercent, OverlaySettings.MaxScalePercent);
            if (percent == ScalePercent) return percent;
            ScalePercent = percent;
            AppConfig.Set("overlay_scale_percent", percent);
            SettingsChanged?.Invoke();
            return percent;
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_disposed) return;

                Active = true;
                _lastFgPid = 0;
                GameOnly = AppConfig.IsOverlayGameOnly();
                Hidden = false;
                _shownPid = 0;
                _fgDesktop = false;
                _gameTicks = 0;

                ApplyOptions();
                OnBattery = _isAlly || _overlayBattery == 1 || IsOnBatteryPower();
                ApplySensorFlags();

                try { PlatformServices.Current.Power.UserPreferencesChanged += OnPreferencesChanged; } catch { }
                HardwareControl.ResetCPUPowerCounter();

                StopFps();
                CurrentFps = 0;
                EnsureFpsMonitor();

                if (GameOnly)
                {
                    Hidden = true;
                    VisibilityChanged?.Invoke(true);
                }
            }

            Tick();
            _timer.Start();
        }

        public void Stop()
        {
            Task? task;

            lock (_gate)
            {
                Active = false;
                HardwareControl.readUsage = false;
                HardwareControl.readFans = false;
                HardwareControl.readMemory = false;
                HardwareControl.readPower = false;
                HardwareControl.readBattery = false;
                try { PlatformServices.Current.Power.UserPreferencesChanged -= OnPreferencesChanged; } catch { }
                _timer.Stop();

                StopFps();
                CurrentFps = 0;

                // Wait for the FPS pump thread to actually finish before we trim memory,
                // otherwise its buffers are still mapped into our working set.
                task = _fpsTask;
                _fpsTask = null;
            }

            TrimAfter(task);
        }

        private static void TrimAfter(Task? prerequisite)
        {
            Task.Run(async () =>
            {
                if (prerequisite != null)
                {
                    try { await prerequisite.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
                }

                try
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
                    if (AppConfig.IsNotFalse("gc")) PlatformServices.Current.Process.TrimMemory();
                }
                catch { }
            });
        }

        /// <summary>Display went off: nothing is visible, stop sampling (legacy SuspendForDisplayOff).</summary>
        public void SuspendForDisplayOff()
        {
            if (Active) Stop();
        }

        /// <summary>Display back on: restart if the overlay is enabled (legacy ResumeForDisplayOn).</summary>
        public void ResumeForDisplayOn()
        {
            if (!Active && AppConfig.IsOverlay()) Start();
        }

        private void OnPreferencesChanged()
        {
            if (Active) ReanchorRequested?.Invoke();
        }

        // ---- FPS -----------------------------------------------------------------------------------

        // Started for the FPS block, or for Auto Show to detect a game even with FPS hidden. Only torn down in Stop().
        private void EnsureFpsMonitor()
        {
            if (_fps != null || !(ShowFps || GameOnly)) return;

            CurrentFps = 0;

            IFpsMonitor? monitor = null;
            try
            {
                var factory = PlatformServices.Current.Fps;
                if (factory.IsSupported) monitor = factory.Create();
            }
            catch (Exception ex)
            {
                Logger.WriteLine("FPS monitor: " + ex.Message);
            }

            if (monitor is null) return;

            _fps = monitor;
            _fpsTask = Task.Run(() =>
            {
                try { monitor.Start(); }
                catch (Exception ex) { Logger.WriteLine("FPS monitor start: " + ex.Message); }
            });
        }

        private void StopFps()
        {
            var fps = _fps;
            _fps = null;
            try { fps?.Dispose(); } catch { }
        }

        private static int GetForegroundPid()
        {
            try { return PlatformServices.Current.Fps.GetForegroundProcessId(); }
            catch { return 0; }
        }

        // ---- sampling -----------------------------------------------------------------------------

        public void Tick()
        {
            if (!Active || _disposed) return;

            // Pin FPS counter to the foreground process: queried every second so switching games just works.
            int fgPid = GetForegroundPid();
            bool ownWindow = fgPid == 0 || fgPid == Environment.ProcessId;

            var fps = _fps;
            if (fps != null)
            {
                if (!ownWindow && fgPid != _lastFgPid)
                {
                    _lastFgPid = fgPid;
                    CurrentFps = 0;
                    _fgDesktop = IsDesktopApp(fgPid);
                    fps.TargetPid = _fgDesktop ? 0 : fgPid;
                }
                else
                {
                    CurrentFps = (int)Math.Round(fps.SampleFps());
                }
            }

            if (GameOnly)
            {
                if (!ownWindow) UpdateGameVisibility(fgPid);
                if (Hidden) return;
            }

            HardwareControl.ReadSensorsOverlay();

            // gpuActive gates power, fan and chart history: when the GPU is disabled its sensors return stale values.
            double gpuTemp = OverlayFormat.ToDouble(HardwareControl.gpuTemp);
            double cpuTemp = OverlayFormat.ToDouble(HardwareControl.cpuTemp);
            bool gpuActive = gpuTemp > 0;

            // Trailing space is the separator between temp and fan number
            GpuTemp = "GPU:" + OverlayFormat.FmtTemp(gpuTemp) + " ";
            CpuTemp = "CPU:" + OverlayFormat.FmtTemp(cpuTemp) + " ";
            GpuFan = OverlayFormat.FormatFan(HardwareControl.gpuFanRPM);
            CpuFan = OverlayFormat.FormatFan(HardwareControl.cpuFanRPM);
            GpuPower = OverlayFormat.FmtGpuPower(HardwareControl.gpuPower);
            CpuPower = OverlayFormat.FmtPow(OverlayFormat.ToDouble(HardwareControl.cpuPower));

            PushHistory((float)Math.Max(0, OverlayFormat.ToDouble(HardwareControl.cpuPower)),
                        gpuActive ? (float)Math.Max(0, OverlayFormat.ToDouble(HardwareControl.gpuPower)) : 0f);

            CpuUsage = HardwareControl.cpuUsage;
            GpuUsage = gpuActive ? (HardwareControl.gpuUsage ?? 0) : null;
            VramUsage = gpuActive ? (HardwareControl.vramUsage ?? 0) : null;
            VramUsedMb = gpuActive ? (HardwareControl.vramUsedMb ?? 0) : null;
            RamUsage = HardwareControl.ramUsage;
            RamUsedMb = HardwareControl.ramUsedMb;

            if (ShowBattery)
            {
                bool onBattery = _isAlly || _overlayBattery == 1 || IsOnBatteryPower();
                if (onBattery != OnBattery)
                {
                    OnBattery = onBattery;
                    ApplySensorFlags();
                    SettingsChanged?.Invoke();
                }

                if (OnBattery)
                {
                    double level = HardwareControl.GetBatteryChargePercentage();
                    BatteryPercent = (int)Math.Round(level);
                    BatteryLevel = OverlayFormat.FormatBatteryLevel(level);
                    decimal rate = HardwareControl.batteryRate ?? 0;
                    BatteryRate = OverlayFormat.FormatBatteryRate(rate);
                    BatteryCharging = rate > 0;
                }
            }

            if (ShowNames)
            {
                if (CpuShortName.Length == 0)
                    CpuShortName = OverlayFormat.ShortCpuName(PawnIO.CpuInfo.Name);
                if (GpuShortName.Length == 0)
                    GpuShortName = OverlayFormat.ShortGpuName(HardwareControl.GpuControl?.FullName);
            }

            Updated?.Invoke();
        }

        private void PushHistory(float cpu, float gpu)
        {
            _cpuHistory[_historyHead] = cpu;
            _gpuHistory[_historyHead] = gpu;
            _historyHead = (_historyHead + 1) % HistoryLength;
        }

        private void UpdateGameVisibility(int fgPid)
        {
            _gameTicks = CurrentFps >= MinGameFps && !_fgDesktop ? _gameTicks + 1 : 0;
            if (_gameTicks >= 2) _shownPid = fgPid;
            SetHidden(fgPid != _shownPid);
        }

        private void SetHidden(bool hidden)
        {
            if (hidden == Hidden) return;
            Hidden = hidden;
            VisibilityChanged?.Invoke(hidden);
        }

        /// <summary>
        /// Instant show/hide against the latched game when the foreground window changes (platform event or UI hook).
        /// Doesn't latch the game itself, that is the timer's job.
        /// </summary>
        public void OnForegroundChanged()
        {
            if (!Active || !GameOnly) return;
            int fgPid = GetForegroundPid();
            if (fgPid == 0 || fgPid == Environment.ProcessId) return;
            SetHidden(fgPid != _shownPid);
        }

        public static bool IsDesktopApp(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return DesktopApps.Contains(p.ProcessName);
            }
            catch { return false; }
        }

        public static bool IsDesktopAppName(string processName) => DesktopApps.Contains(processName);

        // ---- chart ----------------------------------------------------------------------------------

        /// <summary>Largest CPU+GPU power sum in the history, at least 10 W (chart scale).</summary>
        public static float ChartPeak(IReadOnlyList<float> cpu, IReadOnlyList<float> gpu)
        {
            float peak = 10f;
            int n = Math.Min(cpu.Count, gpu.Count);
            for (int i = 0; i < n; i++)
            {
                float t = cpu[i] + gpu[i];
                if (t > peak) peak = t;
            }
            return peak;
        }

        /// <summary>
        /// Fills the three polylines of the stacked power chart (oldest sample left): base line at the bottom, CPU on top of
        /// it, GPU stacked on the CPU. Arrays must have <see cref="HistoryLength"/> entries.
        /// </summary>
        public void BuildChart(int x, int y, int w, int h, PointF[] basePts, PointF[] cpuPts, PointF[] gpuPts)
        {
            float peak = ChartPeak(_cpuHistory, _gpuHistory);
            float stepX = (float)w / (HistoryLength - 1);

            for (int i = 0; i < HistoryLength; i++)
            {
                int idx = (_historyHead + i) % HistoryLength;
                float px = x + i * stepX;
                float cpuH = (_cpuHistory[idx] / peak) * h;
                float gpuH = (_gpuHistory[idx] / peak) * h;

                basePts[i] = new PointF(px, y + h);
                cpuPts[i] = new PointF(px, y + h - cpuH);
                gpuPts[i] = new PointF(px, y + h - cpuH - gpuH);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (Active) Stop();
            _disposed = true;
            _timer.Dispose();
        }
    }
}
