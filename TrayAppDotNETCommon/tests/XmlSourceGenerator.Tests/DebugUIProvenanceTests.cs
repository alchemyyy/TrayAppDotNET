#if DEBUG
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Debugging;
using TrayAppDotNETCommon.Visuals;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class DebugUIProvenanceTests
{
    [Fact]
    public void CommonBuilderBoundaryRecordsGeneratedPropertySources() => AvaloniaTestHost.Run(() =>
    {
        FlyoutControlPalette palette = new(
            Colors.White,
            Colors.Gray,
            Colors.Gray,
            Colors.Blue,
            Colors.Navy,
            Colors.Black,
            Colors.Black,
            Colors.White,
            Colors.Gray,
            Colors.Blue,
            Colors.White);
        TextBlock textBlock = TrayAppDotNETFlyoutUI.IconText("x", palette, 12);

        DebugPropertyAssignmentHistory textHistory =
            DebugUIProvenance.GetPropertyHistory(textBlock, TextBlock.TextProperty);
        DebugPropertyAssignmentHistory fontSizeHistory =
            DebugUIProvenance.GetPropertyHistory(textBlock, TextBlock.FontSizeProperty);

        DebugPropertyAssignment text = Assert.Single(textHistory.Assignments);
        DebugPropertyAssignment fontSize = Assert.Single(fontSizeHistory.Assignments);
        Assert.EndsWith("TrayAppDotNETCommon/src/UI/Controls/FlyoutCards.cs", text.SourcePath);
        Assert.Equal("IconText", text.SourceMember);
        Assert.True(text.SourceLine > 0);
        Assert.True(text.SourceColumn > 0);
        Assert.Equal("glyph", text.ValueExpression);
        Assert.Equal("fontSize", fontSize.ValueExpression);
    });

    [Fact]
    public void PropertyAssignmentsRetainSourceAndSequence() => AvaloniaTestHost.Run(() =>
    {
        Border border = new();

        DebugUIProvenance.RecordProperty(
            border,
            Border.OpacityProperty,
            0.75,
            valueExpression: "palette.CardOpacity",
            sourceFilePath: @"C:\repo\TrayAppDotNET\FanControlTrayAppDotNET\src\UI\CardBuilder.cs",
            sourceLine: 42,
            sourceMember: "Build");

        DebugPropertyAssignmentHistory history =
            DebugUIProvenance.GetPropertyHistory(border, Border.OpacityProperty);

        DebugPropertyAssignment assignment = Assert.Single(history.Assignments);
        Assert.Equal("palette.CardOpacity", assignment.ValueExpression);
        Assert.Equal("FanControlTrayAppDotNET/src/UI/CardBuilder.cs", assignment.SourcePath);
        Assert.Equal(42, assignment.SourceLine);
        Assert.Equal("Build", assignment.SourceMember);
        Assert.Equal("0.75", assignment.ValueDisplay);
        Assert.Equal(1, history.TotalAssignmentCount);
    });

    [Fact]
    public void PropertyAssignmentHistoryIsBounded() => AvaloniaTestHost.Run(() =>
    {
        Border border = new();

        for (int assignmentIndex = 0; assignmentIndex < 300; assignmentIndex++)
        {
            DebugUIProvenance.RecordProperty(
                border,
                Border.OpacityProperty,
                assignmentIndex,
                sourceLine: assignmentIndex);
        }

        DebugPropertyAssignmentHistory history =
            DebugUIProvenance.GetPropertyHistory(border, Border.OpacityProperty);
        DebugPropertyAssignmentHistory recentHistory =
            DebugUIProvenance.GetRecentPropertyHistory(border, Border.OpacityProperty, 4);

        Assert.Equal(300, history.TotalAssignmentCount);
        Assert.Equal(256, history.Assignments.Count);
        Assert.Equal(44, history.DiscardedAssignmentCount);
        Assert.Equal("44", history.Assignments[0].ValueDisplay);
        Assert.Equal("299", history.Assignments[^1].ValueDisplay);
        Assert.Equal(300, recentHistory.TotalAssignmentCount);
        Assert.Equal(4, recentHistory.Assignments.Count);
        Assert.Equal(296, recentHistory.DiscardedAssignmentCount);
        Assert.Equal("296", recentHistory.Assignments[0].ValueDisplay);
    });

    [Fact]
    public void PropertyProvenanceDoesNotRetainTargetsOrAssignedValues() => AvaloniaTestHost.Run(() =>
    {
        Border retainedTarget = new();
        WeakReference<Border> transientTarget = RecordTransientTarget();
        WeakReference<TextBlock> transientValue = RecordTransientValue(retainedTarget);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(transientTarget.TryGetTarget(out _));
        Assert.False(transientValue.TryGetTarget(out _));
        GC.KeepAlive(retainedTarget);
    });

    [Fact]
    public void AXAMLCatalogFindsNamedPropertyAndResourceDefinition()
    {
        AXAMLProvenanceEntry[] entries =
        [
            new(
                AXAMLProvenanceKind.ResourceDefinition,
                "TrayAppDotNETCommon/src/Visuals/AppTheme.axaml",
                27,
                5,
                "TrayAppDotNETCommon.Visuals.AppThemeResources",
                "ThemeColor",
                "/ResourceDictionary/ThemeColor[1]",
                null,
                null,
                "AppTheme.CardBackground",
                "#FBFBFB/#2B2B2B",
                null),
            new(
                AXAMLProvenanceKind.ResourceReference,
                "FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.axaml",
                94,
                9,
                "FanControlTrayAppDotNET.UI.Flyout.FanFlyoutWindow",
                "Border",
                "/Window/Border[1]",
                "DeviceCard",
                "Background",
                "AppTheme.CardBackground",
                "{StaticResource AppTheme.CardBackground}",
                null),
            new(
                AXAMLProvenanceKind.PropertyAssignment,
                "FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.axaml",
                95,
                9,
                "FanControlTrayAppDotNET.UI.Flyout.FanFlyoutWindow",
                "Border",
                "/Window/Border[1]",
                "DeviceCard",
                "Grid.Row",
                null,
                "1",
                null),
            new(
                AXAMLProvenanceKind.StyleSetter,
                "TrayAppDotNETCommon/src/Visuals/AppTheme.axaml",
                130,
                13,
                "TrayAppDotNETCommon.Visuals.AppThemeResources",
                "Setter",
                "/ResourceDictionary/Style[1]/Setter[1]",
                null,
                "Opacity",
                null,
                "0.8",
                "Border"),
            new(
                AXAMLProvenanceKind.Template,
                "TrayAppDotNETCommon/src/Visuals/AppTheme.axaml",
                145,
                17,
                "TrayAppDotNETCommon.Visuals.AppThemeResources",
                "ControlTemplate",
                "/ResourceDictionary/ControlTheme[1]/Setter[1]/ControlTemplate[1]",
                null,
                "Template",
                "ButtonTheme",
                "<ControlTemplate>",
                "ControlTheme:ButtonTheme")
        ];

        DebugUIProvenance.RegisterAXAML(typeof(DebugUIProvenanceTests).Assembly, entries);

        IReadOnlyList<AXAMLProvenanceEntry> properties = DebugUIProvenance.FindAXAMLPropertyEntries(
            ["FanControlTrayAppDotNET.UI.Flyout.FanFlyoutWindow"],
            "Border",
            "DeviceCard",
            "Background");
        IReadOnlyList<AXAMLProvenanceEntry> attachedProperties =
            DebugUIProvenance.FindAXAMLPropertyEntries(
                ["FanControlTrayAppDotNET.UI.Flyout.FanFlyoutWindow"],
                "Border",
                "DeviceCard",
                "Row");
        IReadOnlyList<AXAMLProvenanceEntry> reusableStyles =
            DebugUIProvenance.FindAXAMLPropertyEntries(
                ["FanControlTrayAppDotNET.UI.Flyout.FanFlyoutWindow"],
                "Border",
                "DeviceCard",
                "Opacity");
        IReadOnlyList<AXAMLProvenanceEntry> reusableTemplates =
            DebugUIProvenance.FindAXAMLPropertyEntries(
                ["FanControlTrayAppDotNET.UI.Flyout.FanFlyoutWindow"],
                "Button",
                null,
                "Template");
        IReadOnlyList<AXAMLProvenanceEntry> resources =
            DebugUIProvenance.FindAXAMLResourceDefinitions("AppTheme.CardBackground");

        Assert.Single(properties);
        Assert.Single(attachedProperties);
        Assert.Single(reusableStyles);
        Assert.Single(reusableTemplates);
        Assert.Contains(resources, entry => entry.SourcePath.EndsWith("AppTheme.axaml", StringComparison.Ordinal));
    }

    [Fact]
    public void InspectorDisplaysInstrumentedAndAXAMLPropertySources() => AvaloniaTestHost.Run(() =>
    {
        const string ResourceKey = "AppTheme.CardBackground";
        AXAMLProvenanceEntry[] entries =
        [
            new(
                AXAMLProvenanceKind.ResourceDefinition,
                "TrayAppDotNETCommon/src/Visuals/AppTheme.axaml",
                27,
                5,
                "TrayAppDotNETCommon.Visuals.AppThemeResources",
                "ThemeColor",
                "/ResourceDictionary/ThemeColor[1]",
                null,
                null,
                ResourceKey,
                "LightHex=\"#FBFBFB\" DarkHex=\"#2B2B2B\"",
                null),
            new(
                AXAMLProvenanceKind.ResourceReference,
                "FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.axaml",
                94,
                9,
                "Avalonia.Controls.Window",
                "Border",
                "/Window/Border[1]",
                "DeviceCard",
                "Background",
                ResourceKey,
                "{StaticResource AppTheme.CardBackground}",
                null)
        ];

        DebugUIProvenance.RegisterAXAML(typeof(DebugUIProvenanceTests).Assembly, entries);

        SolidColorBrush background = new(Color.Parse("#2B2B2B"));
        Border border = new()
        {
            Name = "DeviceCard",
            Background = background
        };
        Window window = new() { Content = border };
        DebugUIProvenance.RecordProperty(
            border,
            Border.BackgroundProperty,
            background,
            DebugPropertyAssignmentOperation.Builder,
            "palette.CardBackground",
            @"C:\repo\TrayAppDotNET\TrayAppDotNETCommon\src\UI\Cards.cs",
            73,
            "CreateCard",
            ResourceKey);
        DebugUIProvenance.RecordProperty(
            background,
            SolidColorBrush.ColorProperty,
            background.Color,
            DebugPropertyAssignmentOperation.SetCurrentValue,
            "sourcePalette.CardBackground",
            @"C:\repo\TrayAppDotNET\TrayAppDotNETCommon\src\UI\Controls\SettingsUI.cs",
            271,
            "UpdateFrom");

        window.Show();
        ControlHoverInspectorSnapshot snapshot = ControlHoverInspectorSnapshotBuilder.Build(window, border);
        List<string> rows = Flatten(snapshot.Roots);

        Assert.Contains(rows, row => row.Contains("Instrumented C# assignments (1)", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("palette.CardBackground", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("TrayAppDotNETCommon/src/UI/Cards.cs:73", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Assigned SolidColorBrush provenance", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("sourcePalette.CardBackground", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("SettingsUI.cs:271", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("AXAML source candidates (1)", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("FanFlyoutWindow.axaml:94:9", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Resource definition candidates (", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("AppTheme.axaml:27:5", StringComparison.Ordinal));

        window.Close();
    });

    [Fact]
    public void GeneratedCommonAXAMLCatalogLinksInstrumentedResourceKeys() => AvaloniaTestHost.Run(() =>
    {
        Border border = new() { Opacity = 0.8 };
        Window window = new() { Content = border };
        DebugUIProvenance.RecordProperty(
            border,
            Border.OpacityProperty,
            border.Opacity,
            DebugPropertyAssignmentOperation.Builder,
            "SettingsUILayout.DescriptionOpacity",
            resourceKey: "SettingsUI.DescriptionOpacity");

        window.Show();
        ControlHoverInspectorSnapshot snapshot = ControlHoverInspectorSnapshotBuilder.Build(window, border);
        List<string> rows = Flatten(snapshot.Roots);

        Assert.Contains(rows, row => row.Contains("SettingsUI.DescriptionOpacity", StringComparison.Ordinal));
        Assert.Contains(
            rows,
            row => row.Contains(
                "TrayAppDotNETCommon/src/UI/Controls/SettingsUI.axaml",
                StringComparison.Ordinal));

        window.Close();
    });

    [Fact]
    public void InspectorLinksContentGlyphAndLayoutValuesToAXAML() => AvaloniaTestHost.Run(() =>
    {
        AXAMLProvenanceEntry[] entries =
        [
            ResourceDefinition("InspectorGlyph.Unlock", "InspectorGlyphs.axaml", 7, "Text=G"),
            ResourceDefinition("InspectorLayout.ModeButtonWidth", "InspectorFlyout.axaml", 11, "33"),
            ResourceDefinition("InspectorLayout.ModeButtonHeight", "InspectorFlyout.axaml", 12, "29"),
            ResourceDefinition("InspectorLayout.ModeButtonFontSize", "InspectorFlyout.axaml", 13, "17")
        ];
        DebugUIProvenance.RegisterAXAML(typeof(DebugUIProvenanceTests).Assembly, entries);

        Glyph glyph = new("G", TADNFont.SegoeFluentIcons);
        DebugUIProvenance.RegisterGlyphResource(glyph, "InspectorGlyph.Unlock");
        TextBlock glyphText = new() { FontSize = 17 };
        GlyphApplicator.ApplyTo(glyphText, glyph);
        Border button = new()
        {
            Width = 33,
            Height = 29,
            Child = glyphText
        };
        TrayAppDotNETToolTip.SetTip(button, "Useful tip");

        AvaloniaProperty tooltipObjectProperty = AvaloniaPropertyRegistry.Instance
            .GetRegisteredAttached(typeof(Border))
            .Single(property => property.OwnerType == typeof(ToolTip) && property.Name == "ToolTip");
        button.SetValue(tooltipObjectProperty, new ToolTip());

        InspectorResourceWindow window = new() { Content = button };
        window.Show();
        ControlHoverInspectorSnapshot snapshot = ControlHoverInspectorSnapshotBuilder.Build(window, button);
        List<string> rows = Flatten(snapshot.Roots);

        Assert.Contains(rows, row => row.Contains("[content 1] TextBlock", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("AXAML InspectorGlyph.Unlock @ InspectorGlyphs.axaml:7", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("AXAML InspectorLayout.ModeButtonFontSize @ InspectorFlyout.axaml:13", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Tip = \"Useful tip\"", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, row => row.StartsWith("ToolTip = ", StringComparison.Ordinal));

        window.PreserveLayoutForReflection();
        window.Close();
    });

    private static AXAMLProvenanceEntry ResourceDefinition(
        string resourceKey,
        string sourcePath,
        int line,
        string valueExpression) =>
        new(
            AXAMLProvenanceKind.ResourceDefinition,
            sourcePath,
            line,
            5,
            typeof(InspectorResourceWindow).FullName ?? nameof(InspectorResourceWindow),
            "sys:Double",
            "/Window/Resources/sys:Double[1]",
            null,
            null,
            resourceKey,
            valueExpression,
            null);

    private static List<string> Flatten(IReadOnlyList<ControlHoverInspectorNode> roots)
    {
        List<string> rows = [];
        Stack<ControlHoverInspectorNode> pending = [];
        for (int index = roots.Count - 1; index >= 0; index--)
            pending.Push(roots[index]);

        while (pending.Count > 0)
        {
            ControlHoverInspectorNode node = pending.Pop();
            rows.Add(node.Text);
            for (int index = node.Children.Count - 1; index >= 0; index--)
                pending.Push(node.Children[index]);
        }

        return rows;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Border> RecordTransientTarget()
    {
        Border border = new();
        DebugUIProvenance.RecordProperty(border, Border.OpacityProperty, 0.5);
        return new WeakReference<Border>(border);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<TextBlock> RecordTransientValue(Border target)
    {
        TextBlock value = new() { Text = "transient" };
        target.Child = value;
        DebugUIProvenance.RecordProperty(target, Border.ChildProperty, value);
        target.Child = null;
        return new WeakReference<TextBlock>(value);
    }

    private sealed class InspectorResourceWindow : Window
    {
        private readonly InspectorLayoutAxamlProperties _layout = new();

        public void PreserveLayoutForReflection() => GC.KeepAlive(_layout);
    }

    private readonly struct InspectorLayoutAxamlProperties
    {
        private readonly double _scale;

        public InspectorLayoutAxamlProperties() => _scale = 1;

        public double ModeButtonWidth => 33 * _scale;

        public double ModeButtonHeight => 29 * _scale;

        public double ModeButtonFontSize => 17 * _scale;
    }
}
#endif
