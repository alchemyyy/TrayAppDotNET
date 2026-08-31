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
        TextBlock textBlock = TrayAppDotNETFlyoutUI.IconText(glyph: "x", palette, fontSize: 12);

        DebugPropertyAssignmentHistory textHistory =
            DebugUIProvenance.GetPropertyHistory(textBlock, TextBlock.TextProperty);
        DebugPropertyAssignmentHistory fontSizeHistory =
            DebugUIProvenance.GetPropertyHistory(textBlock, TextBlock.FontSizeProperty);

        DebugPropertyAssignment text = Assert.Single(textHistory.Assignments);
        DebugPropertyAssignment fontSize = Assert.Single(fontSizeHistory.Assignments);
        Assert.EndsWith(expectedEndString: "TrayAppDotNETCommon/src/UI/Controls/FlyoutCards.cs", text.SourcePath);
        Assert.Equal(expected: "IconText", text.SourceMember);
        Assert.True(text.SourceLine > 0);
        Assert.True(text.SourceColumn > 0);
        Assert.Equal(expected: "glyph", text.ValueExpression);
        Assert.Equal(expected: "fontSize", fontSize.ValueExpression);
    });

    [Fact]
    public void PropertyAssignmentsRetainSourceAndSequence() => AvaloniaTestHost.Run(() =>
    {
        Border border = new();

        DebugUIProvenance.RecordProperty(
            border,
            Border.OpacityProperty,
            value: 0.75,
            valueExpression: "palette.CardOpacity",
            sourceFilePath: @"C:\repo\TrayAppDotNET\FanControlTrayAppDotNET\src\UI\CardBuilder.cs",
            sourceLine: 42,
            sourceMember: "Build");

        DebugPropertyAssignmentHistory history =
            DebugUIProvenance.GetPropertyHistory(border, Border.OpacityProperty);

        DebugPropertyAssignment assignment = Assert.Single(history.Assignments);
        Assert.Equal(expected: "palette.CardOpacity", assignment.ValueExpression);
        Assert.Equal(expected: "FanControlTrayAppDotNET/src/UI/CardBuilder.cs", assignment.SourcePath);
        Assert.Equal(expected: 42, assignment.SourceLine);
        Assert.Equal(expected: "Build", assignment.SourceMember);
        Assert.Equal(expected: "0.75", assignment.ValueDisplay);
        Assert.Equal(expected: 1, history.TotalAssignmentCount);
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
            DebugUIProvenance.GetRecentPropertyHistory(border, Border.OpacityProperty, maximumAssignments: 4);

        Assert.Equal(expected: 300, history.TotalAssignmentCount);
        Assert.Equal(expected: 256, history.Assignments.Count);
        Assert.Equal(expected: 44, history.DiscardedAssignmentCount);
        Assert.Equal(expected: "44", history.Assignments[0].ValueDisplay);
        Assert.Equal(expected: "299", history.Assignments[^1].ValueDisplay);
        Assert.Equal(expected: 300, recentHistory.TotalAssignmentCount);
        Assert.Equal(expected: 4, recentHistory.Assignments.Count);
        Assert.Equal(expected: 296, recentHistory.DiscardedAssignmentCount);
        Assert.Equal(expected: "296", recentHistory.Assignments[0].ValueDisplay);
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
                SourcePath: "TrayAppDotNETCommon/src/Visuals/AppTheme.axaml",
                Line: 27,
                Column: 5,
                OwnerTypeName: "TrayAppDotNETCommon.Visuals.AppThemeResources",
                ElementTypeName: "ThemeColor",
                ElementPath: "/ResourceDictionary/ThemeColor[1]",
                ControlName: null,
                PropertyName: null,
                ResourceKey: "AppTheme.CardBackground",
                ValueExpression: "#FBFBFB/#2B2B2B",
                Selector: null),
            new(
                AXAMLProvenanceKind.ResourceReference,
                SourcePath: "FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.axaml",
                Line: 94,
                Column: 9,
                OwnerTypeName: "FanControlTrayAppDotNET.UI.Flyout.FanFlyoutWindow",
                ElementTypeName: "Border",
                ElementPath: "/Window/Border[1]",
                ControlName: "DeviceCard",
                PropertyName: "Background",
                ResourceKey: "AppTheme.CardBackground",
                ValueExpression: "{StaticResource AppTheme.CardBackground}",
                Selector: null),
            new(
                AXAMLProvenanceKind.PropertyAssignment,
                SourcePath: "FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.axaml",
                Line: 95,
                Column: 9,
                OwnerTypeName: "FanControlTrayAppDotNET.UI.Flyout.FanFlyoutWindow",
                ElementTypeName: "Border",
                ElementPath: "/Window/Border[1]",
                ControlName: "DeviceCard",
                PropertyName: "Grid.Row",
                ResourceKey: null,
                ValueExpression: "1",
                Selector: null),
            new(
                AXAMLProvenanceKind.StyleSetter,
                SourcePath: "TrayAppDotNETCommon/src/Visuals/AppTheme.axaml",
                Line: 130,
                Column: 13,
                OwnerTypeName: "TrayAppDotNETCommon.Visuals.AppThemeResources",
                ElementTypeName: "Setter",
                ElementPath: "/ResourceDictionary/Style[1]/Setter[1]",
                ControlName: null,
                PropertyName: "Opacity",
                ResourceKey: null,
                ValueExpression: "0.8",
                Selector: "Border"),
            new(
                AXAMLProvenanceKind.Template,
                SourcePath: "TrayAppDotNETCommon/src/Visuals/AppTheme.axaml",
                Line: 145,
                Column: 17,
                OwnerTypeName: "TrayAppDotNETCommon.Visuals.AppThemeResources",
                ElementTypeName: "ControlTemplate",
                ElementPath: "/ResourceDictionary/ControlTheme[1]/Setter[1]/ControlTemplate[1]",
                ControlName: null,
                PropertyName: "Template",
                ResourceKey: "ButtonTheme",
                ValueExpression: "<ControlTemplate>",
                Selector: "ControlTheme:ButtonTheme")
        ];

        DebugUIProvenance.RegisterAXAML(typeof(DebugUIProvenanceTests).Assembly, entries);

        IReadOnlyList<AXAMLProvenanceEntry> properties = DebugUIProvenance.FindAXAMLPropertyEntries(
            ["FanControlTrayAppDotNET.UI.Flyout.FanFlyoutWindow"],
            elementTypeName: "Border",
            controlName: "DeviceCard",
            propertyName: "Background");
        IReadOnlyList<AXAMLProvenanceEntry> attachedProperties =
            DebugUIProvenance.FindAXAMLPropertyEntries(
                ["FanControlTrayAppDotNET.UI.Flyout.FanFlyoutWindow"],
                elementTypeName: "Border",
                controlName: "DeviceCard",
                propertyName: "Row");
        IReadOnlyList<AXAMLProvenanceEntry> reusableStyles =
            DebugUIProvenance.FindAXAMLPropertyEntries(
                ["FanControlTrayAppDotNET.UI.Flyout.FanFlyoutWindow"],
                elementTypeName: "Border",
                controlName: "DeviceCard",
                propertyName: "Opacity");
        IReadOnlyList<AXAMLProvenanceEntry> reusableTemplates =
            DebugUIProvenance.FindAXAMLPropertyEntries(
                ["FanControlTrayAppDotNET.UI.Flyout.FanFlyoutWindow"],
                elementTypeName: "Button",
                controlName: null,
                propertyName: "Template");
        IReadOnlyList<AXAMLProvenanceEntry> resources =
            DebugUIProvenance.FindAXAMLResourceDefinitions("AppTheme.CardBackground");

        Assert.Single(properties);
        Assert.Single(attachedProperties);
        Assert.Single(reusableStyles);
        Assert.Single(reusableTemplates);
        Assert.Contains(resources,
            entry => entry.SourcePath.EndsWith(value: "AppTheme.axaml", StringComparison.Ordinal));
    }

    [Fact]
    public void InspectorDisplaysInstrumentedAndAXAMLPropertySources() => AvaloniaTestHost.Run(() =>
    {
        const string ResourceKey = "AppTheme.CardBackground";
        AXAMLProvenanceEntry[] entries =
        [
            new(
                AXAMLProvenanceKind.ResourceDefinition,
                SourcePath: "TrayAppDotNETCommon/src/Visuals/AppTheme.axaml",
                Line: 27,
                Column: 5,
                OwnerTypeName: "TrayAppDotNETCommon.Visuals.AppThemeResources",
                ElementTypeName: "ThemeColor",
                ElementPath: "/ResourceDictionary/ThemeColor[1]",
                ControlName: null,
                PropertyName: null,
                ResourceKey,
                ValueExpression: "LightHex=\"#FBFBFB\" DarkHex=\"#2B2B2B\"",
                Selector: null),
            new(
                AXAMLProvenanceKind.ResourceReference,
                SourcePath: "FanControlTrayAppDotNET/src/UI/Flyout/FanFlyoutWindow.axaml",
                Line: 94,
                Column: 9,
                OwnerTypeName: "Avalonia.Controls.Window",
                ElementTypeName: "Border",
                ElementPath: "/Window/Border[1]",
                ControlName: "DeviceCard",
                PropertyName: "Background",
                ResourceKey,
                ValueExpression: "{StaticResource AppTheme.CardBackground}",
                Selector: null)
        ];

        DebugUIProvenance.RegisterAXAML(typeof(DebugUIProvenanceTests).Assembly, entries);

        SolidColorBrush background = new(Color.Parse("#2B2B2B"));
        Border border = new() { Name = "DeviceCard", Background = background };
        Window window = new() { Content = border };
        DebugUIProvenance.RecordProperty(
            border,
            Border.BackgroundProperty,
            background,
            DebugPropertyAssignmentOperation.Builder,
            valueExpression: "palette.CardBackground",
            sourceFilePath: @"C:\repo\TrayAppDotNET\TrayAppDotNETCommon\src\UI\Cards.cs",
            sourceLine: 73,
            sourceMember: "CreateCard",
            ResourceKey);
        DebugUIProvenance.RecordProperty(
            background,
            SolidColorBrush.ColorProperty,
            background.Color,
            DebugPropertyAssignmentOperation.SetCurrentValue,
            valueExpression: "sourcePalette.CardBackground",
            sourceFilePath: @"C:\repo\TrayAppDotNET\TrayAppDotNETCommon\src\UI\Controls\SettingsUI.cs",
            sourceLine: 271,
            sourceMember: "UpdateFrom");

        window.Show();
        ControlHoverInspectorSnapshot snapshot = ControlHoverInspectorSnapshotBuilder.Build(window, border);
        List<string> rows = Flatten(snapshot.Roots);

        Assert.Contains(rows, row => row.Contains(value: "Instrumented C# assignments (1)", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains(value: "palette.CardBackground", StringComparison.Ordinal));
        Assert.Contains(rows,
            row => row.Contains(value: "TrayAppDotNETCommon/src/UI/Cards.cs:73", StringComparison.Ordinal));
        Assert.Contains(rows,
            row => row.Contains(value: "Assigned SolidColorBrush provenance", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains(value: "sourcePalette.CardBackground", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains(value: "SettingsUI.cs:271", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains(value: "AXAML source candidates (1)", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains(value: "FanFlyoutWindow.axaml:94:9", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains(value: "Resource definition candidates (", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains(value: "AppTheme.axaml:27:5", StringComparison.Ordinal));

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
            valueExpression: "SettingsUILayout.DescriptionOpacity",
            resourceKey: "SettingsUI.DescriptionOpacity");

        window.Show();
        ControlHoverInspectorSnapshot snapshot = ControlHoverInspectorSnapshotBuilder.Build(window, border);
        List<string> rows = Flatten(snapshot.Roots);

        Assert.Contains(rows, row => row.Contains(value: "SettingsUI.DescriptionOpacity", StringComparison.Ordinal));
        Assert.Contains(
            rows,
            row => row.Contains(
                value: "TrayAppDotNETCommon/src/UI/Controls/SettingsUI.axaml",
                StringComparison.Ordinal));

        window.Close();
    });

    [Fact]
    public void InspectorLinksContentGlyphAndLayoutValuesToAXAML() => AvaloniaTestHost.Run(() =>
    {
        AXAMLProvenanceEntry[] entries =
        [
            ResourceDefinition(resourceKey: "InspectorGlyph.Unlock", sourcePath: "InspectorGlyphs.axaml", line: 7,
                valueExpression: "Text=G"),
            ResourceDefinition(resourceKey: "InspectorLayout.ModeButtonWidth", sourcePath: "InspectorFlyout.axaml",
                line: 11, valueExpression: "33"),
            ResourceDefinition(resourceKey: "InspectorLayout.ModeButtonHeight", sourcePath: "InspectorFlyout.axaml",
                line: 12, valueExpression: "29"),
            ResourceDefinition(resourceKey: "InspectorLayout.ModeButtonFontSize", sourcePath: "InspectorFlyout.axaml",
                line: 13, valueExpression: "17")
        ];
        DebugUIProvenance.RegisterAXAML(typeof(DebugUIProvenanceTests).Assembly, entries);

        Glyph glyph = new(text: "G", TADNFont.SegoeFluentIcons);
        DebugUIProvenance.RegisterGlyphResource(glyph, resourceKey: "InspectorGlyph.Unlock");
        TextBlock glyphText = new() { FontSize = 17 };
        GlyphApplicator.ApplyTo(glyphText, glyph);
        Border button = new() { Width = 33, Height = 29, Child = glyphText };
        TrayAppDotNETToolTip.SetTip(button, tip: "Useful tip");

        AvaloniaProperty tooltipObjectProperty = AvaloniaPropertyRegistry.Instance
            .GetRegisteredAttached(typeof(Border))
            .Single(property => property.OwnerType == typeof(ToolTip) && property.Name == "ToolTip");
        button.SetValue(tooltipObjectProperty, new ToolTip());

        InspectorResourceWindow window = new() { Content = button };
        window.Show();
        ControlHoverInspectorSnapshot snapshot = ControlHoverInspectorSnapshotBuilder.Build(window, button);
        List<string> rows = Flatten(snapshot.Roots);

        Assert.Contains(rows, row => row.Contains(value: "[content 1] TextBlock", StringComparison.Ordinal));
        Assert.Contains(rows,
            row => row.Contains(value: "AXAML InspectorGlyph.Unlock @ InspectorGlyphs.axaml:7",
                StringComparison.Ordinal));
        Assert.Contains(rows,
            row => row.Contains(value: "AXAML InspectorLayout.ModeButtonFontSize @ InspectorFlyout.axaml:13",
                StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains(value: "Tip = \"Useful tip\"", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, row => row.StartsWith(value: "ToolTip = ", StringComparison.Ordinal));

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
            Column: 5,
            typeof(InspectorResourceWindow).FullName ?? nameof(InspectorResourceWindow),
            ElementTypeName: "sys:Double",
            ElementPath: "/Window/Resources/sys:Double[1]",
            ControlName: null,
            PropertyName: null,
            resourceKey,
            valueExpression,
            Selector: null);

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
        DebugUIProvenance.RecordProperty(border, Border.OpacityProperty, value: 0.5);
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

    private readonly struct InspectorLayoutAxamlProperties()
    {
        private readonly double _scale = 1;

        public double ModeButtonWidth => 33 * _scale;

        public double ModeButtonHeight => 29 * _scale;

        public double ModeButtonFontSize => 17 * _scale;
    }
}
#endif
