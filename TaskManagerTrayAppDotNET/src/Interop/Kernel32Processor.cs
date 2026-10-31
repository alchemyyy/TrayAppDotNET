using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Interop;

/// <summary>Machine-wide processor topology queries.</summary>
internal static class Kernel32Processor
{
    private const ushort AllProcessorGroups = 0xFFFF;

    private static readonly Lazy<int> MachineProcessorCountValue = new(ReadMachineProcessorCount);

    /// <summary>
    /// Active logical processors across every processor group. Unlike <see cref="Environment.ProcessorCount"/> it is
    /// not limited by this process's affinity, job object or processor group, so it is the denominator for
    /// whole-machine CPU percentages.
    /// </summary>
    public static int MachineProcessorCount => MachineProcessorCountValue.Value;

    /// <summary>Active processor groups; more than one means 64-bit affinity masks cannot describe the machine.</summary>
    public static int ProcessorGroupCount => Math.Max(1, (int)GetActiveProcessorGroupCount());

    private static int ReadMachineProcessorCount()
    {
        uint count = GetActiveProcessorCount(AllProcessorGroups);
        return count is > 0 and <= int.MaxValue ? (int)count : Math.Max(1, Environment.ProcessorCount);
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetActiveProcessorCount(ushort groupNumber);

    [DllImport("kernel32.dll")]
    private static extern ushort GetActiveProcessorGroupCount();
}
