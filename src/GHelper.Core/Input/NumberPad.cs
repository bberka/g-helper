using GHelper.Platform;

namespace GHelper.Input
{
    /// <summary>ASUS touchpad number pad switch (Windows driver \\.\AsusTPPC). Request layout is platform neutral and tested.</summary>
    public static class NumberPad
    {
        const int BufferSize = 0x800;

        /// <summary>Request buffer of the AsusTPPC IOCTL: header 0x104, size, 0xFF0000FF, function, 0xC, value (dwords).</summary>
        public static byte[] BuildRequest(int function, int value)
        {
            byte[] buffer = new byte[BufferSize];
            BitConverter.GetBytes(0x104).CopyTo(buffer, 0);
            BitConverter.GetBytes(buffer.Length).CopyTo(buffer, 4);
            BitConverter.GetBytes(0xFF0000FF).CopyTo(buffer, 8);
            BitConverter.GetBytes(function).CopyTo(buffer, 12);
            BitConverter.GetBytes(0xC).CopyTo(buffer, 16);
            BitConverter.GetBytes(value).CopyTo(buffer, 20);
            return buffer;
        }

        private static INumberPadDriver? Driver
            => PlatformServices.IsRegistered ? PlatformServices.Current.Input as INumberPadDriver : null;

        private static bool Ioctl(uint code, int function, ref int value)
        {
            var driver = Driver;
            if (driver is null) return false;

            byte[] buffer = BuildRequest(function, value);
            bool status = driver.NumberPadIoctl(code, buffer);
            value = BitConverter.ToInt32(buffer, 20);
            return status;
        }

        public static void Init()
        {
            if (AppConfig.Get("numberpad") == 0) Set(false);
        }

        public static int Get()
        {
            int value = 0;
            bool status = Ioctl(0x221444, 6, ref value);
            Logger.WriteLine($"NumberPad Get = {value} : " + (status ? "OK" : "Error"));
            if (!status) return -1;
            return value == 1 ? 1 : value == 2 ? 0 : -1;
        }

        public static void Set(bool enabled)
        {
            int value = enabled ? 1 : 2;
            bool status = Ioctl(0x2215EC, 7, ref value);
            if (status) AppConfig.Set("numberpad", enabled ? 1 : 0);
            Logger.WriteLine($"NumberPad Set = {(enabled ? 1 : 2)} : " + (status ? "OK" : "Error"));
        }
    }
}
