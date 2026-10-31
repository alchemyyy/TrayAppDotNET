using System.Collections;
using System.Globalization;
using System.Resources;
using TrayAppDotNETCommon.Localization;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

/// <summary>
/// Multi-line resource strings must break lines with "\n" only. A literal line break inside a resx value follows the
/// file's line endings, which Git rewrites to CRLF on a Windows checkout (core.autocrlf), and the resource compiler
/// keeps it, so the text would change with the checkout. Values write the break as the character reference
/// <c>&amp;#10;</c> instead, which no line-ending conversion touches.
/// </summary>
public sealed class ResourceLineEndingTests
{
    public static TheoryData<string> Resources =>
        ["Common", "Battery", "Brightness", "FanControl", "Network", "TaskManager", "Volume"];

    [Theory]
    [MemberData(nameof(Resources))]
    public void NoResourceStringContainsACarriageReturn(string resources)
    {
        ResourceManager manager = resources == "Common"
            ? CommonStrings.ResourceManager
            : new ResourceManager($"AppResources.{resources}", typeof(ResourceLineEndingTests).Assembly);
        ResourceSet set = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)
                          ?? throw new InvalidOperationException($"{manager.BaseName} has no neutral resource set.");

        string[] offending =
        [
            .. set.Cast<DictionaryEntry>()
                .Where(static entry => entry.Value is string text && text.Contains('\r', StringComparison.Ordinal))
                .Select(static entry => (string)entry.Key)
                .Order(StringComparer.Ordinal)
        ];
        Assert.True(
            offending.Length == 0,
            $"{resources} resource strings contain CR: {string.Join(", ", offending)}. Write each line break inside "
            + "the resx value as &#10; instead of a literal line break.");
    }
}
