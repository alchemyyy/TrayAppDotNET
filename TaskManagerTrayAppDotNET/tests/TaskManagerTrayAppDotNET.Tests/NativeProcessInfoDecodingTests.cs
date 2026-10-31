using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class NativeProcessInfoDecodingTests
{
    [Fact]
    public void SixtyFourBitProcessesAlwaysReportDEPEnabled()
    {
        foreach (ProcessDisplayCode architecture in new[] { ProcessDisplayCode.X64, ProcessDisplayCode.ARM64 })
        {
            Assert.Equal(
                ProcessDisplayCode.Enabled,
                NativeProcessInfo.DecodeDataExecutionPrevention(architecture, policyRead: false, flags: 0));
        }
    }

    [Fact]
    public void ThirtyTwoBitProcessesUseTheDEPPolicyFlags()
    {
        Assert.Equal(
            ProcessDisplayCode.Enabled,
            NativeProcessInfo.DecodeDataExecutionPrevention(ProcessDisplayCode.X86, policyRead: true, flags: 0x1));
        Assert.Equal(
            ProcessDisplayCode.Disabled,
            NativeProcessInfo.DecodeDataExecutionPrevention(ProcessDisplayCode.X86, policyRead: true, flags: 0));
        Assert.Equal(
            ProcessDisplayCode.Unavailable,
            NativeProcessInfo.DecodeDataExecutionPrevention(ProcessDisplayCode.X86, policyRead: false, flags: 0));
    }

    [Fact]
    public void ExtendedCFGUsesTheEnableXfgBitNotTheAuditBit()
    {
        Assert.Equal(expected: 0x8u, NativeProcessInfo.XFGEnabled);
        Assert.Equal(ProcessDisplayCode.Enabled, NativeProcessInfo.DecodeExtendedControlFlowGuard(0x1 | 0x8));
        Assert.Equal(ProcessDisplayCode.Disabled, NativeProcessInfo.DecodeExtendedControlFlowGuard(0x1 | 0x10));
        Assert.Equal(ProcessDisplayCode.Disabled, NativeProcessInfo.DecodeExtendedControlFlowGuard(0x8));
    }
}
