using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FanControlTrayAppDotNET.Services;
using TrayAppDotNETCommon.UI.ControlMapping;
using GlyphApplicator = TrayAppDotNETCommon.Visuals.GlyphApplicator;

namespace FanControlTrayAppDotNET.UI.Settings;

public sealed partial class FanSettingsWindow
{
    private const int NonFunctioningFanColumnCount = 2;
    private const double NonFunctioningFanColumnGap = 8.0;
    private const double NonFunctioningFanCardBottomGap = 6.0;

    private readonly List<FanPropertiesPageGeneration> _fanPropertiesPageGenerations = [];

    private StackPanel BuildFanPropertiesPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_FanProperties_SectionHeader)), p);

        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_FanProperties_DefaultJumpstartDutyCycle_Title)),
            L(nameof(AppStrings.Settings_FanProperties_DefaultJumpstartDutyCycle_Description)),
            _settings.DefaultJumpstartDutyCycle,
            min: 0,
            max: 100,
            v => _settings.DefaultJumpstartDutyCycle = v,
            p,
            suffix: "%",
            [
                L(nameof(AppStrings.Settings_FanProperties_DefaultJumpstart_SearchKeywords))
            ],
            node: ControlMap.Settings.FanPropertiesPage.DefaultJumpstartDutyCycle));
        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_FanProperties_DefaultDeltaMaxDutyCycle_Title)),
            L(nameof(AppStrings.Settings_FanProperties_DefaultDeltaMaxDutyCycle_Description)),
            _settings.DefaultDeltaMaxDutyCycle,
            min: 0,
            max: 100,
            v => _settings.DefaultDeltaMaxDutyCycle = v,
            p,
            suffix: "%/s",
            [
                L(nameof(AppStrings.Settings_FanProperties_DefaultDeltaMax_SearchKeywords))
            ],
            node: ControlMap.Settings.FanPropertiesPage.DefaultDeltaMaxDutyCycle));
        stack.Children.Add(ComboCard(
            L(nameof(AppStrings.Settings_FanProperties_DefaultAssignedCurve_Title)),
            L(nameof(AppStrings.Settings_FanProperties_DefaultAssignedCurve_Description)),
            CurveOptions(),
            string.IsNullOrWhiteSpace(_settings.DefaultAssignedCurve) ? "None" : _settings.DefaultAssignedCurve,
            tag => _settings.DefaultAssignedCurve = tag,
            p,
            autoSizeToText: true,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_FanProperties_DefaultCurve_SearchKeywords))
            ],
            node: ControlMap.Settings.FanPropertiesPage.DefaultAssignedCurve));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_FanProperties_Reassign_Header)), p));
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L(nameof(AppStrings.Settings_FanProperties_Reassign_Description)),
            p,
            new Thickness(left: 0, top: 0, right: 0, bottom: 12)));

        FanPropertiesPageGeneration pageGeneration = new();
        ControlNames.Assign(pageGeneration.FanSlotPanel, parentName: "FanSlots");
        _fanPropertiesPageGenerations.Add(pageGeneration);
        AddPageCleanup(() => RetireFanPropertiesPage(pageGeneration));
        RebuildFanSlots(pageGeneration);
        stack.Children.Add(RawCard(
            pageGeneration.FanSlotPanel,
            p,
            [
                L(nameof(AppStrings.Settings_FanProperties_Reassign_SearchKeywords))
            ]));

        SettingsButton apply = ControlNames.Assign(
            Button(L(nameof(AppStrings.Settings_FanProperties_ApplyFanSwaps_Button)), p)
                .MapTo(ControlMap.Settings.FanPropertiesPage.FanSlots.ApplyFanSwaps),
            parentName: "FanSlots");
        apply.HorizontalAlignment = HorizontalAlignment.Right;
        apply.Margin = new Thickness(left: 0, top: 6, right: 0, bottom: 14);
        apply.IsEnabled = pageGeneration.FanSlots.Count > 1;
        apply.Click += (_, _) => ApplyFanSlotSwaps(pageGeneration);
        stack.Children.Add(apply);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_FanProperties_NonFunctioning_Header)), p));

        List<Fan> liveFans = GetLiveFans();
        if (liveFans.Count == 0)
        {
            stack.Children.Add(RawCard(
                TrayAppDotNETSettingsUI.DescriptionText(
                    L(nameof(AppStrings.Settings_FanProperties_NoFans)), p),
                p,
                [
                    L(nameof(AppStrings.Settings_FanProperties_NonFunctioning_SearchKeywords))
                ]));
            return stack;
        }

        Grid nonFunctioningFanGrid = ControlNames.Assign(new Grid(), parentName: "NonFunctioningFans");
        for (int i = 0; i < NonFunctioningFanColumnCount; i++)
            nonFunctioningFanGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (int i = 0; i < liveFans.Count; i++)
        {
            if (i % NonFunctioningFanColumnCount == 0)
                nonFunctioningFanGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            Fan fan = liveFans[i];
            int column = i % NonFunctioningFanColumnCount;
            int row = i / NonFunctioningFanColumnCount;
            Border card = BoolCard(
                fan.DisplayName,
                fan.ControllerDisplayLabel,
                fan.ForcedNonFunctioning,
                value =>
                {
                    fan.ForcedNonFunctioning = value;
                    AppServices.LHMService?.PersistLiveState(false);
                },
                p,
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_FanProperties_NonFunctioning_SearchKeywords))
                ],
                node: ControlMap.Settings.FanPropertiesPage.ForcedNonFunctioning);
            card.Margin = new Thickness(
                column == 0 ? 0 : NonFunctioningFanColumnGap / 2.0,
                top: 0,
                column == NonFunctioningFanColumnCount - 1 ? 0 : NonFunctioningFanColumnGap / 2.0,
                NonFunctioningFanCardBottomGap);
            Grid.SetColumn(card, column);
            Grid.SetRow(card, row);
            nonFunctioningFanGrid.Children.Add(card);
        }

        stack.Children.Add(nonFunctioningFanGrid);

        return stack;
    }

    private void RebuildFanSlots(FanPropertiesPageGeneration pageGeneration)
    {
        pageGeneration.FanSlots.Clear();
        foreach (Fan fan in GetLiveFans())
        {
            pageGeneration.FanSlots.Add(new FanSettingsSlotEntry(
                KeyForFan(fan),
                fan.DisplayName,
                fan.ControllerDisplayLabel));
        }

        RenderFanSlots(pageGeneration);
    }

    private void RenderFanSlots(FanPropertiesPageGeneration pageGeneration)
    {
        pageGeneration.FanSlotPanel.Children.Clear();
        if (pageGeneration.FanSlots.Count == 0)
        {
            TextBlock empty = ControlNames.Assign(
                TrayAppDotNETSettingsUI.DescriptionText(
                    L(nameof(AppStrings.Settings_FanProperties_NoFans)),
                    Palette),
                parentName: "FanSlots");
            pageGeneration.FanSlotPanel.Children.Add(empty);
            return;
        }

        for (int index = 0; index < pageGeneration.FanSlots.Count; index++)
        {
            pageGeneration.FanSlotPanel.Children.Add(
                BuildFanSlotRow(pageGeneration, pageGeneration.FanSlots[index], index, Palette));
        }
    }

    private Border BuildFanSlotRow(
        FanPropertiesPageGeneration pageGeneration,
        FanSettingsSlotEntry slot,
        int index,
        SettingsPalette p)
    {
        TextBlock handle = ControlNames.Assign(
            TrayAppDotNETSettingsUI.Text(GlyphCatalog.DRAG_HANDLE.Text, p, fontSize: 16),
            parentName: "FanSlot");
        GlyphApplicator.ApplyTo(handle, GlyphCatalog.DRAG_HANDLE);
        handle.Width = 28;
        handle.VerticalAlignment = VerticalAlignment.Center;
        handle.HorizontalAlignment = HorizontalAlignment.Center;

        StackPanel text = new();
        text.Children.Add(TrayAppDotNETSettingsUI.TitleText(slot.DisplayName, p));
        text.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(slot.Detail, p));

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.Children.Add(handle);
        Grid.SetColumn(text, value: 1);
        grid.Children.Add(text);

        Border row = ControlNames.Assign(
            new Border
            {
                Tag = slot,
                Background = TrayAppDotNETSettingsUI.Brush(p.ControlBackground),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = RadiusMedium,
                Padding = new Thickness(8),
                Margin = new Thickness(left: 0, top: 0, right: 0, bottom: 4),
                Child = grid,
                Focusable = true,
                Cursor = TrayAppDotNETCursors.Hand
            }.MapTo(ControlMap.Settings.FanPropertiesPage.FanSlots.FanSlot),
            parentName: "FanSlot");
        ControlNames.AssignLogicalSubtree(row, parentName: "FanSlot");

        bool pointerOver = false;
        bool pointerPressed = false;
        UpdateFanSlotRowVisual(pageGeneration, row, slot, p, pointerOver, pointerPressed);

        row.PointerEntered += (_, _) =>
        {
            pointerOver = true;
            UpdateFanSlotRowVisual(pageGeneration, row, slot, p, pointerOver, pointerPressed);
        };
        row.PointerExited += (_, _) =>
        {
            pointerOver = false;
            pointerPressed = false;
            UpdateFanSlotRowVisual(pageGeneration, row, slot, p, pointerOver, pointerPressed);
        };
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            if (pageGeneration.CapturedPointer != null)
            {
                e.Handled = true;
                return;
            }

            pageGeneration.DraggedSlot = slot;
            pageGeneration.DraggedSlotRow = row;
            pageGeneration.DragStart = e.GetPosition(pageGeneration.FanSlotPanel);
            pageGeneration.DraggedSlotPointerOffsetY = e.GetPosition(row).Y;
            pageGeneration.DraggedSlotHeight = Math.Max(val1: 1, row.Bounds.Height);
            pageGeneration.DraggedSlotTargetIndex = pageGeneration.FanSlots.IndexOf(slot);
            pointerPressed = true;
            UpdateFanSlotRowVisual(pageGeneration, row, slot, p, pointerOver, pointerPressed);
            CaptureFanSlotPointer(
                pageGeneration,
                e.Pointer,
                row,
                () =>
                {
                    pointerPressed = false;
                    ResetFanSlotGesture(pageGeneration, e.Pointer);
                    UpdateFanSlotRowVisual(pageGeneration, row, slot, p, pointerOver, pointerPressed);
                });
            e.Handled = true;
        };
        row.PointerMoved += (_, e) =>
        {
            if (!ReferenceEquals(pageGeneration.CapturedPointer, e.Pointer)) return;
            if (pageGeneration.DraggedSlot == null) return;
            Point current = e.GetPosition(pageGeneration.FanSlotPanel);
            if (Math.Abs(current.Y - pageGeneration.DragStart.Y) < 4) return;
            double draggedMidpoint = current.Y - pageGeneration.DraggedSlotPointerOffsetY
                                     + pageGeneration.DraggedSlotHeight / 2.0;
            pageGeneration.DraggedSlotTargetIndex =
                FanSlotInsertionIndexFromMidpoint(pageGeneration, draggedMidpoint);
            ApplyFanSlotDragPreview(pageGeneration);
            row.RenderTransform = new TranslateTransform(x: 0, current.Y - pageGeneration.DragStart.Y);
            e.Handled = true;
        };
        row.PointerReleased += (_, e) =>
        {
            if (!ReferenceEquals(pageGeneration.CapturedPointer, e.Pointer)) return;
            pointerPressed = false;
            EndFanSlotDrag(pageGeneration, e.Pointer);
        };
        row.PointerCaptureLost += (_, e) =>
        {
            if (!ReferenceEquals(pageGeneration.CapturedPointer, e.Pointer)) return;
            pointerPressed = false;
            if (pageGeneration is { IsRetiring: false, IsResettingGesture: false })
                EndFanSlotDrag(pageGeneration, e.Pointer);
        };
        row.KeyDown += (_, e) =>
        {
            if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
            if (e.Key is not (Key.Up or Key.Down)) return;
            int currentIndex = pageGeneration.FanSlots.IndexOf(slot);
            int nextIndex = e.Key == Key.Up ? currentIndex - 1 : currentIndex + 1;
            if (currentIndex >= 0 && nextIndex >= 0 && nextIndex < pageGeneration.FanSlots.Count)
            {
                pageGeneration.FanSlots.RemoveAt(currentIndex);
                pageGeneration.FanSlots.Insert(nextIndex, slot);
                RenderFanSlots(pageGeneration);
            }

            e.Handled = true;
        };

        TrayAppDotNETToolTip.SetTip(row, tip: L(nameof(AppStrings.Settings_FanProperties_FanSettingsCard_Tooltip)));
        _ = index;
        return row;
    }

    private static void UpdateFanSlotRowVisual(
        FanPropertiesPageGeneration pageGeneration,
        Border row,
        FanSettingsSlotEntry slot,
        SettingsPalette p,
        bool pointerOver,
        bool pointerPressed)
    {
        bool dragging = ReferenceEquals(slot, pageGeneration.DraggedSlot);
        Color background = pointerPressed
            ? p.Pressed
            : pointerOver
                ? p.Hover
                : p.ControlBackground;
        row.Background = TrayAppDotNETSettingsUI.Brush(background);
        row.BorderBrush = TrayAppDotNETSettingsUI.Brush(dragging ? p.Accent : Colors.Transparent);
        row.BorderThickness = dragging ? new Thickness(1) : new Thickness(0);
        row.Opacity = dragging ? 0.82 : 1.0;
        row.SetValue(ZIndexProperty, dragging ? 1 : 0);
    }

    private static int FanSlotInsertionIndexFromMidpoint(
        FanPropertiesPageGeneration pageGeneration,
        double draggedMidpointY)
    {
        int insertion = 0;
        for (int index = 0; index < pageGeneration.FanSlotPanel.Children.Count; index++)
        {
            Control child = pageGeneration.FanSlotPanel.Children[index];
            if (ReferenceEquals(child, pageGeneration.DraggedSlotRow)) continue;
            Point? topLeft = child.TranslatePoint(new Point(x: 0, y: 0), pageGeneration.FanSlotPanel);
            if (topLeft == null) continue;
            if (draggedMidpointY > topLeft.Value.Y + child.Bounds.Height / 2.0) insertion++;
            else break;
        }

        int max = pageGeneration.FanSlots.Count - (pageGeneration.DraggedSlot != null ? 1 : 0);
        return Math.Clamp(insertion, min: 0, Math.Max(val1: 0, max));
    }

    private static void ApplyFanSlotDragPreview(FanPropertiesPageGeneration pageGeneration)
    {
        if (pageGeneration.DraggedSlot == null || pageGeneration.DraggedSlotRow == null) return;
        ResetFanSlotDragPreview(pageGeneration);

        int sourceIndex = pageGeneration.FanSlots.IndexOf(pageGeneration.DraggedSlot);
        if (sourceIndex < 0) return;

        int targetIndex = Math.Clamp(
            pageGeneration.DraggedSlotTargetIndex,
            min: 0,
            Math.Max(val1: 0, pageGeneration.FanSlots.Count - 1));
        double offset = Math.Max(val1: 1, pageGeneration.DraggedSlotHeight
                                          + Math.Max(val1: 0, pageGeneration.DraggedSlotRow.Margin.Bottom));
        if (targetIndex < sourceIndex)
        {
            for (int i = targetIndex; i < sourceIndex; i++)
                SetFanSlotPreviewOffset(pageGeneration, i, offset);
        }
        else if (targetIndex > sourceIndex)
        {
            for (int index = sourceIndex + 1;
                 index <= targetIndex && index < pageGeneration.FanSlotPanel.Children.Count;
                 index++)
                SetFanSlotPreviewOffset(pageGeneration, index, -offset);
        }
    }

    private static void SetFanSlotPreviewOffset(
        FanPropertiesPageGeneration pageGeneration,
        int index,
        double offset)
    {
        if (index < 0 || index >= pageGeneration.FanSlotPanel.Children.Count) return;
        if (ReferenceEquals(pageGeneration.FanSlotPanel.Children[index], pageGeneration.DraggedSlotRow)) return;
        pageGeneration.FanSlotPanel.Children[index].RenderTransform = new TranslateTransform(x: 0, offset);
    }

    private static void ResetFanSlotDragPreview(FanPropertiesPageGeneration pageGeneration)
    {
        foreach (Control child in pageGeneration.FanSlotPanel.Children)
        {
            if (ReferenceEquals(child, pageGeneration.DraggedSlotRow)) continue;
            child.RenderTransform = null;
        }
    }

    private void EndFanSlotDrag(FanPropertiesPageGeneration pageGeneration, IPointer? pointer)
    {
        FanSettingsSlotEntry? dragged = pageGeneration.DraggedSlot;
        int targetIndex = pageGeneration.DraggedSlotTargetIndex;
        bool hadDrag = dragged != null;
        ResetFanSlotGesture(pageGeneration, pointer);
        if (dragged != null && targetIndex >= 0)
        {
            int currentIndex = pageGeneration.FanSlots.IndexOf(dragged);
            if (currentIndex >= 0 && targetIndex != currentIndex)
            {
                pageGeneration.FanSlots.RemoveAt(currentIndex);
                pageGeneration.FanSlots.Insert(
                    Math.Clamp(targetIndex, min: 0, pageGeneration.FanSlots.Count), dragged);
            }
        }

        if (hadDrag) RenderFanSlots(pageGeneration);
    }

    private static void ResetFanSlotGesture(
        FanPropertiesPageGeneration pageGeneration,
        IPointer? fallbackPointer)
    {
        bool wasResetting = pageGeneration.IsResettingGesture;
        pageGeneration.IsResettingGesture = true;
        IPointer? capturedPointer = pageGeneration.CapturedPointer ?? fallbackPointer;
        pageGeneration.CapturedPointer = null;
        Border? draggedRow = pageGeneration.DraggedSlotRow;
        pageGeneration.DraggedSlotRow = null;
        pageGeneration.DraggedSlot = null;
        pageGeneration.DragStart = default;
        pageGeneration.DraggedSlotTargetIndex = -1;
        pageGeneration.DraggedSlotPointerOffsetY = 0;
        pageGeneration.DraggedSlotHeight = 0;
        try
        {
            draggedRow?.RenderTransform = null;
            foreach (Control child in pageGeneration.FanSlotPanel.Children)
                child.RenderTransform = null;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"FanSettingsWindow slot visual reset failed: {exception.Message}");
        }
        finally
        {
            ReleaseFanSlotPointer(capturedPointer, gestureName: "slot drag");
            pageGeneration.IsResettingGesture = wasResetting;
        }
    }

    private static void CaptureFanSlotPointer(
        FanPropertiesPageGeneration pageGeneration,
        IPointer pointer,
        Control target,
        Action rollbackGesture)
    {
        if (pageGeneration.CapturedPointer != null)
            throw new InvalidOperationException("The Fan Properties page already owns a pointer capture.");

        pageGeneration.CapturedPointer = pointer;
        try
        {
            pointer.Capture(target);
        }
        catch
        {
            if (ReferenceEquals(pageGeneration.CapturedPointer, pointer))
                pageGeneration.CapturedPointer = null;
            bool wasResetting = pageGeneration.IsResettingGesture;
            pageGeneration.IsResettingGesture = true;
            try
            {
                rollbackGesture();
            }
            finally
            {
                pageGeneration.IsResettingGesture = wasResetting;
            }

            throw;
        }
    }

    private static void ReleaseFanSlotPointer(IPointer? pointer, string gestureName)
    {
        if (pointer == null) return;

        try
        {
            pointer.Capture(null);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"FanSettingsWindow {gestureName} pointer release failed: {exception.Message}");
        }
    }

    private void RetireFanPropertiesPage(FanPropertiesPageGeneration pageGeneration)
    {
        pageGeneration.IsRetiring = true;
        try
        {
            ResetFanSlotGesture(pageGeneration, fallbackPointer: null);
        }
        finally
        {
            pageGeneration.FanSlotPanel.Children.Clear();
            pageGeneration.FanSlots.Clear();
            _fanPropertiesPageGenerations.Remove(pageGeneration);
        }
    }

    private void ApplyFanSlotSwaps(FanPropertiesPageGeneration pageGeneration)
    {
        List<Fan> fans = GetLiveFans();
        if (fans.Count < 2 || fans.Count != pageGeneration.FanSlots.Count) return;

        Dictionary<string, FanUserSettings> snapshots = fans
            .ToDictionary(KeyForFan, f => f.SnapshotUserSettings(), StringComparer.OrdinalIgnoreCase);
        if (pageGeneration.FanSlots.Any(slot => !snapshots.ContainsKey(slot.Key))) return;

        for (int index = 0; index < fans.Count; index++)
            fans[index].ApplyUserSettings(snapshots[pageGeneration.FanSlots[index].Key]);

        AppServices.LHMService?.PersistLiveState(false);
        Save();
        RebuildFanSlots(pageGeneration);
    }

    private static List<Fan> GetLiveFans() => AppServices.LHMService?.Fans.ToList() ?? [];

    private static string KeyForFan(Fan fan) =>
        !string.IsNullOrWhiteSpace(fan.DataSourceKey)
            ? fan.DataSourceKey
            : $"{fan.ControllerModel}.{fan.ControlsName}.{fan.FansName}";

    private static List<(string Tag, string Text)> CurveOptions()
    {
        List<(string Tag, string Text)> items = [("None", L(nameof(AppStrings.Settings_FanProperties_NoCurve)))];
        foreach (Curve curve in Curve.Curves.Values.OrderBy(c => c.CurveName, StringComparer.OrdinalIgnoreCase))
            items.Add((curve.CurveName, curve.CurveName));
        return items;
    }

    /// <summary>Owns retained controls and gesture state for one Fan Properties page candidate.</summary>
    private sealed class FanPropertiesPageGeneration
    {
        public List<FanSettingsSlotEntry> FanSlots { get; } = [];
        public StackPanel FanSlotPanel { get; } = new();
        public Border? DraggedSlotRow { get; set; }
        public FanSettingsSlotEntry? DraggedSlot { get; set; }
        public IPointer? CapturedPointer { get; set; }
        public Point DragStart { get; set; }
        public double DraggedSlotPointerOffsetY { get; set; }
        public double DraggedSlotHeight { get; set; }
        public int DraggedSlotTargetIndex { get; set; } = -1;
        public bool IsResettingGesture { get; set; }
        public bool IsRetiring { get; set; }
    }

    private sealed record FanSettingsSlotEntry(string Key, string DisplayName, string Detail);
}
