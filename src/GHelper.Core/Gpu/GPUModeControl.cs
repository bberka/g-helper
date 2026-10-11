using GHelper.Display;
using GHelper.Gpu.NVidia;
using GHelper.Platform;
using GHelper.USB;

namespace GHelper.Gpu
{
    /// <summary>
    /// Eco / Standard / Ultimate GPU mode logic. Legacy <c>settingsForm.VisualiseGPUMode / LockGPUModes / HideGPUModes /
    /// VisualiseGPUButtons</c> calls became state properties on this class + <c>Program.Ui.Refresh("gpu")</c>:
    /// the UI re-reads <see cref="EcoSupported"/>, <see cref="MuxSupported"/>, <see cref="ModesHidden"/>,
    /// <see cref="IsLocked"/>/<see cref="LockText"/>, <see cref="RequestedVisualMode"/> and <see cref="gpuError"/>.
    /// </summary>
    public class GPUModeControl
    {
        public static int gpuMode;
        public static bool? gpuExists = null;

        static bool nvRestartPending;

        // ---- state rendered by the UI (legacy SettingsForm.VisualiseGPU* arguments) ----------------------

        /// <summary>Eco flag readable (legacy VisualiseGPUButtons eco). False = hide Eco / Optimized, show "stop GPU apps".</summary>
        public static bool EcoSupported { get; private set; } = true;

        /// <summary>MUX switch readable (legacy VisualiseGPUButtons ultimate). False = hide Ultimate.</summary>
        public static bool MuxSupported { get; private set; } = true;

        /// <summary>No GPU mode switching on this device (legacy HideGPUModes).</summary>
        public static bool ModesHidden { get; private set; }

        /// <summary>With <see cref="ModesHidden"/>: the GPU panel is still shown (a GPU exists, e.g. fan sensor present).</summary>
        public static bool GpuPanelVisible { get; private set; } = true;

        /// <summary>Mode buttons are disabled while a switch is in progress (legacy LockGPUModes).</summary>
        public static bool IsLocked { get; private set; }

        /// <summary>Label shown while locked, null = default "GPU Mode: Changing ...".</summary>
        public static string? LockText { get; private set; }

        /// <summary>Mode to highlight (legacy VisualiseGPUMode(mode)); -1 = read AppConfig "gpu_mode".</summary>
        public static int RequestedVisualMode { get; private set; } = -1;

        private void VisualiseGPUMode(int mode = -1)
        {
            RequestedVisualMode = mode;
            IsLocked = false;
            LockText = null;
            Program.Ui.Refresh("gpu");
        }

        private void LockGPUModes(string? text = null)
        {
            IsLocked = true;
            LockText = text;
            Program.Ui.Refresh("gpu");
        }

        private void HideGPUModes(bool gpuExists)
        {
            ModesHidden = true;
            GpuPanelVisible = gpuExists;
            Program.Ui.Refresh("gpu");
        }

        public GPUModeControl()
        {
        }

        public void InitGPUMode()
        {
            if (AppConfig.NoGpu())
            {
                HideGPUModes(false);
                return;
            }

            int eco = Program.acpi.DeviceGet(AsusACPI.GPUEco);
            int mux = Program.acpi.DeviceGet(AsusACPI.GPUMux);

            Logger.WriteLine("Eco flag : " + eco);
            Logger.WriteLine("Mux flag : " + mux);

            if (eco == 1 && HardwareControl.GpuControl?.IsValid == true)
            {
                Logger.WriteLine("Eco half-state");
                if (AppConfig.IsEcoBootFix())
                {
                    HardwareControl.DisposeGpuControl();
                    Task.Run(() => Program.acpi.DeviceSet(AsusACPI.GPUEco, eco, "GPUEco Force Fix"));
                }
            }

            EcoSupported = eco >= 0;
            MuxSupported = mux >= 0;

            if (mux == 0)
            {
                gpuMode = AsusACPI.GPUModeUltimate;
            }
            else
            {
                if (eco == 1)
                    gpuMode = AsusACPI.GPUModeEco;
                else
                    gpuMode = AsusACPI.GPUModeStandard;

                // GPU mode not supported
                if (eco < 0 && mux < 0)
                {
                    if (gpuExists is null) gpuExists = Program.acpi.GetFan(AsusFan.GPU) >= 0;
                    HideGPUModes((bool)gpuExists);
                }
            }

            AppConfig.Set("gpu_mode", gpuMode);
            VisualiseGPUMode(gpuMode);

            Aura.CustomRGB.ApplyGPUColor(gpuMode);

            CheckGpuError();

        }



        public void SetGPUMode(int GPUMode, int auto = 0)
        {

            int CurrentGPU = AppConfig.Get("gpu_mode");
            AppConfig.Set("gpu_auto", auto);

            if (CurrentGPU == GPUMode)
            {
                VisualiseGPUMode();
                return;
            }

            var restart = false;
            var changed = false;

            int status;

            if (CurrentGPU == AsusACPI.GPUModeUltimate)
            {
                if (Program.Ui.Confirm(Properties.Strings.AlertUltimateTitle, Properties.Strings.AlertUltimateOff))
                {
                    status = Program.acpi.DeviceSet(AsusACPI.GPUMux, 1, "GPUMux");
                    restart = true;
                    changed = true;
                }
            }
            else if (GPUMode == AsusACPI.GPUModeUltimate)
            {
                if (Program.acpi.DeviceGet(AsusACPI.GPUMux) < 0)
                {
                    Logger.WriteLine("Mux not supported");
                    VisualiseGPUMode();
                    return;
                }

                if (Program.Ui.Confirm(Properties.Strings.AlertUltimateTitle, Properties.Strings.AlertUltimateOn))
                {
                    Program.acpi.SetGPUEco(0);
                    Thread.Sleep(500);

                    int eco = Program.acpi.DeviceGet(AsusACPI.GPUEco);
                    Logger.WriteLine("Eco flag : " + eco);
                    if (eco == 1)
                    {
                        VisualiseGPUMode();
                        return;
                    }

                    status = Program.acpi.DeviceSet(AsusACPI.GPUMux, 0, "GPUMux");
                    restart = true;
                    changed = true;
                }

            }
            else if (GPUMode == AsusACPI.GPUModeEco)
            {
                VisualiseGPUMode(GPUMode);
                SetGPUEco(1);
                changed = true;
            }
            else if (GPUMode == AsusACPI.GPUModeStandard)
            {
                VisualiseGPUMode(GPUMode);
                SetGPUEco(0);
                changed = true;
            }

            if (changed)
            {
                AppConfig.Set("gpu_mode", GPUMode);
            }

            if (restart)
            {
                VisualiseGPUMode();
                SystemReboot.Reboot();
            }

        }



        public void SetGPUEco(int eco)
        {

            LockGPUModes();

            Task.Run(async () =>
            {

                int status = 1;

                Program.modeControl.WaitForApply();

                if (eco == 1)
                {
                    HardwareControl.KillGPUApps();
                    HardwareControl.DisposeGpuControl();
                    if (AppConfig.IsNVPlatform()) NvidiaGpuControl.StopNVService();
                }

                Logger.WriteLine($"Running eco command {eco}");

                try
                {

                    status = Program.acpi.SetGPUEco(eco);
                    await Task.Delay(TimeSpan.FromMilliseconds(AppConfig.Get("refresh_delay", 500)));

                    Program.Ui.Post(() =>
                    {
                        InitGPUMode();
                        ScreenControl.AutoScreen();
                    });

                    if (eco == 0)
                    {
                        if (AppConfig.IsNVPlatform() || nvRestartPending)
                        {
                            LockGPUModes(Properties.Strings.RestartingNVServices);
                            await Task.Delay(TimeSpan.FromMilliseconds(AppConfig.Get("nv_delay", 5000)));
                            if (AppConfig.IsNVPlatform()) NvidiaGpuControl.RestartNVService();
                            else NvidiaGpuControl.RestartNvContainer();
                            nvRestartPending = false;
                            Program.Ui.Post(() => InitGPUMode());
                            await Task.Delay(TimeSpan.FromMilliseconds(1000));
                        }

                        await HardwareControl.RecreateGpuControlWithRetry(3, 2);
                        if (HardwareControl.GpuControl is null) await HardwareControl.RecreateGpuControlWithRetry(3, 5);
                        CheckGpuError();
                        CheckStandardHalfState();
                    }

                    if (AppConfig.IsModeReapply())
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(1000));
                        Program.modeControl.AutoPerformance();
                    } else
                    {
                        Program.modeControl.SetGPUClocks(false);
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Error setting GPU Eco: " + ex.Message);
                }

            });


        }

        public static bool IsPlugged() =>
            Program.currentSource == Program.PowerSource.Barrel ||
            (Program.currentSource == Program.PowerSource.USBC && !AppConfig.Is("optimized_usbc"));

        public static bool suspended = false;

        public bool AutoGPUMode(bool optimized = false, int delay = 0)
        {

            bool GpuAuto = AppConfig.Is("gpu_auto");
            bool ForceGPU = AppConfig.IsForceSetGPUMode() && !GpuAuto;

            int GpuMode = AppConfig.Get("gpu_mode");

            if (!GpuAuto && !ForceGPU) return false;

            if (suspended)
            {
                Logger.WriteLine("Skipping GPU Mode switch: Suspend");
                return false;
            }

            int eco = Program.acpi.DeviceGet(AsusACPI.GPUEco);
            int mux = Program.acpi.DeviceGet(AsusACPI.GPUMux);

            if (mux == 0)
            {
                if (optimized) SetGPUMode(AsusACPI.GPUModeStandard, 1);
                return false;
            }
            else
            {

                if (eco == 1)
                    if ((GpuAuto && IsPlugged()) || (ForceGPU && GpuMode == AsusACPI.GPUModeStandard))
                    {
                        if (delay > 0) Thread.Sleep(delay);
                        SetGPUEco(0);
                        return true;
                    }
                if (eco == 0)
                    if ((GpuAuto && !IsPlugged()) || (ForceGPU && GpuMode == AsusACPI.GPUModeEco))
                    {

                        if (Program.acpi.IsXGConnected()) return false;
                        if (HardwareControl.IsUsedGPU())
                        {
                            if (!Program.Ui.Confirm(Properties.Strings.AlertDGPUTitle, Properties.Strings.AlertDGPU)) return false;
                        }
                        else if (GpuAuto && Program.acpi.IsExternalDisplayConnected())
                        {
                            if (!Program.Ui.Confirm(Properties.Strings.AlertDGPUTitle, Properties.Strings.AlertExternalDisplay)) return false;
                        }

                        if (delay > 0) Thread.Sleep(delay);
                        SetGPUEco(1);
                        return true;
                    }
            }

            return false;

        }


        public void ToggleXGM(bool silent = false)
        {

            Task.Run(async () =>
            {
                LockGPUModes();

                if (Program.acpi.DeviceGet(AsusACPI.GPUXG) == 1)
                {
                    XGM.Reset();
                    HardwareControl.KillGPUApps();

                    if (silent)
                    {
                        Program.acpi.DeviceSet(AsusACPI.GPUXG, 0, "GPU XGM");
                        await Task.Delay(TimeSpan.FromSeconds(15));
                    }
                    else
                    {
                        if (Program.Ui.Confirm("Disabling XG Mobile", "Did you close all applications running on XG Mobile?"))
                        {
                            Program.acpi.DeviceSet(AsusACPI.GPUXG, 0, "GPU XGM");
                            await Task.Delay(TimeSpan.FromSeconds(15));
                            HardwareControl.RecreateGpuControl();
                        }
                    }
                }
                else
                {

                    if (AppConfig.Is("xgm_special"))
                        Program.acpi.DeviceSet(AsusACPI.GPUXG, 0x101, "GPU XGM");
                    else
                        Program.acpi.DeviceSet(AsusACPI.GPUXG, 1, "GPU XGM");

                    XGM.Init();

                    await Task.Delay(TimeSpan.FromSeconds(15));
                    await HardwareControl.RecreateGpuControlWithRetry(6, 5);

                    if (AppConfig.IsApplyFans())
                        XGM.SetFan(AppConfig.GetFanConfig(AsusFan.XGM));

                }

                Program.Ui.Post(() =>
                {
                    InitGPUMode();
                });
            });
        }

        public void KillGPUApps()
        {
            if (HardwareControl.GpuControl is not null)
            {
                HardwareControl.GpuControl.KillGPUApps();
            }
        }

        public void CaptureNvBootState()
        {
            nvRestartPending = Program.acpi.IsNVidiaGPU() && Program.acpi.DeviceGet(AsusACPI.GPUEco) == 1;
        }

        public void CheckStandardHalfState()
        {
            if (gpuMode != AsusACPI.GPUModeStandard || HardwareControl.GpuControl is not null) return;

            Logger.WriteLine("Standard half-state");
            if (!AppConfig.IsStandardForceFix()) return;

            Task.Run(async () =>
            {
                Program.acpi.DeviceSet(AsusACPI.GPUEco, 0, "GPUStandard Force Fix");
                await Task.Delay(TimeSpan.FromMilliseconds(AppConfig.Get("nv_delay", 5000)));
                HardwareControl.RecreateGpuControl();
            });
        }

        public void StandardModeFix()
        {
            if (!AppConfig.IsStandardModeFix()) return;
            if (Program.acpi.DeviceGet(AsusACPI.GPUMux) == 0) return; // Ultimate mode

            Logger.WriteLine("Forcing Standard Mode on shutdown");
            Program.acpi.SetGPUEco(0);
        }

        public static string? gpuError = null;

        public static void CheckGpuError() => Task.Run(() =>
        {
            string? error = PlatformServices.Current.Vendor.GetGpuError();
            if (gpuError == error) return;
            gpuError = error;
            if (error != null) Logger.WriteLine(error);
            Program.Ui.Refresh("gpu");
        });

    }

    /// <summary>"Restart the computer in one second" for the mux / FHD switches that need a reboot.</summary>
    internal static class SystemReboot
    {
        public static void Reboot()
        {
            try
            {
                var process = PlatformServices.Current.Process;
                if (PlatformServices.Current.Kind == PlatformKind.Windows)
                    process.RunCommand("shutdown", "/r /t 1");
                else
                    process.RunCommand("systemctl", "reboot");
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Reboot failed: " + ex.Message);
            }
        }
    }
}
