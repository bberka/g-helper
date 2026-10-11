using GHelper.Platform;

namespace GHelper.Helpers
{
    /// <summary>Legacy Audio (NAudio / Core Audio) as a facade over <see cref="IAudioService"/>.</summary>
    public static class Audio
    {
        private static IAudioService Svc => PlatformServices.Current.Audio;

        public static bool IsSupported => Svc.IsSupported;

        public static void SubscribeMute(Action<bool> onMuteChange)
        {
            try
            {
                Svc.SubscribeMute(onMuteChange);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Error subscribing to volume notifications: " + ex.Message);
            }
        }

        public static bool ToggleMicMute() => Svc.ToggleMicMute();

        public static bool IsMicMuted()
        {
            try
            {
                return Svc.IsMicMuted();
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Error checking mic mute status: " + ex.Message);
                return false; // Assume not muted in case of error
            }
        }

        public static bool IsMuted()
        {
            try
            {
                return Svc.IsMuted();
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Error checking mute status: " + ex.Message);
                return false; // Assume not muted in case of error
            }
        }
    }
}
