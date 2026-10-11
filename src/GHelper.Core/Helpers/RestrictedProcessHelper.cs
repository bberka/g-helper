using GHelper.Platform;

namespace GHelper.Helpers;

public static class RestrictedProcessHelper
{
    /// <summary>Runs a command as a non-elevated version of the current user (Windows: via cmd.exe + restricted token).</summary>
    public static void RunAsRestrictedUser(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        PlatformServices.Current.Process.RunAsRestrictedUser(command);
    }
}
