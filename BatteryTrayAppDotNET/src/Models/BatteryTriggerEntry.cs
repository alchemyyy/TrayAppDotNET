namespace BatteryTrayAppDotNET.Models;

public sealed class BatteryTriggerEntry
{
    public int TriggerID { get; set; }

    public string Title { get; set; } = string.Empty;

    public BatteryTriggerCondition? Condition { get; set; }

    public BatteryTriggerAction? Action { get; set; }
}
