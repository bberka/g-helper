using System.Runtime.InteropServices;
using PawnIO;

namespace GHelper.Platform.Windows.Pawn;

/// <summary>
/// <see cref="ICpuTuning"/> for Windows: Core's <see cref="PawnCpuTuning"/> (Ryzen SMU mailbox, Intel RAPL over MSR) running
/// on the PawnIO kernel driver (<see cref="PawnIoDevice"/>). The signed driver modules (RyzenSMU.bin / IntelMSR.bin) are
/// embedded in GHelper.Core. PawnIO is x64 only: everywhere else <see cref="ICpuTuning.IsSupported"/> is false and no
/// driver is ever opened.
/// </summary>
public sealed class WindowsCpuTuning : PawnCpuTuning
{
    public WindowsCpuTuning()
        : base(() => new PawnIoDevice(), OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
    {
    }
}
