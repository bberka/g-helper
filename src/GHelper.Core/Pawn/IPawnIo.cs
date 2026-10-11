namespace PawnIO
{
    public enum PawnConnectResult { OK, NotInstalled, AccessDenied, OtherError }

    /// <summary>
    /// Driver IO used by <see cref="RyzenSmuService"/> and <see cref="IntelMsr"/> (legacy PawnIOWrapper).
    /// Windows: PawnIO driver (\\?\GLOBALROOT\Device\PawnIO, IOCTL load/execute). Linux: an adapter that maps the
    /// module's "ioctl_*" function names onto the ryzen_smu / msr kernel interfaces.
    /// A fresh instance owns one loaded module. Implementations must never throw from these members.
    /// </summary>
    public interface IPawnIo : IDisposable
    {
        bool IsConnected { get; }
        bool IsModuleLoaded { get; }

        PawnConnectResult Connect();

        /// <summary>Load the compiled module (RyzenSMU.bin / IntelMSR.bin, embedded in GHelper.Core).</summary>
        bool LoadModule(byte[] data);

        /// <summary>Call an exported module function with 64-bit input words, fill <paramref name="output"/>.</summary>
        bool Execute(string functionName, ulong[]? input, ulong[]? output);
    }

    /// <summary>Access to the embedded PawnIO modules and the platform driver factory.</summary>
    public static class PawnModules
    {
        public const string RyzenSmuResource = "GHelper.RyzenSMU.bin";
        public const string IntelMsrResource = "GHelper.IntelMSR.bin";

        /// <summary>Driver instance from the registered platform, null when the platform has no PawnIO-like interface.</summary>
        public static IPawnIo? CreateDriver()
        {
            try
            {
                return GHelper.Platform.PlatformServices.IsRegistered
                    ? GHelper.Platform.PlatformServices.Current.CpuTuning.CreatePawnIo()
                    : null;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("PawnIO driver: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Reads a module from the embedded resources. Looks first in <paramref name="assembly"/> (legacy
        /// "&lt;assembly&gt;.RyzenSMU.bin" naming, then the logical name), finally in GHelper.Core itself.
        /// </summary>
        public static byte[] Load(string resourceName, System.Reflection.Assembly? assembly = null)
        {
            string shortName = resourceName.Substring(resourceName.IndexOf('.') + 1);   // RyzenSMU.bin
            var candidates = new List<(System.Reflection.Assembly, string)>();
            if (assembly is not null)
            {
                candidates.Add((assembly, assembly.GetName().Name + "." + shortName));
                candidates.Add((assembly, resourceName));
            }
            candidates.Add((typeof(PawnModules).Assembly, resourceName));

            foreach (var (asm, name) in candidates)
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is null) continue;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }

            throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        }
    }
}
