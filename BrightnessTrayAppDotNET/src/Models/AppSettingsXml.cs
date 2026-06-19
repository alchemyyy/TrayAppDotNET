using System.Xml.Linq;
using static TrayAppDotNETCommon.Models.TrayAppDotNETSettingsXml;

namespace BrightnessTrayAppDotNET.Models;

public partial class AppSettings
{
    private void SaveXml(Stream stream)
    {
        XDocument document = new(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("AppSettings",
                Bool(nameof(RunOnStartup), RunOnStartup),
                Bool(nameof(ApplyBrightnessOnStartup), ApplyBrightnessOnStartup),
                Bool(nameof(Autosave), Autosave),
                Bool(nameof(TrayScrollEnabled), TrayScrollEnabled),
                Enum(nameof(TrayWheelAction), TrayWheelAction),
                Enum(nameof(TrayCtrlWheelAction), TrayCtrlWheelAction),
                Enum(nameof(TrayAltWheelAction), TrayAltWheelAction),
                Bool(nameof(FlyoutNumberKeysSwitchProfile), FlyoutNumberKeysSwitchProfile),
                Bool(nameof(PreserveMasterSliderOffsets), PreserveMasterSliderOffsets),
                Enum(nameof(TrayDoubleClickAction), TrayDoubleClickAction),
                Enum(nameof(TrayCtrlLeftClickAction), TrayCtrlLeftClickAction),
                Enum(nameof(TrayAltLeftClickAction), TrayAltLeftClickAction),
                Enum(nameof(TrayCtrlRightClickAction), TrayCtrlRightClickAction),
                Enum(nameof(TrayAltRightClickAction), TrayAltRightClickAction),
                Enum(nameof(TrayCtrlDoubleLeftClickAction), TrayCtrlDoubleLeftClickAction),
                Enum(nameof(TrayAltDoubleLeftClickAction), TrayAltDoubleLeftClickAction),
                Bool(nameof(ShowProfileSelectorsInMenu), ShowProfileSelectorsInMenu),
                Bool(nameof(ShowMonitorPowerButtons), ShowMonitorPowerButtons),
                Bool(nameof(ShowAllDisplaysPowerButton), ShowAllDisplaysPowerButton),
                Enum(nameof(PowerOffMode), PowerOffMode),
                Enum(nameof(ContextMenuPosition), ContextMenuPosition),
                Int(nameof(BrightnessUpdateRateMs), BrightnessUpdateRateMs),
                Int(nameof(ValidationDwellMs), ValidationDwellMs),
                Int(nameof(ValidationAttempts), ValidationAttempts),
                Int(nameof(DDCOperationTimeoutMs), DDCOperationTimeoutMs),
                Enum(nameof(MasterSliderMode), MasterSliderMode),
                Bool(nameof(ShowFlyoutMonitorPowerButtons), ShowFlyoutMonitorPowerButtons),
                Bool(nameof(ShowFlyoutMonitorNumberBadge), ShowFlyoutMonitorNumberBadge),
                Bool(nameof(ShowFlyoutDisplaySettingsButton), ShowFlyoutDisplaySettingsButton),
                Bool(nameof(ShowFlyoutFooterPowerButton), ShowFlyoutFooterPowerButton),
                Bool(nameof(FooterPowerButtonOnlyEnabledMonitors), FooterPowerButtonOnlyEnabledMonitors),
                Bool(nameof(ShowMasterSlider), ShowMasterSlider),
                Bool(nameof(ShowIndividualSliders), ShowIndividualSliders),
                Bool(nameof(ShowNightLightSlider), ShowNightLightSlider),
                Int(nameof(FlyoutScrollWheelStep), FlyoutScrollWheelStep),
                Bool(nameof(AllowFlyoutUndock), AllowFlyoutUndock),
                Bool(nameof(RestoreFlyoutUndockedOnStartup), RestoreFlyoutUndockedOnStartup),
                Bool(nameof(FlyoutUndocked), FlyoutUndocked),
                Bool(nameof(HasAcknowledgedHardPowerOffWarning), HasAcknowledgedHardPowerOffWarning),
                Bool(nameof(FlyoutHasSavedPosition), FlyoutHasSavedPosition),
                Double(nameof(FlyoutLeft), FlyoutLeft),
                Double(nameof(FlyoutTop), FlyoutTop),
                Bool(nameof(ShowEnvironmentalCurvesButton), ShowEnvironmentalCurvesButton),
                Bool(nameof(ShowNightLightKelvinLabel), ShowNightLightKelvinLabel),
                Bool(nameof(InvertNightLightSlider), InvertNightLightSlider),
                Enum(nameof(NightLightFallbackMode), NightLightFallbackMode),
                Int(nameof(NightLightLastNonZeroStrength), NightLightLastNonZeroStrength),
                Int(nameof(MaxRecoveryAttempts), MaxRecoveryAttempts),
                Int(nameof(LastMasterBrightness), LastMasterBrightness),
                Bool(nameof(TurnOffNightLightAtZeroStrength), TurnOffNightLightAtZeroStrength),
                Int(nameof(NightLightPDBDownloadTimeoutSeconds), NightLightPDBDownloadTimeoutSeconds),
                Bool(nameof(CheckForUpdatesEnabled), CheckForUpdatesEnabled),
                Bool(nameof(ShowUpdateNotificationsEnabled), ShowUpdateNotificationsEnabled),
                Bool(nameof(ShowUpdateButtonInFlyout), ShowUpdateButtonInFlyout),
                Int(nameof(UpdateCheckIntervalMs), UpdateCheckIntervalMs),
                Bool(nameof(KeepFlyoutWarm), KeepFlyoutWarm),
                Bool(nameof(KeepTrayContextMenuWarm), KeepTrayContextMenuWarm),
                Enum(nameof(DefaultDisplaySortMode), DefaultDisplaySortMode),
                Enum(nameof(DefaultDisplaySortDirection), DefaultDisplaySortDirection),
                Enum(nameof(MonitorIdentityStrategy), MonitorIdentityStrategy),
                StringListElement(nameof(MonitorOrder), "ID", MonitorOrder),
                MonitorOverridesElement(),
                KnownDisplaysElement(),
                HotkeysElement<BrightnessHotkeyAction>(Hotkeys),
                Int(nameof(ContextMenuFontSize), ContextMenuFontSize),
                Enum(nameof(ThemeMode), ThemeMode),
                NullableThemeColorElement(nameof(TextColor), TextColor),
                NullableThemeColorElement(nameof(BackgroundColor), BackgroundColor),
                Enum(nameof(TrayIconStyle), TrayIconStyle),
                Enum(nameof(DynamicIconBrightnessTracking), DynamicIconBrightnessTracking),
                Bool(nameof(DynamicIconTrackEnabledOnly), DynamicIconTrackEnabledOnly),
                NullableThemeColorElement(nameof(TrayIconColor), TrayIconColor),
                NullableThemeColorElement(nameof(TrayIconBrightColor), TrayIconBrightColor),
                NullableThemeColorElement(nameof(TrayIconDimColor), TrayIconDimColor),
                NullableThemeColorElement(nameof(FooterBackgroundColor), FooterBackgroundColor),
                Bool(nameof(EnableRoundedCorners), EnableRoundedCorners),
                NullableThemeColorElement(nameof(EnvironmentalBrightnessCurveColor), EnvironmentalBrightnessCurveColor),
                NullableThemeColorElement(nameof(EnvironmentalNightLightCurveColor), EnvironmentalNightLightCurveColor),
                NullableThemeColorElement(nameof(EnvironmentalCurrentTimeColor), EnvironmentalCurrentTimeColor),
                NullableThemeColorElement(nameof(EnvironmentalTwilightBackdropColor),
                    EnvironmentalTwilightBackdropColor),
                NullableThemeColorElement(nameof(EnvironmentalNightBackdropColor), EnvironmentalNightBackdropColor),
                NullableThemeColorElement(nameof(EnvironmentalGridLineColor), EnvironmentalGridLineColor),
                Double(nameof(EnvironmentalLatitude), EnvironmentalLatitude),
                Double(nameof(EnvironmentalLongitude), EnvironmentalLongitude),
                Bool(nameof(EnvironmentalShowBrightnessCurve), EnvironmentalShowBrightnessCurve),
                Bool(nameof(EnvironmentalShowNightLightCurve), EnvironmentalShowNightLightCurve),
                Bool(nameof(EnvironmentalBrightnessCurveEnabled), EnvironmentalBrightnessCurveEnabled),
                Bool(nameof(EnvironmentalNightLightCurveEnabled), EnvironmentalNightLightCurveEnabled),
                CurveStopwatchesElement(),
                Bool(nameof(EnvironmentalOffsetMode), EnvironmentalOffsetMode),
                Bool(nameof(EnvironmentalShowCursorReadout), EnvironmentalShowCursorReadout),
                Bool(nameof(EnvironmentalShowSunOverlay), EnvironmentalShowSunOverlay),
                Int(nameof(EnvironmentalCurveSmoothness), EnvironmentalCurveSmoothness),
                Int(nameof(EnvironmentalCurveTickIntervalMs), EnvironmentalCurveTickIntervalMs),
                SliderThumbElement(SerializedSliderThumb)));

        SaveDocument(stream, document);
    }

    private static AppSettings LoadXml(Stream stream)
    {
        XElement root = LoadRoot(
            stream,
            "AppSettings",
            "Missing AppSettings root.",
            "Unexpected AppSettings root.");

        AppSettings settings = new()
        {
            RunOnStartup = ReadBool(root, nameof(RunOnStartup), true),
            ApplyBrightnessOnStartup = ReadBool(root, nameof(ApplyBrightnessOnStartup), true),
            Autosave = ReadBool(root, nameof(Autosave), true),
            TrayScrollEnabled = ReadBool(root, nameof(TrayScrollEnabled), true),
            TrayWheelAction = ReadEnum(root, nameof(TrayWheelAction), TrayWheelTarget.Brightness),
            TrayCtrlWheelAction = ReadEnum(root, nameof(TrayCtrlWheelAction), TrayWheelTarget.NightLight),
            TrayAltWheelAction = ReadEnum(root, nameof(TrayAltWheelAction), TrayWheelTarget.Nothing),
            FlyoutNumberKeysSwitchProfile = ReadBool(root, nameof(FlyoutNumberKeysSwitchProfile), true),
            PreserveMasterSliderOffsets = ReadBool(root, nameof(PreserveMasterSliderOffsets), false),
            TrayDoubleClickAction = ReadEnum(root, nameof(TrayDoubleClickAction), TrayClickAction.Nothing),
            TrayCtrlLeftClickAction = ReadEnum(root, nameof(TrayCtrlLeftClickAction), TrayClickAction.Nothing),
            TrayAltLeftClickAction = ReadEnum(root, nameof(TrayAltLeftClickAction), TrayClickAction.Nothing),
            TrayCtrlRightClickAction = ReadEnum(root, nameof(TrayCtrlRightClickAction), TrayClickAction.Nothing),
            TrayAltRightClickAction = ReadEnum(root, nameof(TrayAltRightClickAction), TrayClickAction.Nothing),
            TrayCtrlDoubleLeftClickAction =
                ReadEnum(root, nameof(TrayCtrlDoubleLeftClickAction), TrayClickAction.Nothing),
            TrayAltDoubleLeftClickAction =
                ReadEnum(root, nameof(TrayAltDoubleLeftClickAction), TrayClickAction.Nothing),
            ShowProfileSelectorsInMenu = ReadBool(root, nameof(ShowProfileSelectorsInMenu), true),
            ShowMonitorPowerButtons = ReadBool(root, nameof(ShowMonitorPowerButtons), false),
            ShowAllDisplaysPowerButton = ReadBool(root, nameof(ShowAllDisplaysPowerButton), true),
            PowerOffMode = ReadEnum(root, nameof(PowerOffMode), PowerOffMode.Sleep),
            ContextMenuPosition = ReadEnum(root, nameof(ContextMenuPosition), ContextMenuPosition.Modern),
            BrightnessUpdateRateMs =
                ReadInt(root, nameof(BrightnessUpdateRateMs), TimeConstants.BrightnessUpdateRateDefaultMs),
            ValidationDwellMs = ReadInt(root, nameof(ValidationDwellMs), TimeConstants.ValidationDwellDefaultMs),
            ValidationAttempts = ReadInt(root, nameof(ValidationAttempts), 4),
            DDCOperationTimeoutMs =
                ReadInt(root, nameof(DDCOperationTimeoutMs), TimeConstants.DDCOperationTimeoutDefaultMs),
            MasterSliderMode = ReadEnum(root, nameof(MasterSliderMode), MasterSliderMode.Average),
            ShowFlyoutMonitorPowerButtons = ReadBool(root, nameof(ShowFlyoutMonitorPowerButtons), false),
            ShowFlyoutMonitorNumberBadge = ReadBool(root, nameof(ShowFlyoutMonitorNumberBadge), false),
            ShowFlyoutDisplaySettingsButton = ReadBool(root, nameof(ShowFlyoutDisplaySettingsButton), true),
            ShowFlyoutFooterPowerButton = ReadBool(root, nameof(ShowFlyoutFooterPowerButton), false),
            FooterPowerButtonOnlyEnabledMonitors = ReadBool(root, nameof(FooterPowerButtonOnlyEnabledMonitors), false),
            ShowMasterSlider = ReadBool(root, nameof(ShowMasterSlider), true),
            ShowIndividualSliders = ReadBool(root, nameof(ShowIndividualSliders), true),
            ShowNightLightSlider = ReadBool(root, nameof(ShowNightLightSlider), true),
            FlyoutScrollWheelStep = ReadInt(root, nameof(FlyoutScrollWheelStep), 2),
            AllowFlyoutUndock = ReadBool(root, nameof(AllowFlyoutUndock), true),
            RestoreFlyoutUndockedOnStartup = ReadBool(root, nameof(RestoreFlyoutUndockedOnStartup), true),
            FlyoutUndocked = ReadBool(root, nameof(FlyoutUndocked), false),
            HasAcknowledgedHardPowerOffWarning = ReadBool(root, nameof(HasAcknowledgedHardPowerOffWarning), false),
            FlyoutHasSavedPosition = ReadBool(root, nameof(FlyoutHasSavedPosition), false),
            FlyoutLeft = ReadDouble(root, nameof(FlyoutLeft), 0),
            FlyoutTop = ReadDouble(root, nameof(FlyoutTop), 0),
            ShowEnvironmentalCurvesButton = ReadBool(root, nameof(ShowEnvironmentalCurvesButton), true),
            ShowNightLightKelvinLabel = ReadBool(root, nameof(ShowNightLightKelvinLabel), false),
            InvertNightLightSlider = ReadBool(root, nameof(InvertNightLightSlider), false),
            NightLightFallbackMode =
                ReadEnum(root, nameof(NightLightFallbackMode), NightLightFallbackMode.SettingsHandler),
            NightLightLastNonZeroStrength = ReadInt(root, nameof(NightLightLastNonZeroStrength), 50),
            MaxRecoveryAttempts = ReadInt(root, nameof(MaxRecoveryAttempts), 60),
            LastMasterBrightness = ReadInt(root, nameof(LastMasterBrightness), 100),
            TurnOffNightLightAtZeroStrength = ReadBool(root, nameof(TurnOffNightLightAtZeroStrength), false),
            NightLightPDBDownloadTimeoutSeconds = ReadInt(root, nameof(NightLightPDBDownloadTimeoutSeconds), 60),
            CheckForUpdatesEnabled = ReadBool(root, nameof(CheckForUpdatesEnabled), true),
            ShowUpdateNotificationsEnabled = ReadBool(root, nameof(ShowUpdateNotificationsEnabled), false),
            ShowUpdateButtonInFlyout = ReadBool(root, nameof(ShowUpdateButtonInFlyout), true),
            UpdateCheckIntervalMs =
                ReadInt(root, nameof(UpdateCheckIntervalMs), TimeConstants.UpdateCheckIntervalDefaultMs),
            KeepFlyoutWarm = ReadBool(root, nameof(KeepFlyoutWarm), true),
            KeepTrayContextMenuWarm = ReadBool(root, nameof(KeepTrayContextMenuWarm), true),
            DefaultDisplaySortMode = ReadEnum(root, nameof(DefaultDisplaySortMode), DisplaySortMode.Arrangement),
            DefaultDisplaySortDirection =
                ReadEnum(root, nameof(DefaultDisplaySortDirection), DisplaySortDirection.Standard),
            MonitorIdentityStrategy =
                ReadEnum(root, nameof(MonitorIdentityStrategy), MonitorIdentityStrategy.DisplayNumber),
            MonitorOrder = ReadStringList(root.Element(nameof(MonitorOrder)), "ID"),
            MonitorOverrides = ReadMonitorOverrides(root.Element(nameof(MonitorOverrides))),
            KnownDisplays = ReadKnownDisplays(root.Element(nameof(KnownDisplays))),
            Hotkeys = ReadHotkeys<BrightnessHotkeyAction, HotkeyBinding>(
                root.Element(nameof(Hotkeys)),
                BrightnessHotkeyAction.OpenSettings,
                static (action, parameter, modifiers, virtualKey, enabled, bindingID, removedByUser) =>
                    new HotkeyBinding
                    {
                        Action = action,
                        Parameter = parameter,
                        Modifiers = modifiers,
                        VirtualKey = virtualKey,
                        Enabled = enabled,
                        BindingID = bindingID,
                        RemovedByUser = removedByUser,
                    }),
            ContextMenuFontSize = ReadInt(root, nameof(ContextMenuFontSize), 15),
            ThemeMode = ReadEnum(root, nameof(ThemeMode), ThemeMode.System),
            TextColor = ReadNullableThemeColor(root, nameof(TextColor)),
            BackgroundColor = ReadNullableThemeColor(root, nameof(BackgroundColor)),
            TrayIconStyle = ReadEnum(root, nameof(TrayIconStyle), TrayIconStyle.Dynamic),
            DynamicIconBrightnessTracking =
                ReadEnum(root, nameof(DynamicIconBrightnessTracking), MasterSliderMode.Average),
            DynamicIconTrackEnabledOnly = ReadBool(root, nameof(DynamicIconTrackEnabledOnly), false),
            TrayIconColor = ReadNullableThemeColor(root, nameof(TrayIconColor)),
            TrayIconBrightColor = ReadNullableThemeColor(root, nameof(TrayIconBrightColor)),
            TrayIconDimColor = ReadNullableThemeColor(root, nameof(TrayIconDimColor)),
            FooterBackgroundColor = ReadNullableThemeColor(root, nameof(FooterBackgroundColor)),
            EnableRoundedCorners = ReadBool(root, nameof(EnableRoundedCorners), true),
            EnvironmentalBrightnessCurveColor = ReadNullableThemeColor(root, nameof(EnvironmentalBrightnessCurveColor)),
            EnvironmentalNightLightCurveColor = ReadNullableThemeColor(root, nameof(EnvironmentalNightLightCurveColor)),
            EnvironmentalCurrentTimeColor = ReadNullableThemeColor(root, nameof(EnvironmentalCurrentTimeColor)),
            EnvironmentalTwilightBackdropColor =
                ReadNullableThemeColor(root, nameof(EnvironmentalTwilightBackdropColor)),
            EnvironmentalNightBackdropColor = ReadNullableThemeColor(root, nameof(EnvironmentalNightBackdropColor)),
            EnvironmentalGridLineColor = ReadNullableThemeColor(root, nameof(EnvironmentalGridLineColor)),
            EnvironmentalLatitude = ReadDouble(root, nameof(EnvironmentalLatitude), 47.7542814),
            EnvironmentalLongitude = ReadDouble(root, nameof(EnvironmentalLongitude), -122.2795275),
            EnvironmentalShowBrightnessCurve = ReadBool(root, nameof(EnvironmentalShowBrightnessCurve), true),
            EnvironmentalShowNightLightCurve = ReadBool(root, nameof(EnvironmentalShowNightLightCurve), true),
            EnvironmentalBrightnessCurveEnabled = ReadBool(root, nameof(EnvironmentalBrightnessCurveEnabled), false),
            EnvironmentalNightLightCurveEnabled = ReadBool(root, nameof(EnvironmentalNightLightCurveEnabled), false),
            CurveStopwatches = ReadCurveStopwatches(root.Element(nameof(CurveStopwatches))),
            EnvironmentalOffsetMode = ReadBool(root, nameof(EnvironmentalOffsetMode), false),
            EnvironmentalShowCursorReadout = ReadBool(root, nameof(EnvironmentalShowCursorReadout), false),
            EnvironmentalShowSunOverlay = ReadBool(root, nameof(EnvironmentalShowSunOverlay), true),
            EnvironmentalCurveSmoothness = ReadInt(root, nameof(EnvironmentalCurveSmoothness), 100),
            EnvironmentalCurveTickIntervalMs =
                ReadInt(root, nameof(EnvironmentalCurveTickIntervalMs),
                    TimeConstants.EnvironmentalCurveTickIntervalDefaultMs),
            SerializedSliderThumb = ReadSliderThumb(root.Element("SliderThumb"), defaultName: "Capsule"),
        };

        settings.WireColorCallbacks();
        settings.InitializeSliderThumbCatalog();
        return settings;
    }

    private XElement MonitorOverridesElement()
    {
        XElement element = new(nameof(MonitorOverrides));
        foreach (MonitorOverrideEntry monitor in MonitorOverrides)
        {
            XElement item = new("Monitor",
                Attribute(nameof(MonitorOverrideEntry.ID), monitor.ID),
                Attribute(nameof(MonitorOverrideEntry.Name), monitor.Name),
                Attribute(nameof(MonitorOverrideEntry.ValidationDwellMs), monitor.ValidationDwellMs),
                Attribute(nameof(MonitorOverrideEntry.BrightnessDwellMs), monitor.BrightnessDwellMs),
                Attribute(nameof(MonitorOverrideEntry.MinBrightness), monitor.MinBrightness),
                Attribute(nameof(MonitorOverrideEntry.MaxBrightness), monitor.MaxBrightness),
                Attribute(nameof(MonitorOverrideEntry.PowerOffVcpOverride), monitor.PowerOffVcpOverride),
                Attribute(nameof(MonitorOverrideEntry.BrightnessVcpOverride), monitor.BrightnessVcpOverride));

            if (monitor.NormCurvePoints.Count > 0)
                item.Add(PointsElement("NormCurve", monitor.NormCurvePoints));
            element.Add(item);
        }

        return element;
    }

    private XElement KnownDisplaysElement()
    {
        XElement element = new(nameof(KnownDisplays));
        foreach (KnownDisplayEntry display in KnownDisplays)
        {
            XElement item = new("Display",
                Attribute(nameof(KnownDisplayEntry.EDIDKey), display.EDIDKey),
                Attribute(nameof(KnownDisplayEntry.OriginalName), display.OriginalName),
                Attribute(nameof(KnownDisplayEntry.EDIDSerial), display.EDIDSerial),
                Attribute(nameof(KnownDisplayEntry.WasEverDDCCapable), display.WasEverDDCCapable));
            if (display.LastBusBrightness.HasValue)
                item.Add(Attribute(nameof(KnownDisplayEntry.LastBusBrightness), display.LastBusBrightness.Value));
            element.Add(item);
        }

        return element;
    }

    private XElement CurveStopwatchesElement()
    {
        XElement element = new(nameof(CurveStopwatches));
        foreach (CurveStopwatchEntry stopwatch in CurveStopwatches)
        {
            element.Add(new XElement("Stopwatch",
                Attribute(nameof(CurveStopwatchEntry.SliderKey), stopwatch.SliderKey),
                Attribute(nameof(CurveStopwatchEntry.Minutes), stopwatch.Minutes),
                Attribute(nameof(CurveStopwatchEntry.IsEnabled), stopwatch.IsEnabled),
                Attribute(nameof(CurveStopwatchEntry.EngagedAtUtc), stopwatch.EngagedAtUtc),
                Attribute(nameof(CurveStopwatchEntry.ReenableAtUtc), stopwatch.ReenableAtUtc)));
        }

        return element;
    }

    private static XElement PointsElement(string name, IEnumerable<NormCurvePoint> points) =>
        new(name, points.Select(point => new XElement("P",
            Attribute(nameof(NormCurvePoint.X), point.X),
            Attribute(nameof(NormCurvePoint.Y), point.Y))));

    private static List<MonitorOverrideEntry> ReadMonitorOverrides(XElement? element)
    {
        List<MonitorOverrideEntry> monitors = [];
        if (element == null) return monitors;

        foreach (XElement item in element.Elements("Monitor"))
        {
            monitors.Add(new MonitorOverrideEntry
            {
                ID = ReadAttribute(item, nameof(MonitorOverrideEntry.ID), string.Empty),
                Name = ReadAttribute(item, nameof(MonitorOverrideEntry.Name), string.Empty),
                ValidationDwellMs = ReadIntAttribute(item, nameof(MonitorOverrideEntry.ValidationDwellMs), -1),
                BrightnessDwellMs = ReadIntAttribute(item, nameof(MonitorOverrideEntry.BrightnessDwellMs), -1),
                MinBrightness = ReadIntAttribute(item, nameof(MonitorOverrideEntry.MinBrightness), 0),
                MaxBrightness = ReadIntAttribute(item, nameof(MonitorOverrideEntry.MaxBrightness), 100),
                PowerOffVcpOverride =
                    ReadAttribute(item, nameof(MonitorOverrideEntry.PowerOffVcpOverride), string.Empty),
                BrightnessVcpOverride =
                    ReadAttribute(item, nameof(MonitorOverrideEntry.BrightnessVcpOverride), string.Empty),
                NormCurvePoints = ReadNormCurvePoints(item.Element("NormCurve")),
            });
        }

        return monitors;
    }

    private static List<NormCurvePoint> ReadNormCurvePoints(XElement? element)
    {
        List<NormCurvePoint> points = [];
        if (element == null) return points;
        foreach (XElement point in element.Elements("P"))
        {
            points.Add(new NormCurvePoint
            {
                X = ReadDoubleAttribute(point, nameof(NormCurvePoint.X), 0),
                Y = ReadDoubleAttribute(point, nameof(NormCurvePoint.Y), 0),
            });
        }

        return points;
    }

    private static List<KnownDisplayEntry> ReadKnownDisplays(XElement? element)
    {
        List<KnownDisplayEntry> displays = [];
        if (element == null) return displays;
        foreach (XElement item in element.Elements("Display"))
        {
            displays.Add(new KnownDisplayEntry
            {
                EDIDKey = ReadAttribute(item, nameof(KnownDisplayEntry.EDIDKey), string.Empty),
                OriginalName = ReadAttribute(item, nameof(KnownDisplayEntry.OriginalName), string.Empty),
                EDIDSerial = ReadAttribute(item, nameof(KnownDisplayEntry.EDIDSerial), string.Empty),
                WasEverDDCCapable = ReadBoolAttribute(item, nameof(KnownDisplayEntry.WasEverDDCCapable), false),
                LastBusBrightness = TryReadIntAttribute(item, nameof(KnownDisplayEntry.LastBusBrightness)),
            });
        }

        return displays;
    }

    private static List<CurveStopwatchEntry> ReadCurveStopwatches(XElement? element)
    {
        List<CurveStopwatchEntry> stopwatches = [];
        if (element == null) return stopwatches;
        foreach (XElement item in element.Elements("Stopwatch"))
        {
            stopwatches.Add(new CurveStopwatchEntry
            {
                SliderKey = ReadAttribute(item, nameof(CurveStopwatchEntry.SliderKey), string.Empty),
                Minutes = ReadIntAttribute(item, nameof(CurveStopwatchEntry.Minutes), 60),
                IsEnabled = ReadBoolAttribute(item, nameof(CurveStopwatchEntry.IsEnabled), false),
                EngagedAtUtc = ReadDateTimeAttribute(item, nameof(CurveStopwatchEntry.EngagedAtUtc), default),
                ReenableAtUtc = ReadDateTimeAttribute(item, nameof(CurveStopwatchEntry.ReenableAtUtc), default),
            });
        }

        return stopwatches;
    }
}
