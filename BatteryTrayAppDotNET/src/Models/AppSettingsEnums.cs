namespace BatteryTrayAppDotNET.Models;

public enum ContextMenuPosition
{
    Classic,
    Modern,
}

public enum BatteryTriggerCondition
{
    BatteryBelow20,
    BatteryBelow10,
    BatteryAbove80,
    ChargingStarted,
    ChargingStopped,
    ExternalPowerConnected,
    ExternalPowerDisconnected,
    FullyCharged,
}

public enum BatteryTriggerAction
{
    ShowNotification,
    OpenFlyout,
    OpenSettings,
    OpenPowerSettings,
}
