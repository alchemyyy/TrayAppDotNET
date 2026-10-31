#pragma warning disable CA1822

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Settings;
using GlyphApplicator = TrayAppDotNETCommon.Visuals.GlyphApplicator;

namespace BatteryTrayAppDotNET.UI.Settings;

public sealed partial class BatterySettingsWindow
{
    private readonly List<StackPanel> _triggerPagePanels = [];
    private StackPanel? _triggerPanel;
    private StackPanel? _draggedTriggerPanel;
    private Border? _draggedTriggerRow;
    private BatteryTriggerEntry? _draggedTrigger;
    private IPointer? _triggerCapturedPointer;
    private Point _triggerDragStart;
    private double _draggedTriggerPointerOffsetY;
    private double _draggedTriggerHeight;
    private int _draggedTriggerTargetIndex = -1;

    private StackPanel BuildTriggersPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_Triggers_SectionHeader)), p);
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L(nameof(AppStrings.Settings_Triggers_Description)),
            p,
            new Thickness(left: 0, top: 0, right: 0, bottom: 12)));

        _settings.EnsureTriggerDefaults();
        StackPanel triggerPanel = new();
        _triggerPagePanels.Add(triggerPanel);
        _triggerPanel = triggerPanel;
        AddPageCleanup(() => CleanupTriggerPage(triggerPanel));
        RenderTriggerCards();
        stack.Children.Add(triggerPanel);
        return stack;
    }

    private void RenderTriggerCards()
    {
        if (_triggerPanel == null) return;
        _triggerPanel.Children.Clear();

        if (_settings.Triggers.Count == 0)
        {
            Border emptyCard = RawCard(
                TrayAppDotNETSettingsUI.DescriptionText(L(nameof(AppStrings.Settings_Triggers_Empty)), Palette),
                Palette);
            ControlNames.AssignLogicalSubtree(emptyCard, parentName: "TriggerCard");
            _triggerPanel.Children.Add(emptyCard);
            return;
        }

        for (int i = 0; i < _settings.Triggers.Count; i++)
            _triggerPanel.Children.Add(BuildTriggerCard(_settings.Triggers[i], i, Palette));
    }

    private Border BuildTriggerCard(BatteryTriggerEntry trigger, int index, SettingsPalette p)
    {
        TextBlock title = TrayAppDotNETSettingsUI.TitleText(
            string.IsNullOrWhiteSpace(trigger.Title)
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    L(nameof(AppStrings.Settings_Triggers_DefaultTitle_Format)),
                    index + 1)
                : trigger.Title,
            p);
        title.FontWeight = FontWeight.SemiBold;

        SettingsComboBox condition = BuildNullableTriggerCombo(
            L(nameof(AppStrings.Settings_Triggers_Condition_Placeholder)),
            TriggerConditionOptions(),
            trigger.Condition,
            value => trigger.Condition = value,
            p).MapTo(ControlMap.Settings.TriggersPage.Trigger.Condition);
        SettingsComboBox action = BuildNullableTriggerCombo(
            L(nameof(AppStrings.Settings_Triggers_Action_Placeholder)),
            TriggerActionOptions(),
            trigger.Action,
            value => trigger.Action = value,
            p).MapTo(ControlMap.Settings.TriggersPage.Trigger.Action);

        TextBlock arrow = TrayAppDotNETSettingsUI.Text(text: "->", p, fontSize: 14, FontWeight.SemiBold);
        arrow.HorizontalAlignment = HorizontalAlignment.Center;
        arrow.VerticalAlignment = VerticalAlignment.Center;
        arrow.Margin = new Thickness(horizontal: 8, vertical: 0);

        Grid selectorRow = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star) { MinWidth = 153 },
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star) { MinWidth = 153 }
            }
        };
        condition.HorizontalAlignment = HorizontalAlignment.Stretch;
        action.HorizontalAlignment = HorizontalAlignment.Stretch;
        selectorRow.Children.Add(condition);
        Grid.SetColumn(arrow, value: 1);
        selectorRow.Children.Add(arrow);
        Grid.SetColumn(action, value: 2);
        selectorRow.Children.Add(action);

        StackPanel content = new() { Spacing = 8 };
        content.Children.Add(title);
        content.Children.Add(selectorRow);

        Border card = new()
        {
            Tag = trigger,
            Background = TrayAppDotNETSettingsUI.Brush(p.CardBackground),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = RadiusLarge,
            Padding = new Thickness(horizontal: 16, vertical: 12),
            Margin = new Thickness(left: 0, top: 0, right: 0, bottom: 6),
            Child = content,
            Focusable = true,
            Cursor = TrayAppDotNETCursors.Hand
        };
        ControlNames.Assign(card, parentName: "TriggerCard");
        card.MapTo(ControlMap.Settings.TriggersPage.Trigger.ID);
        card.MapCommand(ControlMap.Settings.TriggersPage.Trigger.MoveUp, () => MoveTrigger(trigger, offset: -1));
        card.MapCommand(ControlMap.Settings.TriggersPage.Trigger.MoveDown, () => MoveTrigger(trigger, offset: 1));

        bool pointerOver = false;
        bool pointerPressed = false;
        UpdateTriggerCardVisual(card, trigger, p, pointerOver, pointerPressed);

        card.PointerEntered += (_, _) =>
        {
            pointerOver = true;
            UpdateTriggerCardVisual(card, trigger, p, pointerOver, pointerPressed);
        };
        card.PointerExited += (_, _) =>
        {
            pointerOver = false;
            pointerPressed = false;
            UpdateTriggerCardVisual(card, trigger, p, pointerOver, pointerPressed);
        };
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            if (IsTriggerCardInteractiveSource(e.Source, card)) return;
            StackPanel? triggerPanel = _triggerPanel;
            if (triggerPanel == null) return;

            _draggedTrigger = trigger;
            _draggedTriggerPanel = triggerPanel;
            _draggedTriggerRow = card;
            _triggerCapturedPointer = e.Pointer;
            _triggerDragStart = e.GetPosition(triggerPanel);
            _draggedTriggerPointerOffsetY = e.GetPosition(card).Y;
            _draggedTriggerHeight = Math.Max(val1: 1, card.Bounds.Height);
            _draggedTriggerTargetIndex = _settings.Triggers.IndexOf(trigger);
            pointerPressed = true;
            UpdateTriggerCardVisual(card, trigger, p, pointerOver, pointerPressed);
            try
            {
                e.Pointer.Capture(card);
            }
            catch
            {
                pointerPressed = false;
                ClearTriggerDragState();
                UpdateTriggerCardVisual(card, trigger, p, pointerOver, pointerPressed);
                throw;
            }

            e.Handled = true;
        };
        card.PointerMoved += (_, e) =>
        {
            StackPanel? triggerPanel = _draggedTriggerPanel;
            if (_draggedTrigger == null || triggerPanel == null) return;
            if (!ReferenceEquals(_draggedTriggerRow, card)) return;

            Point current = e.GetPosition(triggerPanel);
            if (Math.Abs(current.Y - _triggerDragStart.Y) < 4) return;
            double draggedMidpoint = current.Y - _draggedTriggerPointerOffsetY + _draggedTriggerHeight / 2.0;
            _draggedTriggerTargetIndex = TriggerInsertionIndexFromMidpoint(draggedMidpoint);
            ApplyTriggerDragPreview();
            card.RenderTransform = new TranslateTransform(x: 0, current.Y - _triggerDragStart.Y);
            e.Handled = true;
        };
        card.PointerReleased += (_, e) =>
        {
            pointerPressed = false;
            EndTriggerDrag(e.Pointer);
        };
        card.PointerCaptureLost += (_, _) =>
        {
            pointerPressed = false;
            EndTriggerDrag(null);
        };

        TrayAppDotNETToolTip.SetTip(
            card,
            L(nameof(AppStrings.Settings_Triggers_Card_ToolTip)));
        Border registeredCard = TrayAppDotNETSettingsCards.RegisterSearchCard(card);
        ControlNames.AssignLogicalSubtree(registeredCard, parentName: "TriggerCard");
        return registeredCard;
    }

    /// <summary>Moves a trigger one place up or down; the map runs this for Ctrl+Up and Ctrl+Down on its card.</summary>
    private void MoveTrigger(BatteryTriggerEntry trigger, int offset)
    {
        int currentIndex = _settings.Triggers.IndexOf(trigger);
        int nextIndex = currentIndex + offset;
        if (currentIndex < 0 || nextIndex < 0 || nextIndex >= _settings.Triggers.Count) return;

        _settings.Triggers.RemoveAt(currentIndex);
        _settings.Triggers.Insert(nextIndex, trigger);
        Save();
        RenderTriggerCards();
    }

    private SettingsComboBox BuildNullableTriggerCombo<TEnum>(
        string placeholder,
        IReadOnlyList<(TEnum Value, string Text)> options,
        TEnum? selected,
        Action<TEnum?> set,
        SettingsPalette p)
        where TEnum : struct, Enum
    {
        SettingsComboBox combo = TrayAppDotNETSettingsUI.ComboBox(p, width: 153);
        combo.Width = double.NaN;
        combo.MinWidth = 153;
        combo.Items.Add(PlaceholderComboItem(placeholder, p));
        foreach ((TEnum value, string text) in options)
            combo.Items.Add(new SettingsComboBoxItem(value.ToString(), text, p));

        if (selected.HasValue)
            TrayAppDotNETSettingsUI.SelectComboByTag(combo, selected.Value.ToString());
        else
            combo.SelectedIndex = 0;

        combo.SelectionChanged += (_, _) =>
        {
            string? tag = combo.SelectedItem?.Tag?.ToString();
            if (string.IsNullOrEmpty(tag))
            {
                set(null);
                Save();
                return;
            }

            if (!Enum.TryParse(tag, out TEnum value)) return;
            set(value);
            Save();
        };
        return combo;
    }

    private static SettingsComboBoxItem PlaceholderComboItem(string text, SettingsPalette p) =>
        new(string.Empty, text, p, () =>
        {
            TextBlock label = TrayAppDotNETSettingsUI.Text(text, p);
            label.Foreground = TrayAppDotNETSettingsUI.Brush(p.DisabledForeground);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.TextWrapping = TextWrapping.NoWrap;
            return label;
        });

    private static IReadOnlyList<(BatteryTriggerCondition Value, string Text)> TriggerConditionOptions() =>
    [
        (BatteryTriggerCondition.BatteryBelow20, L(nameof(AppStrings.Settings_Triggers_Condition_BatteryBelow20))),
        (BatteryTriggerCondition.BatteryBelow10, L(nameof(AppStrings.Settings_Triggers_Condition_BatteryBelow10))),
        (BatteryTriggerCondition.BatteryAbove80, L(nameof(AppStrings.Settings_Triggers_Condition_BatteryAbove80))),
        (BatteryTriggerCondition.ChargingStarted, L(nameof(AppStrings.Settings_Triggers_Condition_ChargingStarted))),
        (BatteryTriggerCondition.ChargingStopped, L(nameof(AppStrings.Settings_Triggers_Condition_ChargingStopped))),
        (BatteryTriggerCondition.ExternalPowerConnected,
            L(nameof(AppStrings.Settings_Triggers_Condition_ExternalPowerConnected))),
        (BatteryTriggerCondition.ExternalPowerDisconnected,
            L(nameof(AppStrings.Settings_Triggers_Condition_ExternalPowerDisconnected))),
        (BatteryTriggerCondition.FullyCharged, L(nameof(AppStrings.Settings_Triggers_Condition_FullyCharged)))
    ];

    private static IReadOnlyList<(BatteryTriggerAction Value, string Text)> TriggerActionOptions() =>
    [
        (BatteryTriggerAction.ShowNotification, L(nameof(AppStrings.Settings_Triggers_Action_ShowNotification))),
        (BatteryTriggerAction.OpenFlyout, L(nameof(AppStrings.Settings_Triggers_Action_OpenFlyout))),
        (BatteryTriggerAction.OpenSettings, L(nameof(AppStrings.Settings_Triggers_Action_OpenSettings))),
        (BatteryTriggerAction.OpenPowerSettings, L(nameof(AppStrings.Settings_Triggers_Action_OpenPowerSettings)))
    ];

    private void UpdateTriggerCardVisual(
        Border card,
        BatteryTriggerEntry trigger,
        SettingsPalette p,
        bool pointerOver,
        bool pointerPressed)
    {
        bool dragging = ReferenceEquals(trigger, _draggedTrigger)
                        && ReferenceEquals(_triggerPanel, _draggedTriggerPanel);
        Color background = pointerPressed
            ? p.Pressed
            : pointerOver
                ? p.Hover
                : p.CardBackground;
        card.Background = TrayAppDotNETSettingsUI.Brush(background);
        card.BorderBrush = TrayAppDotNETSettingsUI.Brush(dragging ? p.Accent : Colors.Transparent);
        card.BorderThickness = dragging ? new Thickness(1) : new Thickness(0);
        card.Opacity = dragging ? 0.82 : 1.0;
        card.SetValue(ZIndexProperty, dragging ? 1 : 0);
    }

    private int TriggerInsertionIndexFromMidpoint(double draggedMidpointY)
    {
        StackPanel? triggerPanel = _draggedTriggerPanel;
        if (triggerPanel == null) return -1;

        int insertion = 0;
        for (int i = 0; i < triggerPanel.Children.Count; i++)
        {
            Control child = triggerPanel.Children[i];
            if (ReferenceEquals(child, _draggedTriggerRow)) continue;
            Point? topLeft = child.TranslatePoint(new Point(x: 0, y: 0), triggerPanel);
            if (topLeft == null) continue;
            if (draggedMidpointY > topLeft.Value.Y + child.Bounds.Height / 2.0) insertion++;
            else break;
        }

        int max = _settings.Triggers.Count - (_draggedTrigger != null ? 1 : 0);
        return Math.Clamp(insertion, min: 0, Math.Max(val1: 0, max));
    }

    private void ApplyTriggerDragPreview()
    {
        StackPanel? triggerPanel = _draggedTriggerPanel;
        if (triggerPanel == null || _draggedTrigger == null || _draggedTriggerRow == null) return;
        ResetTriggerDragPreview();

        int sourceIndex = _settings.Triggers.IndexOf(_draggedTrigger);
        if (sourceIndex < 0) return;

        int targetIndex = Math.Clamp(_draggedTriggerTargetIndex, min: 0,
            Math.Max(val1: 0, _settings.Triggers.Count - 1));
        double offset = Math.Max(val1: 1, _draggedTriggerHeight + Math.Max(val1: 0, _draggedTriggerRow.Margin.Bottom));
        if (targetIndex < sourceIndex)
        {
            for (int i = targetIndex; i < sourceIndex; i++)
                SetTriggerPreviewOffset(i, offset);
        }
        else if (targetIndex > sourceIndex)
        {
            for (int i = sourceIndex + 1; i <= targetIndex && i < triggerPanel.Children.Count; i++)
                SetTriggerPreviewOffset(i, -offset);
        }
    }

    private void SetTriggerPreviewOffset(int index, double offset)
    {
        StackPanel? triggerPanel = _draggedTriggerPanel;
        if (triggerPanel == null) return;
        if (index < 0 || index >= triggerPanel.Children.Count) return;
        if (ReferenceEquals(triggerPanel.Children[index], _draggedTriggerRow)) return;
        triggerPanel.Children[index].RenderTransform = new TranslateTransform(x: 0, offset);
    }

    private void ResetTriggerDragPreview()
    {
        StackPanel? triggerPanel = _draggedTriggerPanel;
        if (triggerPanel == null) return;
        foreach (Control child in triggerPanel.Children)
        {
            if (ReferenceEquals(child, _draggedTriggerRow)) continue;
            child.RenderTransform = null;
        }
    }

    private void EndTriggerDrag(IPointer? pointer)
    {
        StackPanel? draggedTriggerPanel = _draggedTriggerPanel;
        BatteryTriggerEntry? dragged = _draggedTrigger;
        int targetIndex = _draggedTriggerTargetIndex;
        bool hadDrag = dragged != null;
        bool reordered = false;
        _draggedTriggerRow?.RenderTransform = null;
        if (draggedTriggerPanel != null)
        {
            foreach (Control child in draggedTriggerPanel.Children)
                child.RenderTransform = null;
        }

        ClearTriggerDragState();
        ReleaseTriggerPointerCapture(pointer);

        if (dragged != null && targetIndex >= 0)
        {
            int currentIndex = _settings.Triggers.IndexOf(dragged);
            if (currentIndex >= 0 && targetIndex != currentIndex)
            {
                _settings.Triggers.RemoveAt(currentIndex);
                _settings.Triggers.Insert(Math.Clamp(targetIndex, min: 0, _settings.Triggers.Count), dragged);
                reordered = true;
            }
        }

        if (reordered) Save();
        if (hadDrag && ReferenceEquals(_triggerPanel, draggedTriggerPanel)) RenderTriggerCards();
    }

    private void CleanupTriggerPage(StackPanel triggerPanel)
    {
        IPointer? capturedPointer = null;
        if (ReferenceEquals(_draggedTriggerPanel, triggerPanel))
        {
            foreach (Control child in triggerPanel.Children)
                child.RenderTransform = null;

            capturedPointer = _triggerCapturedPointer;
            ClearTriggerDragState();
        }

        _triggerPagePanels.Remove(triggerPanel);
        if (ReferenceEquals(_triggerPanel, triggerPanel))
        {
            _triggerPanel = _triggerPagePanels.Count > 0
                ? _triggerPagePanels[^1]
                : null;
        }

        ReleaseTriggerPointerCapture(capturedPointer);
    }

    private void ClearTriggerDragState()
    {
        _draggedTriggerPanel = null;
        _draggedTriggerRow = null;
        _draggedTrigger = null;
        _triggerCapturedPointer = null;
        _triggerDragStart = default;
        _draggedTriggerTargetIndex = -1;
        _draggedTriggerPointerOffsetY = 0;
        _draggedTriggerHeight = 0;
    }

    private static void ReleaseTriggerPointerCapture(IPointer? pointer)
    {
        if (pointer == null) return;

        try
        {
            pointer.Capture(null);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"BatterySettingsWindow trigger pointer release failed: {exception.Message}");
        }
    }

    private static bool IsTriggerCardInteractiveSource(object? source, Border card)
    {
        for (Control? control = source as Control; control != null; control = control.GetVisualParent<Control>())
        {
            if (ReferenceEquals(control, card)) return false;
            if (control is SettingsComboBox or TextBox or Avalonia.Controls.Button or ScrollViewer)
                return true;
            if (control.Cursor != null)
                return true;
        }

        return false;
    }
}
