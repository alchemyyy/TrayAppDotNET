using System.Xml.Linq;
using static TrayAppDotNETCommon.Models.TrayAppDotNETSettingsXml;

namespace FanControlTrayAppDotNET.Models;

public partial class AppSettings
{
    private void SaveXml(Stream stream)
    {
        XDocument document = new(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("AppSettings",
                Bool(nameof(RunOnStartup), RunOnStartup),
                Bool(nameof(Autosave), Autosave),
                Bool(nameof(DefaultToRPMMode), DefaultToRPMMode),
                Int(nameof(DefaultJumpstartDutyCycle), DefaultJumpstartDutyCycle),
                Int(nameof(DefaultDeltaMaxDutyCycle), DefaultDeltaMaxDutyCycle),
                Text(nameof(DefaultAssignedCurve), DefaultAssignedCurve),
                Enum(nameof(ContextMenuPosition), ContextMenuPosition),
                Int(nameof(ContextMenuFontSize), ContextMenuFontSize),
                Enum(nameof(ThemeMode), ThemeMode),
                NullableThemeColorElement(nameof(TextColor), TextColor),
                NullableThemeColorElement(nameof(BackgroundColor), BackgroundColor),
                NullableThemeColorElement(nameof(FlyoutBackgroundColor), FlyoutBackgroundColor),
                NullableThemeColorElement(nameof(FlyoutTitleBarBackgroundColor), FlyoutTitleBarBackgroundColor),
                NullableThemeColorElement(nameof(FanCardBackgroundColor), FanCardBackgroundColor),
                NullableThemeColorElement(nameof(GroupCardBackgroundColor), GroupCardBackgroundColor),
                NullableThemeColorElement(nameof(CardBorderColor), CardBorderColor),
                Enum(nameof(TrayIconStyle), TrayIconStyle),
                NullableThemeColorElement(nameof(TrayIconColor), TrayIconColor),
                Bool(nameof(EnableRoundedCorners), EnableRoundedCorners),
                Bool(nameof(SquareFlyoutTitleBarCorners), SquareFlyoutTitleBarCorners),
                Bool(nameof(EnableCardBorders), EnableCardBorders),
                Bool(nameof(EnableHoveredCardBorders), EnableHoveredCardBorders),
                Bool(nameof(HideGroupedFanCardBorders), HideGroupedFanCardBorders),
                Bool(nameof(UseGroupBackgroundForGroupedFanCards), UseGroupBackgroundForGroupedFanCards),
                Int(nameof(FlyoutCardSpacing), FlyoutCardSpacing),
                Int(nameof(FlyoutCardHorizontalInset), FlyoutCardHorizontalInset),
                Int(nameof(FlyoutTitleBarCardSpacing), FlyoutTitleBarCardSpacing),
                Bool(nameof(TrayScrollEnabled), TrayScrollEnabled),
                Enum(nameof(TrayDoubleClickAction), TrayDoubleClickAction),
                Enum(nameof(TrayCtrlLeftClickAction), TrayCtrlLeftClickAction),
                Enum(nameof(TrayAltLeftClickAction), TrayAltLeftClickAction),
                Enum(nameof(TrayCtrlRightClickAction), TrayCtrlRightClickAction),
                Enum(nameof(TrayAltRightClickAction), TrayAltRightClickAction),
                Enum(nameof(TrayCtrlDoubleLeftClickAction), TrayCtrlDoubleLeftClickAction),
                Enum(nameof(TrayAltDoubleLeftClickAction), TrayAltDoubleLeftClickAction),
                Bool(nameof(AllowFlyoutUndock), AllowFlyoutUndock),
                Bool(nameof(RestoreFlyoutUndockedOnStartup), RestoreFlyoutUndockedOnStartup),
                Bool(nameof(FlyoutUndocked), FlyoutUndocked),
                Bool(nameof(FlyoutHasSavedPosition), FlyoutHasSavedPosition),
                Double(nameof(FlyoutLeft), FlyoutLeft),
                Double(nameof(FlyoutTop), FlyoutTop),
                Bool(nameof(ShowNonFunctioningFans), ShowNonFunctioningFans),
                Bool(nameof(ShowCPUTempInTooltip), ShowCPUTempInTooltip),
                Bool(nameof(ShowGPUTempInTooltip), ShowGPUTempInTooltip),
                SliderThumbElement(SerializedSliderThumb),
                Bool(nameof(CheckForUpdatesEnabled), CheckForUpdatesEnabled),
                Bool(nameof(ShowUpdateNotificationsEnabled), ShowUpdateNotificationsEnabled),
                Bool(nameof(ShowUpdateButtonInFlyout), ShowUpdateButtonInFlyout),
                Int(nameof(UpdateCheckIntervalMs), UpdateCheckIntervalMs),
                Bool(nameof(KeepFlyoutWarm), KeepFlyoutWarm),
                Bool(nameof(KeepTrayContextMenuWarm), KeepTrayContextMenuWarm),
                HotkeysElement(Hotkeys),
                FansElement(Fans),
                DataSourcesElement(DataSources),
                CurvesElement(Curves),
                DeadbandsElement(Deadbands),
                FanGroupsElement(FanGroups),
                FanProfilesElement(FanProfiles),
                Int(nameof(SelectedFanProfileIndex), SelectedFanProfileIndex)));

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
            Autosave = ReadBool(root, nameof(Autosave), true),
            DefaultToRPMMode = ReadBool(root, nameof(DefaultToRPMMode), false),
            DefaultJumpstartDutyCycle = ReadInt(root, nameof(DefaultJumpstartDutyCycle), 50),
            DefaultDeltaMaxDutyCycle = ReadInt(root, nameof(DefaultDeltaMaxDutyCycle), 100),
            DefaultAssignedCurve = ReadString(root, nameof(DefaultAssignedCurve), "None"),
            ContextMenuPosition = ReadEnum(root, nameof(ContextMenuPosition), ContextMenuPosition.Modern),
            ContextMenuFontSize = ReadInt(root, nameof(ContextMenuFontSize), 15),
            ThemeMode = ReadEnum(root, nameof(ThemeMode), ThemeMode.System),
            TextColor = ReadNullableThemeColor(root, nameof(TextColor)),
            BackgroundColor = ReadNullableThemeColor(root, nameof(BackgroundColor)),
            FlyoutBackgroundColor = ReadNullableThemeColor(root, nameof(FlyoutBackgroundColor)),
            FlyoutTitleBarBackgroundColor = ReadNullableThemeColor(root, nameof(FlyoutTitleBarBackgroundColor)),
            FanCardBackgroundColor = ReadNullableThemeColor(root, nameof(FanCardBackgroundColor)),
            GroupCardBackgroundColor = ReadNullableThemeColor(root, nameof(GroupCardBackgroundColor)),
            CardBorderColor = ReadNullableThemeColor(root, nameof(CardBorderColor)),
            TrayIconStyle = ReadEnum(root, nameof(TrayIconStyle), TrayIconStyle.Dynamic),
            TrayIconColor = ReadNullableThemeColor(root, nameof(TrayIconColor)),
            EnableRoundedCorners = ReadBool(root, nameof(EnableRoundedCorners), true),
            SquareFlyoutTitleBarCorners = ReadBool(root, nameof(SquareFlyoutTitleBarCorners), false),
            EnableCardBorders = ReadBool(root, nameof(EnableCardBorders), false),
            EnableHoveredCardBorders = ReadBool(root, nameof(EnableHoveredCardBorders), false),
            HideGroupedFanCardBorders = ReadBool(root, nameof(HideGroupedFanCardBorders), true),
            UseGroupBackgroundForGroupedFanCards = ReadBool(root, nameof(UseGroupBackgroundForGroupedFanCards), false),
            FlyoutCardSpacing = ReadInt(root, nameof(FlyoutCardSpacing), 1),
            FlyoutCardHorizontalInset = ReadInt(root, nameof(FlyoutCardHorizontalInset), 1),
            FlyoutTitleBarCardSpacing = ReadInt(root, nameof(FlyoutTitleBarCardSpacing), 2),
            TrayScrollEnabled = ReadBool(root, nameof(TrayScrollEnabled), true),
            TrayDoubleClickAction = ReadEnum(root, nameof(TrayDoubleClickAction), TrayClickAction.OpenSettings),
            TrayCtrlLeftClickAction = ReadEnum(root, nameof(TrayCtrlLeftClickAction), TrayClickAction.Nothing),
            TrayAltLeftClickAction = ReadEnum(root, nameof(TrayAltLeftClickAction), TrayClickAction.Nothing),
            TrayCtrlRightClickAction = ReadEnum(root, nameof(TrayCtrlRightClickAction), TrayClickAction.Nothing),
            TrayAltRightClickAction = ReadEnum(root, nameof(TrayAltRightClickAction), TrayClickAction.Nothing),
            TrayCtrlDoubleLeftClickAction =
                ReadEnum(root, nameof(TrayCtrlDoubleLeftClickAction), TrayClickAction.Nothing),
            TrayAltDoubleLeftClickAction =
                ReadEnum(root, nameof(TrayAltDoubleLeftClickAction), TrayClickAction.Nothing),
            AllowFlyoutUndock = ReadBool(root, nameof(AllowFlyoutUndock), true),
            RestoreFlyoutUndockedOnStartup = ReadBool(root, nameof(RestoreFlyoutUndockedOnStartup), true),
            FlyoutUndocked = ReadBool(root, nameof(FlyoutUndocked), false),
            FlyoutHasSavedPosition = ReadBool(root, nameof(FlyoutHasSavedPosition), false),
            FlyoutLeft = ReadDouble(root, nameof(FlyoutLeft), 0),
            FlyoutTop = ReadDouble(root, nameof(FlyoutTop), 0),
            ShowNonFunctioningFans = ReadBool(root, nameof(ShowNonFunctioningFans), true),
            ShowCPUTempInTooltip = ReadBool(root, nameof(ShowCPUTempInTooltip), true),
            ShowGPUTempInTooltip = ReadBool(root, nameof(ShowGPUTempInTooltip), true),
            SerializedSliderThumb = ReadSliderThumb(root.Element("SliderThumb"), defaultName: "Capsule"),
            CheckForUpdatesEnabled = ReadBool(root, nameof(CheckForUpdatesEnabled), true),
            ShowUpdateNotificationsEnabled = ReadBool(root, nameof(ShowUpdateNotificationsEnabled), false),
            ShowUpdateButtonInFlyout = ReadBool(root, nameof(ShowUpdateButtonInFlyout), true),
            UpdateCheckIntervalMs =
                ReadInt(root, nameof(UpdateCheckIntervalMs), TimeConstants.UpdateCheckIntervalDefaultMs),
            KeepFlyoutWarm = ReadBool(root, nameof(KeepFlyoutWarm), true),
            KeepTrayContextMenuWarm = ReadBool(root, nameof(KeepTrayContextMenuWarm), true),
            Hotkeys = ReadHotkeys(root.Element("Hotkeys")),
            Fans = ReadFans(root.Element("Fans")),
            DataSources = ReadDataSources(root.Element("DataSources")),
            Curves = ReadCurves(root.Element("Curves")),
            Deadbands = ReadDeadbands(root.Element("Deadbands")),
            FanGroups = ReadFanGroups(root.Element("FanGroups")),
            FanProfiles = ReadFanProfiles(root.Element("FanProfiles")),
            SelectedFanProfileIndex = ReadInt(root, nameof(SelectedFanProfileIndex), 0),
        };

        settings.WireColorCallbacks();
        settings.InitializeSliderThumbCatalog();
        return settings;
    }

    private static XElement FansElement(IEnumerable<Fan> fans)
    {
        XElement element = new("Fans");
        foreach (Fan fan in fans)
        {
            XElement fanElement = new("Fan",
                Attribute(nameof(Fan.RPMMode), fan.RPMMode),
                Attribute(nameof(Fan.ClampLow), fan.ClampLow),
                Attribute(nameof(Fan.ClampHigh), fan.ClampHigh),
                Attribute(nameof(Fan.WarnLow), fan.WarnLow),
                Attribute(nameof(Fan.WarnHigh), fan.WarnHigh),
                Attribute(nameof(Fan.DeltaMax), fan.DeltaMax),
                Attribute(nameof(Fan.Offset), fan.Offset),
                Attribute(nameof(Fan.FanDisplayedValue), fan.FanDisplayedValue),
                Attribute(nameof(Fan.StartupSpeed), fan.StartupSpeed),
                Attribute(nameof(Fan.MaxRPM), fan.MaxRPM),
                Attribute(nameof(Fan.AssignedCurveName), fan.AssignedCurveName),
                Attribute(nameof(Fan.DataSourceKey), fan.DataSourceKey),
                Attribute(nameof(Fan.ControllerModel), fan.ControllerModel),
                Attribute(nameof(Fan.ControlsName), fan.ControlsName),
                Attribute(nameof(Fan.FansName), fan.FansName),
                Attribute(nameof(Fan.UserDefinedName), fan.UserDefinedName),
                Attribute(nameof(Fan.ForcedNonFunctioning), fan.ForcedNonFunctioning),
                Attribute(nameof(Fan.ModeLocked), fan.ModeLocked),
                Attribute(nameof(Fan.CurrentControlMode), fan.CurrentControlMode),
                Attribute(nameof(Fan.DeadbandsName), fan.DeadbandsName),
                Attribute(nameof(Fan.Group), fan.Group),
                Attribute(nameof(Fan.FlyoutDisplayOrder), fan.FlyoutDisplayOrder));
            fanElement.Add(TriggersElement(fan.Triggers));
            element.Add(fanElement);
        }

        return element;
    }

    private static XElement DataSourcesElement(IEnumerable<DataSource> sources)
    {
        XElement element = new("DataSources");
        foreach (DataSource source in sources)
        {
            element.Add(new XElement("DataSource",
                Attribute(nameof(DataSource.DataSourceKey), source.DataSourceKey),
                Attribute(nameof(DataSource.UserDefinedName), source.UserDefinedName),
                Attribute(nameof(DataSource.ControllerName), source.ControllerName),
                Attribute(nameof(DataSource.DataSourceType), source.DataSourceType),
                Attribute(nameof(DataSource.Value), source.Value),
                Attribute(nameof(DataSource.TransformString), source.TransformString)));
        }

        return element;
    }

    private static XElement CurvesElement(IEnumerable<Curve> curves)
    {
        XElement element = new("Curves");
        foreach (Curve curve in curves)
        {
            XElement curveElement = new("Curve",
                Attribute(nameof(Curve.CurveName), curve.CurveName),
                Attribute(nameof(Curve.SmoothingFactor), curve.SmoothingFactor),
                Attribute(nameof(Curve.ClampDutyMin), curve.ClampDutyMin),
                Attribute(nameof(Curve.ClampDutyMax), curve.ClampDutyMax),
                Attribute(nameof(Curve.ClampXMin), curve.ClampXMin),
                Attribute(nameof(Curve.ClampXMax), curve.ClampXMax),
                Attribute(nameof(Curve.HysteresisMs), curve.HysteresisMs),
                Attribute(nameof(Curve.SelectedDataSourceKey), curve.SelectedDataSourceKey));
            XElement nodes = new("Nodes");
            foreach (CurveNode node in curve.CurveNodes)
                nodes.Add(new XElement("Node", Attribute(nameof(CurveNode.X), node.X),
                    Attribute(nameof(CurveNode.Y), node.Y)));
            curveElement.Add(nodes);
            element.Add(curveElement);
        }

        return element;
    }

    private static XElement DeadbandsElement(IEnumerable<DeadbandsList> deadbands)
    {
        XElement element = new("Deadbands");
        foreach (DeadbandsList list in deadbands)
        {
            XElement listElement = new("DeadbandsList",
                Attribute(nameof(DeadbandsList.Name), list.Name),
                Attribute(nameof(DeadbandsList.RPMMode), list.RPMMode));
            XElement bands = new("Bands");
            foreach (DeadbandRange band in list.Bands)
                bands.Add(new XElement("Band", Attribute(nameof(DeadbandRange.Lower), band.Lower),
                    Attribute(nameof(DeadbandRange.Upper), band.Upper)));
            listElement.Add(bands);
            element.Add(listElement);
        }

        return element;
    }

    private static XElement FanGroupsElement(IEnumerable<FanGroup> groups)
    {
        XElement element = new("FanGroups");
        foreach (FanGroup group in groups)
        {
            element.Add(new XElement("Group",
                Attribute(nameof(FanGroup.Name), group.Name),
                Attribute(nameof(FanGroup.DisplayOrder), group.DisplayOrder),
                Attribute(nameof(FanGroup.IsCollapsed), group.IsCollapsed),
                Attribute(nameof(FanGroup.FanDisplayedValue), group.FanDisplayedValue),
                Attribute(nameof(FanGroup.CurrentControlMode), group.CurrentControlMode),
                Attribute(nameof(FanGroup.AssignedCurveName), group.AssignedCurveName)));
        }

        return element;
    }

    private static XElement FanProfilesElement(IEnumerable<FanProfile> profiles)
    {
        XElement element = new("FanProfiles");
        foreach (FanProfile profile in profiles)
        {
            XElement profileElement = new("Profile", Attribute(nameof(FanProfile.Name), profile.Name));
            XElement fans = new("Fans");
            foreach (FanProfileEntry fan in profile.Fans)
            {
                fans.Add(new XElement("Fan",
                    Attribute(nameof(FanProfileEntry.DataSourceKey), fan.DataSourceKey),
                    Attribute(nameof(FanProfileEntry.AssignedCurveName), fan.AssignedCurveName),
                    Attribute(nameof(FanProfileEntry.ModeLocked), fan.ModeLocked),
                    Attribute(nameof(FanProfileEntry.FanDisplayedValue), fan.FanDisplayedValue),
                    Attribute(nameof(FanProfileEntry.CurrentControlMode), fan.CurrentControlMode)));
            }

            profileElement.Add(fans);
            element.Add(profileElement);
        }

        return element;
    }

    private static XElement TriggersElement(IEnumerable<Trigger> triggers)
    {
        XElement element = new("Triggers");
        foreach (Trigger trigger in triggers)
            element.Add(new XElement("Trigger", Attribute(nameof(Trigger.Name), trigger.Name),
                Attribute(nameof(Trigger.Enabled), trigger.Enabled)));
        return element;
    }

    private static List<Fan> ReadFans(XElement? element)
    {
        List<Fan> fans = [];
        if (element == null) return fans;

        foreach (XElement fanElement in element.Elements("Fan"))
        {
            Fan fan = new()
            {
                DataSourceKey = ReadAttribute(fanElement, nameof(Fan.DataSourceKey), string.Empty),
                ControllerModel = ReadAttribute(fanElement, nameof(Fan.ControllerModel), string.Empty),
                ControlsName = ReadAttribute(fanElement, nameof(Fan.ControlsName), string.Empty),
                FansName = ReadAttribute(fanElement, nameof(Fan.FansName), string.Empty),
                RPMMode = ReadBoolAttribute(fanElement, nameof(Fan.RPMMode), false),
                ClampLow = ReadIntAttribute(fanElement, nameof(Fan.ClampLow), 0),
                ClampHigh = ReadIntAttribute(fanElement, nameof(Fan.ClampHigh), 100),
                WarnLow = ReadIntAttribute(fanElement, nameof(Fan.WarnLow), 0),
                WarnHigh = ReadIntAttribute(fanElement, nameof(Fan.WarnHigh), 100),
                DeltaMax = ReadIntAttribute(fanElement, nameof(Fan.DeltaMax), 100),
                Offset = ReadIntAttribute(fanElement, nameof(Fan.Offset), 0),
                FanDisplayedValue = ReadIntAttribute(fanElement, nameof(Fan.FanDisplayedValue), 50),
                StartupSpeed = ReadIntAttribute(fanElement, nameof(Fan.StartupSpeed), 50),
                MaxRPM = ReadIntAttribute(fanElement, nameof(Fan.MaxRPM), -1),
                AssignedCurveName = ReadAttribute(fanElement, nameof(Fan.AssignedCurveName), string.Empty),
                UserDefinedName = ReadAttribute(fanElement, nameof(Fan.UserDefinedName), string.Empty),
                ForcedNonFunctioning = ReadBoolAttribute(fanElement, nameof(Fan.ForcedNonFunctioning), false),
                ModeLocked = ReadBoolAttribute(fanElement, nameof(Fan.ModeLocked), false),
                CurrentControlMode =
                    ReadEnumAttribute(fanElement, nameof(Fan.CurrentControlMode), FanControlMode.Manual),
                DeadbandsName = ReadAttribute(fanElement, nameof(Fan.DeadbandsName), string.Empty),
                Group = ReadNullableAttribute(fanElement, nameof(Fan.Group)),
                FlyoutDisplayOrder = ReadIntAttribute(fanElement, nameof(Fan.FlyoutDisplayOrder), -1),
                Triggers = ReadTriggers(fanElement.Element("Triggers")),
            };
            fans.Add(fan);
        }

        return fans;
    }

    private static List<DataSource> ReadDataSources(XElement? element)
    {
        List<DataSource> sources = [];
        if (element == null) return sources;

        foreach (XElement sourceElement in element.Elements("DataSource"))
        {
            sources.Add(new DataSource
            {
                DataSourceKey = ReadAttribute(sourceElement, nameof(DataSource.DataSourceKey), string.Empty),
                UserDefinedName = ReadAttribute(sourceElement, nameof(DataSource.UserDefinedName), string.Empty),
                ControllerName = ReadAttribute(sourceElement, nameof(DataSource.ControllerName), string.Empty),
                DataSourceType =
                    ReadEnumAttribute(sourceElement, nameof(DataSource.DataSourceType), DataSourceTypeEnum.Unknown),
                Value = ReadLongAttribute(sourceElement, nameof(DataSource.Value), 0),
                TransformString = ReadAttribute(sourceElement, nameof(DataSource.TransformString), string.Empty),
            });
        }

        return sources;
    }

    private static List<Curve> ReadCurves(XElement? element)
    {
        List<Curve> curves = [];
        if (element == null) return curves;

        foreach (XElement curveElement in element.Elements("Curve"))
        {
            Curve curve = new()
            {
                CurveName = ReadAttribute(curveElement, nameof(Curve.CurveName), string.Empty),
                SmoothingFactor = ReadIntAttribute(curveElement, nameof(Curve.SmoothingFactor), 50),
                ClampDutyMin = ReadIntAttribute(curveElement, nameof(Curve.ClampDutyMin), 0),
                ClampDutyMax = ReadIntAttribute(curveElement, nameof(Curve.ClampDutyMax), 100),
                ClampXMin = ReadIntAttribute(curveElement, nameof(Curve.ClampXMin), 0),
                ClampXMax = ReadIntAttribute(curveElement, nameof(Curve.ClampXMax), 100),
                HysteresisMs = ReadIntAttribute(curveElement, nameof(Curve.HysteresisMs), 0),
                SelectedDataSourceKey = ReadAttribute(curveElement, nameof(Curve.SelectedDataSourceKey), string.Empty),
            };

            foreach (XElement nodeElement in curveElement.Element("Nodes")?.Elements("Node") ?? [])
                curve.CurveNodes.Add(new CurveNode(
                    ReadDoubleAttribute(nodeElement, nameof(CurveNode.X), 0),
                    ReadDoubleAttribute(nodeElement, nameof(CurveNode.Y), 0)));
            curves.Add(curve);
        }

        return curves;
    }

    private static List<DeadbandsList> ReadDeadbands(XElement? element)
    {
        List<DeadbandsList> lists = [];
        if (element == null) return lists;

        foreach (XElement listElement in element.Elements("DeadbandsList"))
        {
            DeadbandsList list = new()
            {
                Name = ReadAttribute(listElement, nameof(DeadbandsList.Name), string.Empty),
                RPMMode = ReadBoolAttribute(listElement, nameof(DeadbandsList.RPMMode), false),
            };
            foreach (XElement bandElement in listElement.Element("Bands")?.Elements("Band") ?? [])
                list.Bands.Add(new DeadbandRange(
                    ReadIntAttribute(bandElement, nameof(DeadbandRange.Lower), 0),
                    ReadIntAttribute(bandElement, nameof(DeadbandRange.Upper), 0)));
            lists.Add(list);
        }

        return lists;
    }

    private static List<FanGroup> ReadFanGroups(XElement? element)
    {
        List<FanGroup> groups = [];
        if (element == null) return groups;

        foreach (XElement groupElement in element.Elements("Group"))
        {
            groups.Add(new FanGroup
            {
                Name = ReadAttribute(groupElement, nameof(FanGroup.Name), string.Empty),
                DisplayOrder = ReadIntAttribute(groupElement, nameof(FanGroup.DisplayOrder), 0),
                IsCollapsed = ReadBoolAttribute(groupElement, nameof(FanGroup.IsCollapsed), false),
                FanDisplayedValue = ReadIntAttribute(groupElement, nameof(FanGroup.FanDisplayedValue), 50),
                CurrentControlMode =
                    ReadEnumAttribute(groupElement, nameof(FanGroup.CurrentControlMode), FanControlMode.Manual),
                AssignedCurveName = ReadAttribute(groupElement, nameof(FanGroup.AssignedCurveName), string.Empty),
            });
        }

        return groups;
    }

    private static List<FanProfile> ReadFanProfiles(XElement? element)
    {
        List<FanProfile> profiles = [];
        if (element == null) return profiles;

        foreach (XElement profileElement in element.Elements("Profile"))
        {
            FanProfile profile = new() { Name = ReadAttribute(profileElement, nameof(FanProfile.Name), string.Empty), };
            foreach (XElement fanElement in profileElement.Element("Fans")?.Elements("Fan") ?? [])
            {
                profile.Fans.Add(new FanProfileEntry
                {
                    DataSourceKey = ReadAttribute(fanElement, nameof(FanProfileEntry.DataSourceKey), string.Empty),
                    AssignedCurveName =
                        ReadAttribute(fanElement, nameof(FanProfileEntry.AssignedCurveName), string.Empty),
                    ModeLocked = ReadBoolAttribute(fanElement, nameof(FanProfileEntry.ModeLocked), false),
                    FanDisplayedValue = ReadIntAttribute(fanElement, nameof(FanProfileEntry.FanDisplayedValue), 50),
                    CurrentControlMode = ReadEnumAttribute(fanElement, nameof(FanProfileEntry.CurrentControlMode),
                        FanControlMode.Manual),
                });
            }

            profiles.Add(profile);
        }

        return profiles;
    }

    private static List<Trigger> ReadTriggers(XElement? element)
    {
        List<Trigger> triggers = [];
        if (element == null) return triggers;

        foreach (XElement triggerElement in element.Elements("Trigger"))
        {
            triggers.Add(new Trigger
            {
                Name = ReadAttribute(triggerElement, nameof(Trigger.Name), string.Empty),
                Enabled = ReadBoolAttribute(triggerElement, nameof(Trigger.Enabled), true),
            });
        }

        return triggers;
    }
}
