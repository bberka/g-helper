using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Minimal NVML (NVIDIA Management Library) binding. The native library is resolved at runtime with
/// <see cref="NativeLibrary"/> (nvml.dll on Windows, libnvidia-ml.so.1 on Linux), so a machine without an NVIDIA driver
/// simply reports "unavailable": Init() returns false and every query returns null / false, never throws.
/// </summary>
public static unsafe class NvmlHelper
{
    private const int NVML_SUCCESS = 0;
    private const uint NVML_TEMPERATURE_GPU = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct nvmlMemory_t { public ulong total; public ulong free; public ulong used; }

    [StructLayout(LayoutKind.Sequential)]
    private struct nvmlUtilization_t { public uint gpu; public uint memory; }

    // nvmlProcessInfo_t (v2/v3): pid, usedGpuMemory, gpuInstanceId, computeInstanceId. v1 has no instance ids.
    private const int ProcessInfoStride = 24;
    private const int ProcessInfoStrideV1 = 16;
    private const int MaxProcesses = 128;

    private sealed class Api
    {
        public IntPtr Library;
        public delegate* unmanaged[Cdecl]<int> Init;
        public delegate* unmanaged[Cdecl]<int> Shutdown;
        public delegate* unmanaged[Cdecl]<uint*, int> GetCount;
        public delegate* unmanaged[Cdecl]<uint, IntPtr*, int> GetHandle;
        public delegate* unmanaged[Cdecl]<IntPtr, byte*, uint, int> GetName;
        public delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int> GetTemperature;
        public delegate* unmanaged[Cdecl]<IntPtr, uint*, int> GetPowerUsage;
        public delegate* unmanaged[Cdecl]<IntPtr, nvmlMemory_t*, int> GetMemoryInfo;
        public delegate* unmanaged[Cdecl]<IntPtr, nvmlUtilization_t*, int> GetUtilization;
        public delegate* unmanaged[Cdecl]<IntPtr, uint*, byte*, int> GetGraphicsProcesses;
        public delegate* unmanaged[Cdecl]<IntPtr, uint*, byte*, int> GetComputeProcesses;
        public int ProcessStride = ProcessInfoStride;
        public delegate* unmanaged[Cdecl]<IntPtr, int*, int> GetGpcOffset;
        public delegate* unmanaged[Cdecl]<IntPtr, int, int> SetGpcOffset;
        public delegate* unmanaged[Cdecl]<IntPtr, int*, int> GetMemOffset;
        public delegate* unmanaged[Cdecl]<IntPtr, int, int> SetMemOffset;
        public delegate* unmanaged[Cdecl]<IntPtr, uint, uint, int> SetLockedClocks;
        public delegate* unmanaged[Cdecl]<IntPtr, int> ResetLockedClocks;
    }

    private static readonly object _lock = new();
    private static string[]? _candidates;
    private static Api? _api;
    private static bool _loadTried;
    private static bool _init = false;
    private static int _lockedClockMhz = -1;

    /// <summary>Library names / paths tried in order. Default depends on the OS.</summary>
    public static IReadOnlyList<string> LibraryCandidates
    {
        get
        {
            lock (_lock) return _candidates ?? DefaultCandidates();
        }
    }

    /// <summary>Override the library search list (tests, unusual driver locations). Null restores the default and reloads.</summary>
    public static void SetLibraryCandidates(IEnumerable<string>? candidates)
    {
        lock (_lock)
        {
            ShutdownLocked();
            UnloadLocked();
            _candidates = candidates?.ToArray();
        }
    }

    private static string[] DefaultCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            var list = new List<string> { "nvml.dll" };
            foreach (string env in new[] { "ProgramW6432", "ProgramFiles" })
            {
                string? root = Environment.GetEnvironmentVariable(env);
                if (!string.IsNullOrEmpty(root))
                    list.Add(Path.Combine(root, "NVIDIA Corporation", "NVSMI", "nvml.dll"));
            }
            return list.ToArray();
        }

        return ["libnvidia-ml.so.1", "libnvidia-ml.so", "/usr/lib/x86_64-linux-gnu/libnvidia-ml.so.1", "/usr/lib64/libnvidia-ml.so.1"];
    }

    private static IntPtr Fn(IntPtr lib, params string[] names)
    {
        foreach (string name in names)
            if (NativeLibrary.TryGetExport(lib, name, out IntPtr address))
                return address;
        return IntPtr.Zero;
    }

    private static Api? LoadLocked()
    {
        if (_api is not null) return _api;
        if (_loadTried) return null;
        _loadTried = true;

        IntPtr lib = IntPtr.Zero;
        foreach (string candidate in _candidates ?? DefaultCandidates())
        {
            try
            {
                if (NativeLibrary.TryLoad(candidate, out lib) && lib != IntPtr.Zero) break;
            }
            catch { }
            lib = IntPtr.Zero;
        }

        if (lib == IntPtr.Zero) return null;

        var api = new Api { Library = lib };
        api.Init = (delegate* unmanaged[Cdecl]<int>)Fn(lib, "nvmlInit_v2", "nvmlInit");
        api.Shutdown = (delegate* unmanaged[Cdecl]<int>)Fn(lib, "nvmlShutdown");
        api.GetCount = (delegate* unmanaged[Cdecl]<uint*, int>)Fn(lib, "nvmlDeviceGetCount_v2", "nvmlDeviceGetCount");
        api.GetHandle = (delegate* unmanaged[Cdecl]<uint, IntPtr*, int>)Fn(lib, "nvmlDeviceGetHandleByIndex_v2", "nvmlDeviceGetHandleByIndex");
        api.GetName = (delegate* unmanaged[Cdecl]<IntPtr, byte*, uint, int>)Fn(lib, "nvmlDeviceGetName");
        api.GetTemperature = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int>)Fn(lib, "nvmlDeviceGetTemperature");
        api.GetPowerUsage = (delegate* unmanaged[Cdecl]<IntPtr, uint*, int>)Fn(lib, "nvmlDeviceGetPowerUsage");
        api.GetMemoryInfo = (delegate* unmanaged[Cdecl]<IntPtr, nvmlMemory_t*, int>)Fn(lib, "nvmlDeviceGetMemoryInfo");
        api.GetUtilization = (delegate* unmanaged[Cdecl]<IntPtr, nvmlUtilization_t*, int>)Fn(lib, "nvmlDeviceGetUtilizationRates");

        IntPtr gfx = Fn(lib, "nvmlDeviceGetGraphicsRunningProcesses_v3", "nvmlDeviceGetGraphicsRunningProcesses_v2");
        IntPtr compute = Fn(lib, "nvmlDeviceGetComputeRunningProcesses_v3", "nvmlDeviceGetComputeRunningProcesses_v2");
        if (gfx == IntPtr.Zero && compute == IntPtr.Zero)
        {
            gfx = Fn(lib, "nvmlDeviceGetGraphicsRunningProcesses");
            compute = Fn(lib, "nvmlDeviceGetComputeRunningProcesses");
            api.ProcessStride = ProcessInfoStrideV1;
        }
        api.GetGraphicsProcesses = (delegate* unmanaged[Cdecl]<IntPtr, uint*, byte*, int>)gfx;
        api.GetComputeProcesses = (delegate* unmanaged[Cdecl]<IntPtr, uint*, byte*, int>)compute;

        api.GetGpcOffset = (delegate* unmanaged[Cdecl]<IntPtr, int*, int>)Fn(lib, "nvmlDeviceGetGpcClkVfOffset");
        api.SetGpcOffset = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)Fn(lib, "nvmlDeviceSetGpcClkVfOffset");
        api.GetMemOffset = (delegate* unmanaged[Cdecl]<IntPtr, int*, int>)Fn(lib, "nvmlDeviceGetMemClkVfOffset");
        api.SetMemOffset = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)Fn(lib, "nvmlDeviceSetMemClkVfOffset");
        api.SetLockedClocks = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint, int>)Fn(lib, "nvmlDeviceSetGpuLockedClocks");
        api.ResetLockedClocks = (delegate* unmanaged[Cdecl]<IntPtr, int>)Fn(lib, "nvmlDeviceResetGpuLockedClocks");

        if (api.Init is null)
        {
            NativeLibrary.Free(lib);
            return null;
        }

        _api = api;
        return api;
    }

    private static void UnloadLocked()
    {
        if (_api is not null)
        {
            try { NativeLibrary.Free(_api.Library); } catch { }
            _api = null;
        }
        _loadTried = false;
    }

    /// <summary>
    /// Picks the internal discrete GPU out of the NVML device names: the first "... Laptop GPU" (legacy: NVAPI
    /// SystemType.Laptop, keeps an XG Mobile / eGPU out), otherwise the first device. -1 when there is none.
    /// </summary>
    public static int SelectInternalDevice(IReadOnlyList<string?> names)
    {
        for (int i = 0; i < names.Count; i++)
            if (names[i]?.Contains("Laptop", StringComparison.OrdinalIgnoreCase) == true) return i;
        return names.Count > 0 && names[0] is not null ? 0 : -1;
    }

    /// <summary>The NVML library could be located and initialised.</summary>
    public static bool IsAvailable
    {
        get
        {
            lock (_lock)
            {
                InitLocked();
                return _init;
            }
        }
    }

    public static void Init()
    {
        lock (_lock) InitLocked();
    }

    private static void InitLocked()
    {
        if (_init) return;
        try
        {
            var api = LoadLocked();
            if (api is null) return;

            int rc = api.Init();
            _init = rc == NVML_SUCCESS;
            Logger.WriteLine($"NVML Init: {rc}");
        }
        catch (Exception e)
        {
            Logger.WriteLine($"NVML Init exception: {e.Message}");
        }
    }

    /// <summary>Number of NVML devices, 0 when unavailable.</summary>
    public static int GetDeviceCount()
    {
        lock (_lock)
        {
            InitLocked();
            if (!_init || _api is null || _api.GetCount is null) return 0;
            try
            {
                uint count = 0;
                return _api.GetCount(&count) == NVML_SUCCESS ? (int)count : 0;
            }
            catch { return 0; }
        }
    }

    public static string? GetDeviceName(uint gpuIndex = 0)
    {
        lock (_lock)
        {
            InitLocked();
            if (!_init || _api is null || _api.GetName is null) return null;
            try
            {
                if (!TryHandle(gpuIndex, out IntPtr device)) return null;
                byte* buffer = stackalloc byte[96];
                if (_api.GetName(device, buffer, 96) != NVML_SUCCESS) return null;
                return Marshal.PtrToStringAnsi((IntPtr)buffer);
            }
            catch { return null; }
        }
    }

    private static bool TryHandle(uint index, out IntPtr device)
    {
        device = IntPtr.Zero;
        if (_api is null || _api.GetHandle is null) return false;
        IntPtr handle;
        if (_api.GetHandle(index, &handle) != NVML_SUCCESS) return false;
        device = handle;
        return true;
    }

    public static int? GetTemperature(uint gpuIndex = 0)
    {
        lock (_lock)
        {
            InitLocked();
            if (!_init || _api is null || _api.GetTemperature is null) return null;
            try
            {
                if (!TryHandle(gpuIndex, out IntPtr device)) return null;
                uint temp;
                if (_api.GetTemperature(device, NVML_TEMPERATURE_GPU, &temp) != NVML_SUCCESS) return null;
                return (int)temp;
            }
            catch { return null; }
        }
    }

    public static float? GetGpuPower(uint gpuIndex = 0)
    {
        lock (_lock)
        {
            InitLocked();
            if (!_init || _api is null || _api.GetPowerUsage is null) return null;
            try
            {
                if (!TryHandle(gpuIndex, out IntPtr device)) return null;
                uint mW;
                if (_api.GetPowerUsage(device, &mW) != NVML_SUCCESS) return null;
                if (mW > 200_000f) return null;
                return mW / 1000f;
            }
            catch { return null; }
        }
    }

    public static (long usedMb, long totalMb)? GetMemoryInfo(uint gpuIndex = 0)
    {
        lock (_lock)
        {
            InitLocked();
            if (!_init || _api is null || _api.GetMemoryInfo is null) return null;
            try
            {
                if (!TryHandle(gpuIndex, out IntPtr device)) return null;
                nvmlMemory_t mem;
                if (_api.GetMemoryInfo(device, &mem) != NVML_SUCCESS) return null;
                if (mem.total == 0) return null;
                const ulong MB = 1024 * 1024;
                return ((long)(mem.used / MB), (long)(mem.total / MB));
            }
            catch { return null; }
        }
    }

    /// <summary>GPU core utilisation 0..100.</summary>
    public static int? GetGpuUtilization(uint gpuIndex = 0)
    {
        lock (_lock)
        {
            InitLocked();
            if (!_init || _api is null || _api.GetUtilization is null) return null;
            try
            {
                if (!TryHandle(gpuIndex, out IntPtr device)) return null;
                nvmlUtilization_t util;
                if (_api.GetUtilization(device, &util) != NVML_SUCCESS) return null;
                return (int)Math.Min(util.gpu, 100u);
            }
            catch { return null; }
        }
    }

    /// <summary>PIDs of graphics and compute clients of the device (legacy NVAPI GetActiveApplications).</summary>
    public static int[] GetProcessIds(uint gpuIndex = 0)
    {
        lock (_lock)
        {
            InitLocked();
            if (!_init || _api is null) return [];
            var pids = new HashSet<int>();
            try
            {
                if (!TryHandle(gpuIndex, out IntPtr device)) return [];
                Collect(_api.GetGraphicsProcesses, device, _api.ProcessStride, pids);
                Collect(_api.GetComputeProcesses, device, _api.ProcessStride, pids);
            }
            catch { }
            return pids.ToArray();
        }
    }

    private static void Collect(delegate* unmanaged[Cdecl]<IntPtr, uint*, byte*, int> fn, IntPtr device, int stride, HashSet<int> pids)
    {
        if (fn is null) return;
        byte* buffer = stackalloc byte[MaxProcesses * ProcessInfoStride];
        uint count = MaxProcesses;
        if (fn(device, &count, buffer) != NVML_SUCCESS) return;
        for (int i = 0; i < Math.Min(count, MaxProcesses); i++)
            pids.Add((int)*(uint*)(buffer + i * stride));
    }

    // ---- clocks ---------------------------------------------------------------------------------

    public static bool SupportsClockOffsets
    {
        get
        {
            lock (_lock)
            {
                InitLocked();
                return _init && _api is not null && _api.GetGpcOffset is not null && _api.SetGpcOffset is not null
                       && _api.GetMemOffset is not null && _api.SetMemOffset is not null;
            }
        }
    }

    /// <summary>Graphics / memory clock offsets (MHz) of the performance state curve.</summary>
    public static bool GetClockOffsets(out int coreMhz, out int memoryMhz, uint gpuIndex = 0)
    {
        coreMhz = memoryMhz = 0;
        lock (_lock)
        {
            InitLocked();
            if (!_init || _api is null || _api.GetGpcOffset is null || _api.GetMemOffset is null) return false;
            try
            {
                if (!TryHandle(gpuIndex, out IntPtr device)) return false;
                int core, mem;
                if (_api.GetGpcOffset(device, &core) != NVML_SUCCESS) return false;
                if (_api.GetMemOffset(device, &mem) != NVML_SUCCESS) return false;
                coreMhz = core;
                memoryMhz = mem;
                return true;
            }
            catch { return false; }
        }
    }

    /// <returns>NVML return code, -1 when the entry points are missing.</returns>
    public static int SetClockOffsets(int coreMhz, int memoryMhz, uint gpuIndex = 0)
    {
        lock (_lock)
        {
            InitLocked();
            if (!_init || _api is null || _api.SetGpcOffset is null || _api.SetMemOffset is null) return -1;
            try
            {
                if (!TryHandle(gpuIndex, out IntPtr device)) return -1;
                int rc = _api.SetGpcOffset(device, coreMhz);
                if (rc != NVML_SUCCESS) return rc;
                return _api.SetMemOffset(device, memoryMhz);
            }
            catch { return -1; }
        }
    }

    /// <summary>Lock (maxMhz &gt; 0) or reset (maxMhz &lt;= 0) the graphics clock. Needs admin/root.</summary>
    /// <returns>NVML return code (0 = ok), -1 when unsupported.</returns>
    public static int SetLockedClock(int maxMhz, uint gpuIndex = 0)
    {
        lock (_lock)
        {
            InitLocked();
            if (!_init || _api is null) return -1;
            try
            {
                if (!TryHandle(gpuIndex, out IntPtr device)) return -1;
                int rc;
                if (maxMhz > 0)
                {
                    if (_api.SetLockedClocks is null) return -1;
                    rc = _api.SetLockedClocks(device, 0, (uint)maxMhz);
                    if (rc == NVML_SUCCESS) _lockedClockMhz = maxMhz;
                }
                else
                {
                    if (_api.ResetLockedClocks is null) return -1;
                    rc = _api.ResetLockedClocks(device);
                    if (rc == NVML_SUCCESS) _lockedClockMhz = -1;
                }
                return rc;
            }
            catch { return -1; }
        }
    }

    /// <summary>Last locked clock applied through this process, -1 when none / unknown (NVML has no getter).</summary>
    public static int LockedClockMhz
    {
        get { lock (_lock) return _lockedClockMhz; }
        set { lock (_lock) _lockedClockMhz = value; }
    }

    public static void Shutdown()
    {
        lock (_lock) ShutdownLocked();
    }

    private static void ShutdownLocked()
    {
        if (!_init) return;
        try
        {
            int rc = _api!.Shutdown is null ? 0 : _api.Shutdown();
            Logger.WriteLine($"NVML Shutdown: {rc}");
        }
        catch { }
        _init = false;
    }
}
