using BatteryTrayAppDotNET.Models;
using BatteryTrayAppDotNET.Services;
using Xunit;

namespace BatteryTrayAppDotNET.Tests;

public sealed class BatteryTriggerEngineTests
{
    [Fact]
    public void StartupReadingNeverFiresEvenWhenEveryStateQualifies()
    {
        Assert.Empty(new BatteryTriggerEngine().Observe(OnBattery(5), AllConditions()));
        Assert.Empty(new BatteryTriggerEngine().Observe(Charging(85), AllConditions()));
        Assert.Empty(new BatteryTriggerEngine().Observe(Full(), AllConditions()));
    }

    [Fact]
    public void StartupStateBelowThresholdDoesNotFireWhenUnplugged()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(Charging(15));

        Assert.DoesNotContain(BatteryTriggerCondition.BatteryBelow20, engine.ObserveConditions(OnBattery(15)));
        Assert.DoesNotContain(BatteryTriggerCondition.BatteryBelow20, engine.ObserveConditions(OnBattery(14)));
    }

    [Fact]
    public void RepeatedReadingDoesNotFireAgain()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(OnBattery(21));
        Assert.Contains(BatteryTriggerCondition.BatteryBelow20, engine.ObserveConditions(OnBattery(20)));

        Assert.Empty(engine.ObserveConditions(OnBattery(20)));
    }

    [Fact]
    public void Below20FiresOnTheReadingThatReaches20OnBattery()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(OnBattery(25));

        Assert.Empty(engine.ObserveConditions(OnBattery(21)));
        Assert.Equal([BatteryTriggerCondition.BatteryBelow20], engine.ObserveConditions(OnBattery(20)));
    }

    [Fact]
    public void Below10FiresOnTheReadingThatReaches10OnBattery()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(OnBattery(12));

        Assert.Empty(engine.ObserveConditions(OnBattery(11)));
        Assert.Equal([BatteryTriggerCondition.BatteryBelow10], engine.ObserveConditions(OnBattery(10)));
    }

    [Fact]
    public void LowThresholdWaitsWhilePluggedInAndFiresOnUnplug()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(Plugged(30));

        Assert.Empty(engine.ObserveConditions(Plugged(18)));
        Assert.Equal(
            [BatteryTriggerCondition.BatteryBelow20, BatteryTriggerCondition.ExternalPowerDisconnected],
            engine.ObserveConditions(OnBattery(18)));
    }

    [Fact]
    public void LowThresholdJitterFiresOnceAndRearmsOnlyAtThreeAboveThreshold()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(OnBattery(21));

        Assert.Contains(BatteryTriggerCondition.BatteryBelow20, engine.ObserveConditions(OnBattery(20)));
        Assert.Empty(engine.ObserveConditions(OnBattery(21)));
        Assert.Empty(engine.ObserveConditions(OnBattery(20)));
        Assert.Empty(engine.ObserveConditions(OnBattery(22)));
        Assert.Empty(engine.ObserveConditions(OnBattery(20)));

        Assert.Empty(engine.ObserveConditions(OnBattery(23)));
        Assert.Contains(BatteryTriggerCondition.BatteryBelow20, engine.ObserveConditions(OnBattery(20)));
    }

    [Fact]
    public void CriticalThresholdRearmsAt13()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(OnBattery(11));
        Assert.Contains(BatteryTriggerCondition.BatteryBelow10, engine.ObserveConditions(OnBattery(10)));

        Assert.Empty(engine.ObserveConditions(OnBattery(12)));
        Assert.DoesNotContain(BatteryTriggerCondition.BatteryBelow10, engine.ObserveConditions(OnBattery(9)));
        Assert.Empty(engine.ObserveConditions(OnBattery(13)));
        Assert.Contains(BatteryTriggerCondition.BatteryBelow10, engine.ObserveConditions(OnBattery(10)));
    }

    [Fact]
    public void ResumeReadingIsAnOrdinaryTransition()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(OnBattery(30));

        Assert.Equal(
            [BatteryTriggerCondition.BatteryBelow20, BatteryTriggerCondition.BatteryBelow10],
            engine.ObserveConditions(OnBattery(8)));
    }

    [Fact]
    public void Above80FiresOnTheReadingThatReaches80WhilePluggedIn()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(Charging(78));

        Assert.Empty(engine.ObserveConditions(Charging(79)));
        Assert.Equal([BatteryTriggerCondition.BatteryAbove80], engine.ObserveConditions(Charging(80)));
    }

    [Fact]
    public void Above80FiresWhenAChargeLimitHoldsTheBatteryAt80WithoutCharging()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(Charging(79));

        Assert.Contains(BatteryTriggerCondition.BatteryAbove80, engine.ObserveConditions(Plugged(80)));
    }

    [Fact]
    public void Above80DoesNotFireOnBattery()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(OnBattery(79));

        Assert.Empty(engine.ObserveConditions(OnBattery(80)));
    }

    [Fact]
    public void Above80JitterFiresOnceAndRearmsOnlyAt77()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(Charging(79));
        Assert.Contains(BatteryTriggerCondition.BatteryAbove80, engine.ObserveConditions(Charging(80)));

        Assert.Empty(engine.ObserveConditions(Charging(79)));
        Assert.Empty(engine.ObserveConditions(Charging(80)));
        Assert.Empty(engine.ObserveConditions(Charging(78)));
        Assert.Empty(engine.ObserveConditions(Charging(81)));

        Assert.Empty(engine.ObserveConditions(Charging(77)));
        Assert.Contains(BatteryTriggerCondition.BatteryAbove80, engine.ObserveConditions(Charging(80)));
    }

    [Fact]
    public void StartupAbove80DoesNotFireUntilTheChargeDropsTo77()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(Charging(85));

        Assert.Empty(engine.ObserveConditions(Charging(86)));
        Assert.Contains(BatteryTriggerCondition.ExternalPowerDisconnected, engine.ObserveConditions(OnBattery(77)));
        Assert.Contains(BatteryTriggerCondition.BatteryAbove80, engine.ObserveConditions(Charging(80)));
    }

    [Fact]
    public void ChargingStartedAndStoppedFireOnTheEdge()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(Plugged(50));

        Assert.Equal([BatteryTriggerCondition.ChargingStarted], engine.ObserveConditions(Charging(50)));
        Assert.Empty(engine.ObserveConditions(Charging(51)));
        Assert.Equal([BatteryTriggerCondition.ChargingStopped], engine.ObserveConditions(Plugged(51)));
    }

    [Fact]
    public void ExternalPowerConnectedAndDisconnectedFireOnTheEdge()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(OnBattery(50));

        Assert.Equal([BatteryTriggerCondition.ExternalPowerConnected], engine.ObserveConditions(Plugged(50)));
        Assert.Empty(engine.ObserveConditions(Plugged(50)));
        Assert.Equal([BatteryTriggerCondition.ExternalPowerDisconnected], engine.ObserveConditions(OnBattery(50)));
    }

    [Fact]
    public void UnpluggingWhileChargingFiresBothEdges()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(Charging(50));

        Assert.Equal(
            [BatteryTriggerCondition.ChargingStopped, BatteryTriggerCondition.ExternalPowerDisconnected],
            engine.ObserveConditions(OnBattery(50)));
    }

    [Fact]
    public void FullyChargedFiresWhenTheBatteryBecomesFull()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(Charging(99));

        Assert.Equal(
            [BatteryTriggerCondition.ChargingStopped, BatteryTriggerCondition.FullyCharged],
            engine.ObserveConditions(Full()));
    }

    [Fact]
    public void FullyChargedTrickleChargingDoesNotFireAgain()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(Charging(99));
        Assert.Contains(BatteryTriggerCondition.FullyCharged, engine.ObserveConditions(Full()));

        Assert.DoesNotContain(BatteryTriggerCondition.FullyCharged, engine.ObserveConditions(Charging(99)));
        Assert.DoesNotContain(BatteryTriggerCondition.FullyCharged, engine.ObserveConditions(Full()));
        Assert.DoesNotContain(BatteryTriggerCondition.FullyCharged, engine.ObserveConditions(Charging(98)));
        Assert.DoesNotContain(BatteryTriggerCondition.FullyCharged, engine.ObserveConditions(Full()));
    }

    [Fact]
    public void FullyChargedRearmsAfterUnplugOrDropTo97()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(Full());

        engine.ObserveConditions(OnBattery(100));
        Assert.Contains(BatteryTriggerCondition.FullyCharged, engine.ObserveConditions(Full()));

        engine.ObserveConditions(Plugged(97));
        Assert.Contains(BatteryTriggerCondition.FullyCharged, engine.ObserveConditions(Full()));
    }

    [Fact]
    public void MultipleTriggersSharingAConditionEachFireInListOrder()
    {
        BatteryTriggerEngine engine = new();
        BatteryTriggerEntry notify = Trigger(1, BatteryTriggerCondition.ChargingStarted, BatteryTriggerAction.ShowNotification);
        BatteryTriggerEntry other = Trigger(2, BatteryTriggerCondition.ChargingStopped, BatteryTriggerAction.OpenSettings);
        BatteryTriggerEntry flyout = Trigger(3, BatteryTriggerCondition.ChargingStarted, BatteryTriggerAction.OpenFlyout);
        engine.Observe(Plugged(50), [notify, other, flyout]);

        IReadOnlyList<BatteryTriggerFiring> firings = engine.Observe(Charging(50), [notify, other, flyout]);

        Assert.Equal(
            [
                new BatteryTriggerFiring(notify, BatteryTriggerCondition.ChargingStarted, BatteryTriggerAction.ShowNotification),
                new BatteryTriggerFiring(flyout, BatteryTriggerCondition.ChargingStarted, BatteryTriggerAction.OpenFlyout)
            ],
            firings);
    }

    [Fact]
    public void TriggerWithoutConditionOrActionNeverFires()
    {
        BatteryTriggerEngine engine = new();
        BatteryTriggerEntry noCondition = new() { TriggerID = 1, Action = BatteryTriggerAction.ShowNotification };
        BatteryTriggerEntry noAction = new() { TriggerID = 2, Condition = BatteryTriggerCondition.ExternalPowerConnected };
        BatteryTriggerEntry empty = new() { TriggerID = 3 };
        engine.Observe(OnBattery(50), [noCondition, noAction, empty]);

        Assert.Empty(engine.Observe(Plugged(50), [noCondition, noAction, empty]));
    }

    [Fact]
    public void NoBatteryKeepsTheEngineIdle()
    {
        BatteryTriggerEngine engine = new();

        Assert.Empty(engine.Observe(NoBattery(), AllConditions()));
        Assert.Empty(engine.Observe(NoBattery(), AllConditions()));
        Assert.Empty(engine.Observe(OnBattery(5), AllConditions()));
    }

    [Fact]
    public void BatteryDisappearingDropsTheBaseline()
    {
        BatteryTriggerEngine engine = new();
        engine.ObserveConditions(OnBattery(50));

        Assert.Empty(engine.ObserveConditions(NoBattery()));
        Assert.Empty(engine.ObserveConditions(Plugged(5)));
        Assert.Equal([BatteryTriggerCondition.ExternalPowerDisconnected], engine.ObserveConditions(OnBattery(5)));
    }

    [Fact]
    public void TriggerConfiguredLaterFiresOnTheNextTransitionOnly()
    {
        BatteryTriggerEngine engine = new();
        BatteryTriggerEntry trigger = new() { TriggerID = 1 };
        engine.Observe(OnBattery(50), [trigger]);
        Assert.Empty(engine.Observe(Plugged(50), [trigger]));

        trigger.Condition = BatteryTriggerCondition.ExternalPowerConnected;
        trigger.Action = BatteryTriggerAction.ShowNotification;
        Assert.Empty(engine.Observe(Plugged(50), [trigger]));
        engine.Observe(OnBattery(50), [trigger]);

        Assert.Single(engine.Observe(Plugged(50), [trigger]));
    }

    private static List<BatteryTriggerEntry> AllConditions() =>
        Enum.GetValues<BatteryTriggerCondition>()
            .Select((condition, index) => Trigger(index + 1, condition, BatteryTriggerAction.ShowNotification))
            .ToList();

    private static BatteryTriggerEntry Trigger(int id, BatteryTriggerCondition condition, BatteryTriggerAction action) =>
        new() { TriggerID = id, Title = $"Trigger {id}", Condition = condition, Action = action };

    private static BatterySnapshot OnBattery(int percent) => Reading(percent, plugged: false, charging: false);

    private static BatterySnapshot Plugged(int percent) => Reading(percent, plugged: true, charging: false);

    private static BatterySnapshot Charging(int percent) => Reading(percent, plugged: true, charging: true);

    private static BatterySnapshot Full() =>
        Reading(percent: 100, plugged: true, charging: false) with { IsFullyCharged = true };

    private static BatterySnapshot NoBattery() => BatterySnapshot.Unknown;

    private static BatterySnapshot Reading(int percent, bool plugged, bool charging) =>
        BatterySnapshot.Unknown with
        {
            BatteryPresent = true,
            ChargePercentage = percent,
            IsOnExternalPower = plugged,
            IsCharging = charging,
            IsFullyCharged = false
        };
}
