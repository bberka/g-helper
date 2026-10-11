using GHelper.Platform;

namespace GHelper.Display
{
    /// <summary>
    /// Panel ELMB (extreme low motion blur) switch over the DisplayPort AUX channel. The Intel ControlLib / NVAPI
    /// access is platform code (<see cref="IDisplayService.ElmbAccess"/>); only the model gate lives here.
    /// </summary>
    public static class ScreenELMB
    {
        static int Aux(int value)
        {
            if (!AppConfig.IsELMB()) return -1;
            return PlatformServices.Current.Display.ElmbAccess(value);
        }

        public static int Get() => Aux(-1);

        public static void Set(int status) => Logger.WriteLine($"ELMB: set {status} result {Aux(status)}");
    }
}
