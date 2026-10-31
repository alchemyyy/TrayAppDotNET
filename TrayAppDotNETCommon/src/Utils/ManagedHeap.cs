using System.Runtime;

namespace TrayAppDotNETCommon.Utils;

public static class ManagedHeap
{
    /// <summary>
    /// Collects every generation, runs the pending finalizers and collects again, compacting the large object heap
    /// both times. The aggressive mode also decommits the freed space. Blocks the calling thread until done.
    /// </summary>
    public static void CompactAndDecommit()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }
}
