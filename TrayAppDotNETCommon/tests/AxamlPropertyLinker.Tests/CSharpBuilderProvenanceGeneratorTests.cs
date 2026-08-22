#if DEBUG
using Microsoft.CodeAnalysis;
using Xunit;

namespace TrayAppDotNETCommon.AxamlPropertyLinker.Tests;

public sealed class CSharpBuilderProvenanceGeneratorTests
{
    [Fact]
    public void EmitsLastAssignmentForExplicitBuilderBoundary()
    {
        AxamlGeneratorResult result = AxamlGeneratorHost.RunGenerator(
            SampleSource,
            [],
            isDebug: true);

        Assert.Empty(result.CompilationDiagnostics);
        GeneratedSourceResult generatedResult = result.RunResult.Results
            .Single()
            .GeneratedSources
            .Single(static source => source.HintName ==
                "AxamlPropertyLinker.CSharpBuilderProvenance.g.cs");
        string generatedSource = generatedResult.SourceText.ToString();

        Assert.Contains("global::Avalonia.Controls.TextBlock.TextProperty", generatedSource);
        Assert.Contains("global::Avalonia.Controls.TextBlock.OpacityProperty", generatedSource);
        Assert.DoesNotContain("global::Avalonia.Controls.TextBlock.WidthProperty", generatedSource);
        Assert.Contains("\"value + \\\"!\\\"\"", generatedSource);
        Assert.DoesNotContain("\"value\",", generatedSource);
        Assert.Contains("DebugUIProvenance.RegisterCSharpBuilders(Entries)", generatedSource);
    }

    private const string SampleSource =
        """
        using System.Collections.Generic;
        using System.Diagnostics;
        using System.Runtime.CompilerServices;

        namespace Avalonia
        {
            public class AvaloniaProperty
            {
            }

            public class AvaloniaObject
            {
            }
        }

        namespace Avalonia.Controls
        {
            public sealed class TextBlock : Avalonia.AvaloniaObject
            {
                public static readonly Avalonia.AvaloniaProperty TextProperty = new();
                public static readonly Avalonia.AvaloniaProperty OpacityProperty = new();
                public static readonly Avalonia.AvaloniaProperty WidthProperty = new();

                public string? Text { get; set; }
                public double Opacity { get; set; }
                public double Width { get; set; }
            }
        }

        namespace TrayAppDotNETCommon.UI.Debugging
        {
            public enum DebugPropertyAssignmentOperation
            {
                CLRSetter
            }

            public readonly record struct CSharpBuilderProvenanceEntry(
                string BoundarySourcePath,
                int BoundarySourceLine,
                Avalonia.AvaloniaProperty Property,
                DebugPropertyAssignmentOperation Operation,
                string ValueExpression,
                int AssignmentSourceLine,
                int AssignmentSourceColumn,
                string AssignmentSourceMember,
                string? ResourceKey);

            public static class DebugUIProvenance
            {
                [Conditional("DEBUG")]
                public static void RecordBuilder(
                    Avalonia.AvaloniaObject target,
                    [CallerFilePath] string sourceFilePath = "",
                    [CallerLineNumber] int sourceLine = 0)
                {
                }

                public static void RegisterCSharpBuilders(
                    IReadOnlyList<CSharpBuilderProvenanceEntry> entries)
                {
                }
            }
        }

        namespace Samples
        {
            public static class Builder
            {
                public static Avalonia.Controls.TextBlock Build(string value)
                {
                    Avalonia.Controls.TextBlock label = new()
                    {
                        Text = value,
                        Opacity = 0.4
                    };
                    label.Text = value + "!";
                    if (value.Length > 0)
                        label.Width = 10;
                    TrayAppDotNETCommon.UI.Debugging.DebugUIProvenance.RecordBuilder(label);
                    return label;
                }
            }
        }
        """;
}
#endif
