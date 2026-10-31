using System.Runtime.InteropServices;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class CriticalProcessActionsTests
{
    [Fact]
    public void KillHelperMailboxLayoutMatchesTheNativeProtocol()
    {
        Assert.Equal(KillHelperProtocol.MailboxSize, Marshal.SizeOf<KillHelperMailbox>());
        Assert.Equal(expected: 64,
            Marshal.OffsetOf<KillHelperMailbox>(nameof(KillHelperMailbox.ArmPayloadSequence)).ToInt32());
        Assert.Equal(expected: 128,
            Marshal.OffsetOf<KillHelperMailbox>(nameof(KillHelperMailbox.FirePayloadSequence)).ToInt32());
        Assert.Equal(expected: 192,
            Marshal.OffsetOf<KillHelperMailbox>(nameof(KillHelperMailbox.FireResponseSequence)).ToInt32());
    }
}
