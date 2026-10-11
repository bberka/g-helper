using GHelper.AnimeMatrix;
using GHelper.Gpu;
using GHelper.Helpers;
using GHelper.Input;
using GHelper.Peripherals;
using GHelper.Platform;
using System.Drawing;
using static GHelper.Helpers.DynamicLightingHelper;

namespace GHelper.USB
{
    public class AuraPower
    {
        public bool BootLogo;
        public bool BootKeyb;
        public bool AwakeLogo;
        public bool AwakeKeyb;
        public bool SleepLogo;
        public bool SleepKeyb;
        public bool ShutdownLogo;
        public bool ShutdownKeyb;

        public bool BootBar;
        public bool AwakeBar;
        public bool SleepBar;
        public bool ShutdownBar;

        public bool BootLid;
        public bool AwakeLid;
        public bool SleepLid;
        public bool ShutdownLid;

        public bool BootRear;
        public bool AwakeRear;
        public bool SleepRear;
        public bool ShutdownRear;
    }

    public enum AuraSpeed : int
    {
        Slow = 0,
        Normal = 1,
        Fast = 2,
    }

    public enum AuraBacklightType : byte
    {
        Unknown = 0x00,
        MultiZone = 0x02,
        PerKey = 0x03,
        SingleZone = 0x04,
    }


    public static class Aura
    {

        static byte[] MESSAGE_APPLY = { AsusHid.AURA_ID, 0xb4 };
        static byte[] MESSAGE_SET = { AsusHid.AURA_ID, 0xb5, 0, 0, 0 };

        static readonly int AURA_ZONES = 8;

        private static AuraMode mode = AuraMode.AuraStatic;
        private static AuraSpeed speed = AuraSpeed.Normal;

        private static bool backlight = false;
        private static bool initDirect = false;
        public static bool sessionLock = false;

        public static Color Color1 = Color.White;
        public static Color Color2 = Color.Black;

        public static Color RearColor = Color.White;
        private static AuraMode rearMode = AuraMode.AuraStatic;
        public static AuraMode RearMode
        {
            get { return rearMode; }
            set { rearMode = GetRearModes().ContainsKey(value) ? value : AuraMode.AuraStatic; }
        }

        static bool isACPI = AppConfig.IsTUF() || AppConfig.IsVivoZenPro();

        // legacy: SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online
        static bool IsOnAc => PlatformServices.Current.Power.GetPowerSource() == GHelper.Platform.PowerSource.AC;

        static bool isStrix => BacklightType == AuraBacklightType.MultiZone || BacklightType == AuraBacklightType.PerKey;
        public static bool IsBacklightDetected => BacklightType != AuraBacklightType.Unknown;

        static bool isStrix4Zone = false;
        static bool isStrixNumpad = AppConfig.IsStrixNumpad();
        static bool isStrix4ZoneFlipped = AppConfig.IsStrix4ZoneFlipped();

        static public bool isWhite = AppConfig.IsWhite();

        public static AuraBacklightType BacklightType { get; private set; } = AuraBacklightType.Unknown;

        public static bool HasLogo { get; private set; }
        public static bool HasLightbar { get; private set; }
        public static bool HasRearglow { get; private set; }
        public static bool IsOldStrix { get; private set; }

        static System.Timers.Timer timer = new System.Timers.Timer(1000);

        static readonly List<double> audioMaxes = new List<double>();
        static long lastAudioPresent;
        static double envBrightness;
        static double smoothedHue;
        static readonly double audioDecay = AppConfig.Get("audio_decay", 70) / 100.0;

        static Aura()
        {
            timer.Elapsed += Timer_Elapsed;
        }

        public static Dictionary<AuraSpeed, string> GetSpeeds()
        {
            return new Dictionary<AuraSpeed, string>
            {
                { AuraSpeed.Slow, Properties.Strings.AuraSlow },
                { AuraSpeed.Normal, Properties.Strings.AuraNormal },
                { AuraSpeed.Fast, Properties.Strings.AuraFast }
            };
        }


        public static Dictionary<AuraMode, string> GetModes()
        {
            var modes = new Dictionary<AuraMode, string>();

            if (isWhite)
            {
                modes[AuraMode.AuraStatic] = Properties.Strings.AuraStatic;
                modes[AuraMode.AuraBreathe] = Properties.Strings.AuraBreathe;
                modes[AuraMode.AuraStrobe] = Properties.Strings.AuraStrobe;
                return modes;
            }

            if (AppConfig.IsDynamicLightingOnly())
            {
                modes[AuraMode.AuraStatic] = Properties.Strings.AuraStatic;
                modes[AuraMode.AuraBreathe] = Properties.Strings.AuraColorCycle;
                modes[AuraMode.AuraRainbow] = Properties.Strings.AuraRainbow;
                modes[AuraMode.AuraStrobe] = Properties.Strings.AuraStrobe;
                return modes;
            }

            bool perKey = BacklightType == AuraBacklightType.PerKey;
            bool multiZone = BacklightType == AuraBacklightType.MultiZone;
            bool isStrixKb = perKey || multiZone;
            bool isAlly = AppConfig.IsAlly();

            modes[AuraMode.AuraStatic] = Properties.Strings.AuraStatic;
            modes[AuraMode.AuraBreathe] = Properties.Strings.AuraBreathe;
            modes[AuraMode.AuraColorCycle] = Properties.Strings.AuraColorCycle;
            if (isStrixKb) modes[AuraMode.AuraRainbow] = Properties.Strings.AuraRainbow;

            if (perKey)
            {
                modes[AuraMode.Star] = "Star";
                modes[AuraMode.Rain] = "Rain";
                modes[AuraMode.Highlight] = "Highlight";
                modes[AuraMode.Laser] = "Laser";
                modes[AuraMode.Ripple] = "Ripple";
            }

            modes[AuraMode.AuraStrobe] = Properties.Strings.AuraStrobe;

            if (perKey)
            {
                modes[AuraMode.Comet] = "Comet";
                modes[AuraMode.Flash] = "Flash";
            }

            if (isAlly)
            {
                modes[AuraMode.BATTERY] = "Battery";
                return modes;
            }

            modes[AuraMode.HEATMAP] = "Heatmap";
            modes[AuraMode.GPUMODE] = "GPU Mode";
            modes[AuraMode.AMBIENT] = "Ambient";
            modes[AuraMode.BATTERY] = "Battery";
            modes[AuraMode.AUDIO] = "Audio Spectrum";
            modes[AuraMode.AUDIOPULSE] = "Audio Pulse";

            if (isStrixKb)
            {
                modes[AuraMode.GRADIENT] = "Gradient";
                modes[AuraMode.ZONETEST] = "Zone Test";
            }

            return modes;
        }

        private static Dictionary<AuraMode, string> _modesRear = new Dictionary<AuraMode, string>
        {
            { AuraMode.AuraStatic, Properties.Strings.AuraStatic },
            { AuraMode.AuraBreathe, Properties.Strings.AuraBreathe },
            { AuraMode.AuraColorCycle, Properties.Strings.AuraColorCycle },
            { AuraMode.AuraRainbow, Properties.Strings.AuraRainbow },
            { AuraMode.AuraStrobe, Properties.Strings.AuraStrobe },
        };

        public static Dictionary<AuraMode, string> GetRearModes()
        {
            return _modesRear;
        }
        public static AuraMode Mode
        {
            get { return mode; }
            set
            {
                mode = GetModes().ContainsKey(value) ? value : AuraMode.AuraStatic;
            }
        }

        public static AuraSpeed Speed
        {
            get { return speed; }
            set
            {
                speed = GetSpeeds().ContainsKey(value) ? value : AuraSpeed.Normal;
            }

        }

        public static void SetColor(int colorCode)
        {
            Color1 = Color.FromArgb(colorCode);
        }

        public static void SetColor2(int colorCode)
        {
            Color2 = Color.FromArgb(colorCode);
        }

        public static void SetRearColor(int colorCode)
        {
            RearColor = Color.FromArgb(colorCode);
        }

        public static bool HasSecondColor()
        {
            return (mode == AuraMode.AuraBreathe || mode == AuraMode.GRADIENT) && (!isACPI || AppConfig.IsDynamicLightingOnly());
        }

        public static bool HasRandomColor()
        {
            return mode == AuraMode.Star || mode == AuraMode.Highlight || mode == AuraMode.Laser || mode == AuraMode.Ripple;
        }

        private static void Timer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (!InputDispatcher.backlightActivity)
                return;

            if (Mode == AuraMode.HEATMAP)
            {
                CustomRGB.ApplyHeatmap();
            }
            else if (Mode == AuraMode.BATTERY)
            {
                CustomRGB.ApplyBattery();
            }
            else if (Mode == AuraMode.AMBIENT)
            {
                CustomRGB.ApplyAmbient();
            }
        }


        public static byte[] AuraMessage(AuraMode mode, Color color, Color color2, int speed)
            => AuraProtocol.AuraMessage(mode, color, color2, speed, isWhite);

        private static void DetectBacklightType()
        {
            if (isACPI) return;

            if (IsBacklightDetected)
            {
                AsusHid.AuraProbe(false);
                return;
            }

            var response = AsusHid.AuraProbe(true);

            if (response is null || response.Length < 18) return;

            byte typeByte = response[9];
            byte year = response[10];
            byte layout = response[12];
            byte feat1 = response[13];
            byte feat2 = response[14];
            byte family = year >= 0x23 ? response[17] : (byte)0;

            const byte FEAT1_LOGO     = 0x01;
            const byte FEAT1_LIGHTBAR = 0x02;
            const byte FEAT1_VCUT     = 0x10;
            const byte FEAT1_AERO     = 0x20;
            const byte FEAT1_BUMP     = 0x40;
            const byte FEAT1_REARGLOW = 0x80;
            const byte FEAT2_DEFAULT_COLOR        = 0x04;
            const byte FEAT2_RGB_WHEEL            = 0x08;
            const byte FEAT2_ONE_ZONE_RED_EFFECT  = 0x10;
            const byte FEAT2_BIT_FORMAT_KEY_POS   = 0x40;

            string familyName = family switch
            {
                0x01 => "Strix",
                0x02 => "Flow",
                0x04 => "Zephyrus",
                0x08 => "TUF",
                0x10 => "NR2301",
                0x20 => "Desktop",
                0x00 => "(pre-2023)",
                _    => $"unknown(0x{family:X2})"
            };

            Logger.WriteLine($"Aura Probe: Type=0x{typeByte:X2} Year=0x{year:X2} Layout=0x{layout:X2} Feat1=0x{feat1:X2} Feat2=0x{feat2:X2} Family=0x{family:X2} ({familyName})");
            Logger.WriteLine($"Aura Probe Feat1 regions: Logo={(feat1 & FEAT1_LOGO) != 0} Lightbar={(feat1 & FEAT1_LIGHTBAR) != 0} Vcut={(feat1 & FEAT1_VCUT) != 0} Aero={(feat1 & FEAT1_AERO) != 0} Bump={(feat1 & FEAT1_BUMP) != 0} Rearglow={(feat1 & FEAT1_REARGLOW) != 0}");
            Logger.WriteLine($"Aura Probe Feat2 features: DefaultColor={(feat2 & FEAT2_DEFAULT_COLOR) != 0} RGBWheel={(feat2 & FEAT2_RGB_WHEEL) != 0} OneZoneRedEffect={(feat2 & FEAT2_ONE_ZONE_RED_EFFECT) != 0} PerKeyMap={(feat2 & FEAT2_BIT_FORMAT_KEY_POS) != 0}");

            BacklightType = typeByte switch
            {
                (byte)AuraBacklightType.MultiZone => AuraBacklightType.MultiZone,
                (byte)AuraBacklightType.PerKey => AuraBacklightType.PerKey,
                (byte)AuraBacklightType.SingleZone => AuraBacklightType.SingleZone,
                0x00 => AuraBacklightType.SingleZone,
                _ => AuraBacklightType.Unknown
            };

            if (!IsBacklightDetected) return;

            AppConfig.Set("backlight_type", typeByte);

            IsOldStrix = feat1 == 0 && AppConfig.IsStrix();
            if (IsOldStrix) feat1 = FEAT1_LOGO | FEAT1_LIGHTBAR;
            HasLogo = (feat1 & FEAT1_LOGO) != 0 || AppConfig.IsZ13();
            HasLightbar = (feat1 & FEAT1_LIGHTBAR) != 0;
            HasRearglow = (feat1 & (FEAT1_REARGLOW | FEAT1_VCUT)) != 0;

            isStrix4Zone = BacklightType == AuraBacklightType.MultiZone;

            if (typeByte != 0x00 && (feat2 & FEAT2_ONE_ZONE_RED_EFFECT) != 0) isWhite = true;
        }

        public static void Init()
        {
            DetectBacklightType();

            if (AppConfig.IsZ13() && !AppConfig.Is("skip_aura"))
                AsusHid.Write([AsusHid.AURA_ID, 0xC0, 0x03, 0x01], "Dynamic Lighting Init");

            if (AppConfig.IsProArt())
            {
                AsusHid.WriteInput([AsusHid.INPUT_ID, 0x05, 0x20, 0x31, 0x00, 0x08], "ProArt Init");
                //AsusHid.WriteInput([AsusHid.INPUT_ID, 0xD0, 0x4E], "ProArt Init");
                AsusHid.WriteInput([AsusHid.INPUT_ID, 0xBA, 0xC5, 0xC4], "ProArt Init");
                AsusHid.WriteInput([AsusHid.INPUT_ID, 0xD0, 0x8F, 0x01], "ProArt Init");
                AsusHid.WriteInput([AsusHid.INPUT_ID, 0xD0, 0x85, 0xFF], "ProArt Init");
                //AsusHid.WriteInput([AsusHid.INPUT_ID, 0xD0, 0x4E], "ProArt Init");
            }

            InputDispatcher.InitFNLock();
        }


        public static void SleepBrightness()
        {
            if (!AppConfig.IsSleepBacklight() || !AppConfig.Is("keyboard_sleep")) ApplyBrightness(0, "Sleep");
        }

        public static void ApplyBrightness(int brightness, string log = "Backlight")
        {
            if (brightness == 0) backlight = false;

            DirectBrightness(brightness, log);
            if (AppConfig.IsAlly()) ApplyAura();

            if (brightness > 0)
            {
                if (!backlight) initDirect = true;
                backlight = true;
                if (Mode == AuraMode.GRADIENT) ApplyAura();
            }
        }

        public static void DirectBrightness(int brightness, string log)
        {
            if (isACPI) Program.acpi.TUFKeyboardBrightness(brightness, log);
            if (AppConfig.IsAlly()) AsusHid.SetFeatureAura([AsusHid.AURA_ID, 0xBA, 0xC5, 0xC4, (byte)brightness]);
            else AsusHid.WriteInput([AsusHid.INPUT_ID, 0xBA, 0xC5, 0xC4, (byte)brightness], log);
        }

        private static void ApplyAllyPower(AuraPower flags)
        {
            AsusHid.WriteInput(AuraProtocol.AllyPowerMessage(flags), "Aura");
        }

        public static void ApplyPowerOff()
        {
            AsusHid.Write(AuraProtocol.PowerMessage(new AuraPower()));
        }

        public static void ApplyPower()
        {

            bool backlightBattery = AppConfig.IsBacklightZones() && !IsOnAc;

            AuraPower flags = new()
            {
                // Keyboard
                AwakeKeyb = backlightBattery ? AppConfig.IsOnBattery("keyboard_awake") : AppConfig.IsNotFalse("keyboard_awake"),
                BootKeyb = AppConfig.IsNotFalse("keyboard_boot"),
                SleepKeyb = AppConfig.IsNotFalse("keyboard_sleep"),
                ShutdownKeyb = AppConfig.IsNotFalse("keyboard_shutdown"),

                // Logo
                AwakeLogo = backlightBattery ? AppConfig.IsOnBattery("keyboard_awake_logo") : AppConfig.IsNotFalse("keyboard_awake_logo"),
                BootLogo = AppConfig.IsNotFalse("keyboard_boot_logo"),
                SleepLogo = AppConfig.IsNotFalse("keyboard_sleep_logo"),
                ShutdownLogo = AppConfig.IsNotFalse("keyboard_shutdown_logo"),

                // Lightbar
                AwakeBar = backlightBattery ? AppConfig.IsOnBattery("keyboard_awake_bar") : AppConfig.IsNotFalse("keyboard_awake_bar"),
                BootBar = AppConfig.IsNotFalse("keyboard_boot_bar"),
                SleepBar = AppConfig.IsNotFalse("keyboard_sleep_bar"),
                ShutdownBar = AppConfig.IsNotFalse("keyboard_shutdown_bar"),

                // Lid
                AwakeLid = backlightBattery ? AppConfig.IsOnBattery("keyboard_awake_lid") : AppConfig.IsNotFalse("keyboard_awake_lid"),
                BootLid = AppConfig.IsNotFalse("keyboard_boot_lid"),
                SleepLid = AppConfig.IsNotFalse("keyboard_sleep_lid"),
                ShutdownLid = AppConfig.IsNotFalse("keyboard_shutdown_lid"),

                // Rear Bar
                AwakeRear = backlightBattery ? AppConfig.IsOnBattery("keyboard_awake_lid") : AppConfig.IsNotFalse("keyboard_awake_lid"),
                BootRear = AppConfig.IsNotFalse("keyboard_boot_lid"),
                SleepRear = AppConfig.IsNotFalse("keyboard_sleep_lid"),
                ShutdownRear = AppConfig.IsNotFalse("keyboard_shutdown_lid")
            };

            // On Z13 back panel light is controlled by mix of different flags, so merging them together
            if (AppConfig.IsZ13())
            {
                flags.AwakeBar = flags.AwakeLid = flags.AwakeLogo;
                flags.BootBar = flags.BootLid = flags.BootLogo;
                flags.SleepBar = flags.SleepLid = flags.SleepLogo;
                flags.ShutdownBar = flags.ShutdownLid = flags.ShutdownLogo;
            }

            if (AppConfig.IsAlly())
            {
                ApplyAllyPower(flags);
                return;
            }

            AsusHid.Write(AuraProtocol.PowerMessage(flags));

            if (isACPI)
                Program.acpi.TUFKeyboardPower(
                    flags.AwakeKeyb,
                    flags.BootKeyb,
                    flags.SleepKeyb,
                    flags.ShutdownKeyb);

        }

        public static void ApplyDirect(Color[] color, bool init = false)
        {
            if (color is { Length: > 0 })
            {
                PeripheralsProvider.StreamMouseColor(color.Length > 3 ? color[3] : color[0]);
                PeripheralsProvider.StreamKeyboardColor(color.Length > 3 ? color[3] : color[0]);
            }

            if (!backlight) return;

            if (AsusLampArray.Available)
            {
                AsusLampArray.SetColors(color);
                return;
            }

            if (init || initDirect)
            {
                initDirect = false;
                AsusHid.SetFeatureAura(new byte[] { AsusHid.AURA_ID, 0xBC, (byte)(IsOldStrix ? 0 : 1) });
                Thread.Sleep(50);
            }

            var packets = AuraProtocol.DirectPackets(color, isStrix4Zone, isStrixNumpad, isStrix4ZoneFlipped);
            for (int i = 0; i < packets.Count; i++)
            {
                AsusHid.SetFeatureAura(packets[i]);
                if (i < packets.Count - 1) Thread.Sleep(1);
            }
        }

        public static void ApplyDirectLightbar(Color[] color)
        {
            if (AsusLampArray.Available) return;
            AsusHid.SetFeatureAura(AuraProtocol.LightbarPacket(color, isStrix4ZoneFlipped));
        }


        public static void ApplyDirect(Color color, bool init = false)
        {
            PeripheralsProvider.StreamMouseColor(color);
            PeripheralsProvider.StreamKeyboardColor(color);

            if (!backlight) return;

            if (isACPI)
            {
                Program.acpi.TUFKeyboardRGB(0, color, 0, null);
                return;
            }

            if (AsusLampArray.Available)
            {
                AsusLampArray.SetColor(color);
                return;
            }

            if (AppConfig.IsNoDirectRGB())
            {
                AsusHid.SetFeatureAura(AuraMessage(AuraMode.AuraStatic, color, color, 0xeb));
                AsusHid.SetFeatureAura(MESSAGE_SET);
                return;
            }

            if (isStrix)
            {
                ApplyDirect(Enumerable.Repeat(color, AURA_ZONES).ToArray(), init);
                return;
            }

            if (init || initDirect)
            {
                initDirect = false;
                //Init();
                AsusHid.SetFeatureAura(new byte[] { AsusHid.AURA_ID, 0xBC, 1 });
                Thread.Sleep(50);
            }

            AsusHid.SetFeatureAura(AuraProtocol.DirectSingleColorPacket(color));

        }

        public static Color ColorDim(Color Color, double colorDim = 1)
        {
            switch (InputDispatcher.GetBacklight())
            {
                case 1: colorDim = 0.1; break;
                case 2: colorDim = 0.3; break;
            }
            return Color.FromArgb((int)(Color.R * colorDim), (int)(Color.G * colorDim), (int)(Color.B * colorDim));
        }

        public static void ApplyRearLight()
        {
            if (!AppConfig.HasRearLight()) return;

            RearMode = (AuraMode)AppConfig.Get("rear_mode");
            SetRearColor(AppConfig.Get("rear_color"));

            int _speed = (Speed == AuraSpeed.Normal) ? 0xeb : (Speed == AuraSpeed.Fast) ? 0xf5 : 0xe1;
            AsusHid.Write(new List<byte[]> { AuraMessage(RearMode, RearColor, RearColor, _speed), MESSAGE_SET, MESSAGE_APPLY }, "Rear", AsusHid.REAR_LIGHT_PIDS);
        }

        public static void ApplyAura()
        {
            Mode = (AuraMode)AppConfig.Get("aura_mode");
            Speed = (AuraSpeed)AppConfig.Get("aura_speed");
            SetColor(AppConfig.Get("aura_color"));
            SetColor2(AppConfig.Get("aura_color2"));

            Color _Color1 = Color1;
            Color _Color2 = Color2;

            // Ally lower brightness fix
            if (AppConfig.IsAlly())
            {
                _Color1 = ColorDim(_Color1);
                _Color2 = ColorDim(_Color2);
            }

            timer.Stop();
            if (Mode != AuraMode.AUDIO && Mode != AuraMode.AUDIOPULSE) StopAudio();

            Logger.WriteLine($"AuraMode: {Mode}");

            AsusLampArray.SetMode(Mode);
            if (AsusLampArray.Probing) return;

            if (Mode == AuraMode.AUDIO || Mode == AuraMode.AUDIOPULSE)
            {
                StartAudio();
                return;
            }

            if (Mode == AuraMode.HEATMAP)
            {
                CustomRGB.ApplyHeatmap(true);
                timer.Interval = 2000;
                timer.Start();
                return;
            }

            if (Mode == AuraMode.BATTERY)
            {
                CustomRGB.ApplyBattery();
                timer.Interval = 30000;
                timer.Start();
                return;
            }

            if (Mode == AuraMode.AMBIENT)
            {
                CustomRGB.ApplyAmbient(true);
                timer.Interval = AppConfig.Get("aura_refresh", AppConfig.IsStrix() ? 100 : 300);
                timer.Start();
                return;
            }

            if (Mode == AuraMode.GRADIENT)
            {
                CustomRGB.ApplyGradient();
                return;
            }

            if (Mode == AuraMode.ZONETEST)
            {
                CustomRGB.ApplyZoneTest();
                return;
            }

            if (Mode == AuraMode.GPUMODE)
            {
                CustomRGB.ApplyGPUColor();
                return;
            }

            if (AppConfig.IsDynamicLightingOnly())
            {
                switch (mode)
                {
                    case AuraMode.AuraBreathe:
                        DynamicLightingHelper.SetEffect(
                            DynamicLightingEffect.Wave,
                            color: _Color1,
                            color2: _Color2,
                            speed: (int)Speed * 5
                            );
                        break;
                    case AuraMode.AuraColorCycle:
                    case AuraMode.AuraRainbow:
                        DynamicLightingHelper.SetEffect(
                            DynamicLightingEffect.Rainbow,
                            speed: (int)Speed * 5
                            );
                        break;
                    case AuraMode.AuraStrobe:
                        DynamicLightingHelper.SetEffect(
                            DynamicLightingEffect.Breathing,
                            color: _Color1,
                            speed: 10
                            );
                        break;
                    default:
                        DynamicLightingHelper.SetEffect(
                            DynamicLightingEffect.Solid,
                            color: _Color1
                            );
                        break;
                }
                return;
            }

            AuraSpeed effectiveSpeed = Speed;
            if (PeripheralsProvider.IsAuraSync && (Mode == AuraMode.AuraBreathe || Mode == AuraMode.AuraColorCycle))
                effectiveSpeed = AuraSpeed.Slow;

            int _speed = (effectiveSpeed == AuraSpeed.Normal) ? 0xeb : (effectiveSpeed == AuraSpeed.Fast) ? 0xf5 : 0xe1;

            PeripheralsProvider.SyncMiceWithKeyboardAura();
            PeripheralsProvider.SyncKeyboardsWithAura();
            PeripheralsProvider.SyncHeadsetsWithAura();

            AsusHid.Write(new List<byte[]> { AuraMessage(Mode, _Color1, _Color2, _speed), MESSAGE_SET, MESSAGE_APPLY }, "Aura", AsusHid.MAIN_AURA_PIDS);
            XGM.LightMode(Mode, _Color1, _Color2, _speed);

            if (isACPI)
                Program.acpi.TUFKeyboardRGB(Mode, Color1, _speed);

            ApplyRearLight();

        }

        public static void StopAudio()
        {
            AudioVisualizer.Shared.Unsubscribe(OnAudioSpectrum);
        }

        public static void StartAudio()
        {
            if (!backlight) return;

            initDirect = true;
            audioMaxes.Clear();
            lastAudioPresent = 0;
            envBrightness = 0;
            smoothedHue = 0;

            AudioVisualizer.Shared.Subscribe(OnAudioSpectrum);
        }

        private static void OnAudioSpectrum(double[] fftMag)
        {
            if (!backlight || sessionLock) return;
            if (Mode != AuraMode.AUDIO && Mode != AuraMode.AUDIOPULSE) return;

            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (Math.Abs(now - lastAudioPresent) < 50) return;
            lastAudioPresent = now;

            int bands = AURA_ZONES;
            if (fftMag.Length < bands) return;

            double[] bars = new double[bands];
            double max = 0;
            for (int i = 0; i < bands; i++)
            {
                bars[i] = Math.Sqrt(fftMag[i] * 10000);
                if (bars[i] > max) max = bars[i];
            }

            audioMaxes.Add(max);
            if (audioMaxes.Count > 100) audioMaxes.RemoveAt(0);
            double maxAvg = audioMaxes.OrderByDescending(x => x).ElementAt(audioMaxes.Count / 10);
            if (maxAvg < 1) maxAvg = 1;

            envBrightness = Math.Max(envBrightness * audioDecay, max);
            double brightness = Math.Min(1.0, envBrightness / maxAvg);

            Color c1 = Color1;
            double curvedBrightness = brightness * brightness * brightness;

            try
            {
                if (Mode == AuraMode.AUDIOPULSE)
                {
                    Color dimmed = Color.FromArgb(
                        (byte)(c1.R * curvedBrightness),
                        (byte)(c1.G * curvedBrightness),
                        (byte)(c1.B * curvedBrightness));
                    if (isStrix) ApplyDirect(Enumerable.Repeat(dimmed, AURA_ZONES).ToArray());
                    else ApplyDirect(dimmed);
                    return;
                }

                double baseHue = ColorUtils.HSV.ToHSV(c1).Hue;

                if (isStrix)
                {
                    Color[] colors = new Color[AURA_ZONES];
                    for (int i = 0; i < AURA_ZONES; i++)
                    {
                        double hue = (baseHue + (double)i / (AURA_ZONES - 1) * (2.0 / 3.0)) % 1.0;
                        double ratio = Math.Min(1.0, bars[i] / maxAvg);
                        double v = ratio * ratio * ratio;
                        colors[i] = new ColorUtils.HSV { Hue = hue, Saturation = 1.0, Value = v }.ToRGB();
                    }
                    ApplyDirect(colors);
                }
                else
                {
                    int dominant = 1;
                    double dominantWeighted = bars[1];
                    for (int i = 2; i < bands; i++)
                    {
                        double w = bars[i] * (1 + (i - 1) * 0.15);
                        if (w > dominantWeighted) { dominantWeighted = w; dominant = i; }
                    }
                    if (max > maxAvg * 0.3)
                    {
                        double targetHue = (baseHue + (dominant - 1) / (double)(bands - 2) * (2.0 / 3.0)) % 1.0;
                        smoothedHue = smoothedHue * 0.6 + targetHue * 0.4;
                    }

                    ApplyDirect(new ColorUtils.HSV { Hue = smoothedHue, Saturation = 1.0, Value = curvedBrightness }.ToRGB());
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Aura audio: " + ex.Message);
            }
        }


        public static class CustomRGB
        {

            static int tempFreeze = AppConfig.Get("temp_freeze", 20);
            static int tempCold = AppConfig.Get("temp_cold", 40);
            static int tempWarm = AppConfig.Get("temp_warm", 65);
            static int tempHot = AppConfig.Get("temp_hot", 90);

            static Color colorFreeze = ColorTranslator.FromHtml(AppConfig.GetString("color_freeze", "#0000FF"));
            static Color colorCold = ColorTranslator.FromHtml(AppConfig.GetString("color_cold", "#008000"));
            static Color colorWarm = ColorTranslator.FromHtml(AppConfig.GetString("color_warm", "#FFFF00"));
            static Color colorHot = ColorTranslator.FromHtml(AppConfig.GetString("color_hot", "#FF0000"));

            static float battLow = 20f;
            static float battMid = 60f;
            static float battHigh = 100f;

            static Color colorLow = Color.Red;
            static Color colorMid = Color.Yellow;
            static Color colorHigh = Color.Lime;

            static Color colorUltimate = ColorTranslator.FromHtml(AppConfig.GetString("color_ultimate", "#FF0000"));
            static Color colorStandard = ColorTranslator.FromHtml(AppConfig.GetString("color_standard", "#FFFF00"));
            static Color colorEco = ColorTranslator.FromHtml(AppConfig.GetString("color_eco", "#008000"));

            public static void ApplyGradient()
            {
                if (!isStrix && !isStrix4Zone)
                {
                    ApplyDirect(Aura.Color1, true);
                    return;
                }

                Color[] colors = new Color[AURA_ZONES];

                for (int z = 0; z < 4; z++)
                {
                    float t = z / 3f;
                    colors[z] = ColorUtils.GetWeightedAverage(Aura.Color2, Aura.Color1, t);
                }

                int[] lightbarOrder = AsusLampArray.Available ? new int[] { 4, 5, 6, 7 } : new int[] { 7, 6, 4, 5 };
                for (int i = 0; i < lightbarOrder.Length; i++)
                {
                    float t = i / 3f;
                    colors[lightbarOrder[i]] = ColorUtils.GetWeightedAverage(Aura.Color2, Aura.Color1, t);
                }

                ApplyDirect(colors, true);
                ApplyDirect(colors);
            }

            // Zone 0 red, 1 orange, 2 yellow, 3 green, 4 cyan, 5 blue, 6 magenta, 7 white.
            public static void ApplyZoneTest()
            {
                Color[] colors = new Color[]
                {
                    Color.FromArgb(0xFF, 0x00, 0x00),
                    Color.FromArgb(0xFF, 0x80, 0x00),
                    Color.FromArgb(0xFF, 0xFF, 0x00),
                    Color.FromArgb(0x00, 0xFF, 0x00),
                    Color.FromArgb(0x00, 0xFF, 0xFF),
                    Color.FromArgb(0x00, 0x00, 0xFF),
                    Color.FromArgb(0xFF, 0x00, 0xFF),
                    Color.FromArgb(0xFF, 0xFF, 0xFF),
                };

                ApplyDirect(colors, true);
                ApplyDirectLightbar(colors);
            }

            public static void ApplyGPUColor(int gpuMode = -1)
            {
                if ((AuraMode)AppConfig.Get("aura_mode") != AuraMode.GPUMODE) return;

                Color color;

                if (gpuMode < 0) gpuMode = GPUModeControl.gpuMode;
                switch (gpuMode)
                {
                    case AsusACPI.GPUModeUltimate:
                        color = colorUltimate;
                        break;
                    case AsusACPI.GPUModeEco:
                        color = colorEco;
                        break;
                    default:
                        color = colorStandard;
                        break;
                }

                PeripheralsProvider.StreamMouseColor(color);
                PeripheralsProvider.StreamKeyboardColor(color);
                if (isACPI) Program.acpi.TUFKeyboardRGB(AuraMode.AuraStatic, color, 0xeb, $"TUF RGB GPU {gpuMode}");
                AsusHid.Write(new List<byte[]> { AuraMessage(AuraMode.AuraStatic, color, color, 0xeb), MESSAGE_APPLY, MESSAGE_SET });

            }

            public static void ApplyHeatmap(bool init = false)
            {
                float cpuTemp = (float)HardwareControl.GetCPUTemp();
                Color color = colorFreeze;

                if (cpuTemp < tempCold) color = ColorUtils.GetWeightedAverage(colorFreeze, colorCold, ((float)cpuTemp - tempFreeze) / (tempCold - tempFreeze));
                else if (cpuTemp < tempWarm) color = ColorUtils.GetWeightedAverage(colorCold, colorWarm, ((float)cpuTemp - tempCold) / (tempWarm - tempCold));
                else if (cpuTemp < tempHot) color = ColorUtils.GetWeightedAverage(colorWarm, colorHot, ((float)cpuTemp - tempWarm) / (tempHot - tempWarm));
                else color = colorHot;

                ApplyDirect(color, init);
            }

            public static void ApplyBattery()
            {
                float battery = (float)HardwareControl.GetBatteryChargePercentage();
                Color color = colorLow;

                if (battery < battLow)
                {
                    color = colorLow;
                }
                else if (battery < battMid)
                {
                    // Low → Mid
                    float t = (battery - battLow) / (battMid - battLow);
                    color = ColorUtils.GetWeightedAverage(colorLow, colorMid, t);
                }
                else if (battery < battHigh)
                {
                    // Mid → High
                    float t = (battery - battMid) / (battHigh - battMid);
                    color = ColorUtils.GetWeightedAverage(colorMid, colorHigh, t);
                }
                else
                {
                    color = colorHigh;
                }

                if (AppConfig.IsAlly()) color = ColorDim(color);
                PeripheralsProvider.StreamMouseColor(color);
                PeripheralsProvider.StreamKeyboardColor(color);
                AsusHid.Write(new List<byte[]> { AuraMessage(AuraMode.AuraStatic, color, color, 0xeb), MESSAGE_APPLY, MESSAGE_SET });
                if (isACPI) Program.acpi.TUFKeyboardRGB(AuraMode.AuraStatic, color, 0xeb);
            }

            public static void ApplyAmbient(bool init = false)
            {
                if (!backlight || sessionLock) return;

                var capture = ScreenCapture.Current;
                if (capture is null || !capture.IsSupported) return;
                if (capture.IsWindowMoveSize()) return;

                var bound = capture.GetPrimaryScreenBounds();
                bound.Y += bound.Height / 3;
                bound.Height -= (int)Math.Round(bound.Height * (0.33f + 0.022f)); // cut 1/3 of the top screen + windows panel

                PixelBuffer? screen_low = capture.Capture(bound, 512, 288);   //quality decreases greatly if it is less 512 ;
                if (screen_low is null) return;
                PixelBuffer screeb_pxl = screen_low.Resize(4, 2);              // 4x2 zone. top for keyboard and bot for lightbar;

                int zones = AURA_ZONES;

                if (isStrix) // laptop with lightbar
                {
                    var mid_left = ColorUtils.GetMidColor(screeb_pxl.GetPixel(0, 1), screeb_pxl.GetPixel(1, 1));
                    var mid_right = ColorUtils.GetMidColor(screeb_pxl.GetPixel(2, 1), screeb_pxl.GetPixel(3, 1));

                    AmbientData.Colors[4].RGB = ColorUtils.HSV.UpSaturation(screeb_pxl.GetPixel(1, 1)); // left bck
                    AmbientData.Colors[5].RGB = ColorUtils.HSV.UpSaturation(mid_left);  // center left
                    AmbientData.Colors[6].RGB = ColorUtils.HSV.UpSaturation(mid_right); // center right
                    AmbientData.Colors[7].RGB = ColorUtils.HSV.UpSaturation(screeb_pxl.GetPixel(3, 1)); // right bck

                    for (int i = 0; i < 4; i++) // keyboard
                        AmbientData.Colors[i].RGB = ColorUtils.HSV.UpSaturation(screeb_pxl.GetPixel(i, 0));
                }
                else
                {
                    zones = 1;
                    AmbientData.Colors[0].RGB = ColorUtils.HSV.UpSaturation(screeb_pxl.AverageColor(), (float)0.3);
                }

                bool is_fresh = init;

                for (int i = 0; i < zones; i++)
                {
                    if (AmbientData.result[i].ToArgb() != AmbientData.Colors[i].RGB.ToArgb()) is_fresh = true;
                    AmbientData.result[i] = AmbientData.Colors[i].RGB;
                }

                if (is_fresh)
                {
                    if (isStrix) ApplyDirect(AmbientData.result, init);
                    else ApplyDirect(AmbientData.result[0], init);
                }

            }

            static class AmbientData
            {
                static public Color[] result = new Color[AURA_ZONES];
                static public ColorUtils.SmoothColor[] Colors = Enumerable.Repeat(0, AURA_ZONES).
                    Select(h => new ColorUtils.SmoothColor()).ToArray();
            }

        }

    }
}
