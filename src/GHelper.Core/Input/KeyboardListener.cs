using GHelper.USB;
using HidSharp;

namespace GHelper.Input
{
    public class KeyboardListener
    {

        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        Action<int> _handler;
        HidStream? input;

        static bool isAlly = AppConfig.IsAlly();

        public KeyboardListener(Action<int> KeyHandler)
        {
            _handler = KeyHandler;
            var task = Task.Run(() => { while (Listen()) { } });
        }

        private bool Listen()
        {

            try { input?.Dispose(); } catch { }
            input = AsusHid.FindHidStream(AsusHid.INPUT_ID);

            // Fallback
            int count = 0;

            while (input == null && count++ < 10)
            {
                Thread.Sleep(1000);
                input = AsusHid.FindHidStream(AsusHid.INPUT_ID);
            }

            if (input == null)
            {
                Logger.WriteLine($"Input device not found");
                return false;
            }

            AsusHid.InitInput();
            Logger.WriteLine($"Input: {input.Device.DevicePath}");

            try
            {
                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {

                    // Emergency break
                    if (input == null || !input.CanRead)
                    {
                        Logger.WriteLine("Listener terminated");
                        break;
                    }

                    input.ReadTimeout = int.MaxValue;

                    var data = input.Read();
                    if (cancellationTokenSource.Token.IsCancellationRequested) break;
                    if (AsusKeyReport.TryGetKey(data, isAlly, out int key))
                    {
                        if (key > 0) Logger.WriteLine($"Key: {key}");
                        _handler(key);
                    }
                }

                Logger.WriteLine("Listener stopped");

            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Listener exited: {ex.Message}");
                if (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    Thread.Sleep(300);
                    Logger.WriteLine($"Restarting listener");
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            cancellationTokenSource?.Cancel();
            input?.Dispose();
        }
    }
}
