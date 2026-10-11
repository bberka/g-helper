using System.Runtime.InteropServices;

namespace GHelper.App;

/// <summary>
/// The executable is a GUI (WinExe) application on Windows, so "--version" would print into the void when started from
/// a terminal. Attach to the parent console for the few command line only code paths.
/// </summary>
internal static class ConsoleAttach
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    public static void Attach()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            if (!AttachConsole(AttachParentProcess)) return;

            // The CRT handles were captured at start-up (nothing); reopen the standard streams on the console.
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdout);
            var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetError(stderr);
        }
        catch
        {
            // best effort
        }
    }
}
