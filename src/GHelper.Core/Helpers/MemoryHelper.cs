using GHelper.Platform;

namespace GHelper.Helpers
{
    public static class MemoryHelper
    {
        public static void TrimAfter(Task? prerequisite = null, TimeSpan? timeout = null)
        {
            Task.Run(async () =>
            {
                if (prerequisite != null)
                {
                    try
                    {
                        await prerequisite.WaitAsync(timeout ?? TimeSpan.FromSeconds(3));
                    }
                    catch { }
                }

                Trim();
            });
        }

        private static void Trim()
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);

            if (!AppConfig.IsNotFalse("gc")) return;

            try
            {
                // Windows: SetProcessWorkingSetSize(-1, -1). No-op elsewhere.
                PlatformServices.Current.Process.TrimMemory();
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Trim memory: " + ex.Message);
            }
        }
    }
}
