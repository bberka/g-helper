namespace GHelper.Platform.Windows.Asus;

/// <summary>
/// Pure buffer layout of the ATKACPI DeviceIoControl protocol (legacy app/AsusACPI.cs CallMethod / DeviceGetLarge).
/// Kept free of any OS call so it is unit-testable on every platform.
/// </summary>
internal static class AtkAcpiProtocol
{
    public const string DevicePath = @"\\.\\ATKACPI";
    public const uint ControlCode = 0x0022240C;
    public const uint EventRegisterControlCode = 0x222400;
    public const uint EventReadControlCode = 0x222408;

    public const uint DSTS = 0x53545344; // "DSTS"
    public const uint DEVS = 0x53564544; // "DEVS"
    public const uint INIT = 0x54494E49; // "INIT"
    public const uint WDOG = 0x474F4457; // "WDOG"

    public const int OutBufferSize = 16;

    /// <summary>[method:u32][argsLength:u32][args...]</summary>
    public static byte[] BuildCall(uint method, byte[] args)
    {
        byte[] buffer = new byte[8 + args.Length];
        BitConverter.GetBytes(method).CopyTo(buffer, 0);
        BitConverter.GetBytes((uint)args.Length).CopyTo(buffer, 4);
        Array.Copy(args, 0, buffer, 8, args.Length);
        return buffer;
    }

    public static byte[] BuildDsts(uint deviceId, uint status = 0)
    {
        byte[] args = new byte[8];
        BitConverter.GetBytes(deviceId).CopyTo(args, 0);
        BitConverter.GetBytes(status).CopyTo(args, 4);
        return BuildCall(DSTS, args);
    }

    public static byte[] BuildDevs(uint deviceId, int status)
    {
        byte[] args = new byte[8];
        BitConverter.GetBytes(deviceId).CopyTo(args, 0);
        BitConverter.GetBytes((uint)status).CopyTo(args, 4);
        return BuildCall(DEVS, args);
    }

    public static byte[] BuildDevs(uint deviceId, byte[] parameters)
    {
        byte[] args = new byte[4 + parameters.Length];
        BitConverter.GetBytes(deviceId).CopyTo(args, 0);
        parameters.CopyTo(args, 4);
        return BuildCall(DEVS, args);
    }

    public static byte[] BuildInit() => BuildCall(INIT, new byte[8]);

    public static byte[] BuildWatchDog() => BuildCall(WDOG, new byte[8]);

    /// <summary>DSTS with extra input padding: [DSTS][4+extraIn][deviceId][extraIn zero bytes].</summary>
    public static byte[] BuildLargeDsts(uint deviceId, int extraIn = 8)
    {
        byte[] buffer = new byte[8 + 4 + extraIn];
        BitConverter.GetBytes(DSTS).CopyTo(buffer, 0);
        BitConverter.GetBytes((uint)(4 + extraIn)).CopyTo(buffer, 4);
        BitConverter.GetBytes(deviceId).CopyTo(buffer, 8);
        return buffer;
    }

    /// <summary>Legacy DeviceGet: status dword minus 0x10000 (negative = unsupported).</summary>
    public static int DeviceGetValue(byte[] outBuffer) => BitConverter.ToInt32(outBuffer, 0) - 65536;

    /// <summary>Legacy DeviceSet: first dword of the result, 1 = OK.</summary>
    public static int DeviceSetResult(byte[] outBuffer) => BitConverter.ToInt32(outBuffer, 0);

    /// <summary>The 8 byte payload of the "register event handle" control code (little endian handle value).</summary>
    public static byte[] BuildEventRegistration(long eventHandle)
    {
        byte[] data = new byte[8];
        BitConverter.GetBytes(eventHandle).CopyTo(data, 0);
        return data;
    }
}
