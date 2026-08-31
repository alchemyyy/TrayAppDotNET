using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class EnterpriseContextReaderTests
{
    [Theory]
    [InlineData(0U, "Personal")]
    [InlineData(1U, "Exempt")]
    [InlineData(32U, "Personal")]
    public void FormatsTerminalContexts(uint flags, string expected) =>
        Assert.Equal(expected, EnterpriseContextReader.FormatContext(flags, []));

    [Fact]
    public void FormatsEnterpriseIDsAndApplicationCapabilities()
    {
        string value = EnterpriseContextReader.FormatContext(
            2U | 8U,
            ["corp.contoso.com", "research.contoso.com"]);

        Assert.Equal(
            expected: "corp.contoso.com, research.contoso.com (Enlightened, Permissive)",
            value);
    }
}
