using System.Diagnostics;
using GHelper.Platform;

public static class Logger
{
    // Platform services must be registered before the first log call (the app head does it first thing in Main).
    public static string appPath = PlatformServices.IsRegistered
        ? (PlatformServices.Current.System.IsRunningAsSystem ? PlatformServices.Current.System.SharedConfigDirectory : PlatformServices.Current.System.ConfigDirectory)
        : Path.Combine(Path.GetTempPath(), "GHelper");
    public static string logFile = Path.Combine(appPath, "log.txt");

    private static readonly Random _random = new Random();
    private static readonly object _lock = new object();

    public static void WriteLine(string logMessage)
    {
        Debug.WriteLine($"{DateTime.Now}: {logMessage}");

        lock (_lock)
        {
            try
            {
                if (!Directory.Exists(appPath)) Directory.CreateDirectory(appPath);
                using (StreamWriter w = File.AppendText(logFile))
                {
                    w.WriteLine($"{DateTime.Now}: {logMessage}");
                    w.Close();
                }
            }
            catch { }

            if (_random.Next(100) == 1) Cleanup();
        }
    }

    public static void Cleanup()
    {
        try
        {
            var file = File.ReadAllLines(logFile);
            int skip = Math.Max(0, file.Count() - 2000);
            File.WriteAllLines(logFile, file.Skip(skip).ToArray());
        }
        catch { }
    }

}
