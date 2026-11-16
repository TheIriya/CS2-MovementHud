using CounterStrikeSharp.API.Core;
using MovementHud;

internal static class RecoveryChecks
{
    internal static void Run(Action<bool, string> check, CCSPlayerController player)
    {
        foreach (var bonus in new[] { false, true })
        {
            var plugin = new TimerFixture();
            var timer = plugin.playerTimers[1];
            timer.CurrentZoneInfo.InMainMapStartZone = !bonus;
            timer.CurrentZoneInfo.InBonusStartZone = bonus;
            timer.inStartzone = true;
            var recovery = SharpTimerStartZoneRecovery.Capture(plugin, 1);
            recovery.ResetStartEntry();
            check(!recovery.HasEnteredTriggerStart() && !timer.inStartzone,
                "The old start entry must not re-enable timing after teleporting out.");
            check(recovery.IsBlocked, "Checkpoint runs remain blocked on the course and at the finish.");

            // SharpTimer replaces CurrentZoneInfo when entering either trigger start.
            timer.CurrentZoneInfo = new ZoneFixture { InMainMapStartZone = !bonus, InBonusStartZone = bonus };
            check(recovery.HasEnteredTriggerStart(), "Recognize a fresh main or bonus trigger entry.");
            recovery.Restore();
            check(!timer.IsTimerBlocked, "A fresh run must be enabled without !r or a respawn.");
            check(plugin.playerTimers[2].IsTimerBlocked, "Recovery must only affect the checkpoint user.");

            timer.CurrentZoneInfo = new ZoneFixture { InMainMapStartZone = true };
            timer.IsTimerBlocked = true;
            recovery.ResetStartEntry();
            check(!recovery.HasEnteredTriggerStart(), "Repeated teleports must clear the newest start entry too.");
        }

        var fake = new TimerFixture { useTriggers = false };
        var fakeRecovery = SharpTimerStartZoneRecovery.Capture(fake, 1);
        check(fakeRecovery.IsInsideFakeStart(15, 15, 35), "Use native fake-zone height and geometry.");
        check(!fakeRecovery.IsInsideFakeStart(15, 15, 46), "Outside the native fake-zone height must stay blocked.");
        check(!fakeRecovery.IsInsideFakeStart(80, 80, 80), "Course/finish positions must not re-enable timing.");
        fakeRecovery.RestoreAtFakeStart(player, 1, 2, 3);
        check(!fake.playerTimers[1].IsTimerBlocked && fake.CoordinateEntries == 1 && fake.playerTimers[1].inStartzone,
            "Initialize native fake-start logic immediately, including quick entry/exit.");
        check(fake.LastSpeed == new FixtureVector(1, 2, 3), "Native start-speed checks receive the real velocity.");

        fake.useTriggers = true;
        check(!fakeRecovery.IsInsideFakeStart(15, 15, 15), "Trigger-only maps must ignore unused fake starts.");
        fake.useTriggersAndFakeZones = true;
        fake.currentBonusStartC1[3] = new(100, 100, 100);
        fake.currentBonusStartC2[3] = new(110, 110, 110);
        fake.totalBonuses[3] = 3;
        check(fakeRecovery.IsInsideFakeStart(105, 105, 105), "Mixed maps must recognize configured fake bonus starts.");
        check(!fakeRecovery.IsInsideFakeStart(15, 15, 15), "Mixed maps must still use main trigger starts.");
        fake.totalBonuses[3] = 0;
        check(!fakeRecovery.IsInsideFakeStart(105, 105, 105), "Disabled/removed bonus zones must stop restoring timing.");
        fake.useTriggers = false;
        fake.currentMapStartC1 = fake.currentMapStartC2 = default;
        check(!fakeRecovery.IsInsideFakeStart(0, 0, 0), "Unset coordinate zones must not restore timing.");

        fake.playerTimers[1] = new();
        check(!fakeRecovery.IsCurrent, "Slot reuse must discard old recovery state.");
        fakeRecovery.Restore();
        check(fake.playerTimers[1].IsTimerBlocked, "Do not enable another player's replacement state.");

        var reload = new TimerFixture();
        var reloadRecovery = SharpTimerStartZoneRecovery.Capture(reload, 1);
        reload.playerTimers = new() { [1] = new() };
        check(!reloadRecovery.IsCurrent, "Replacing the timer dictionary must discard recovery state.");
        var newRecovery = SharpTimerStartZoneRecovery.Capture(reload, 1);
        _ = new TimerFixture();
        check(!newRecovery.IsCurrent, "Plugin reload must invalidate the previous plugin instance.");

        try
        {
            SharpTimerStartZoneRecovery.Capture(new object(), 1);
            throw new Exception("Unsupported upstream state must reject capture before timing is disabled.");
        }
        catch (MissingFieldException) { check(true, "Unsupported upstream state rejected."); }
    }
}

// These fixtures model only the public state/geometry contract inspected in
// SharpTimer 0.4.2. They do not claim to replace a live CS2 trigger/physics test.
public sealed class TimerFixture
{
    public static TimerFixture Instance = null!;
    public Dictionary<int, TimerStateFixture> playerTimers = new() { [1] = new(), [2] = new() };
    public bool useTriggers = true;
    public bool useTriggersAndFakeZones;
    public FixtureVector currentMapStartC1 = new(10, 10, 10);
    public FixtureVector currentMapStartC2 = new(20, 20, 20);
    public FixtureVector[] currentBonusStartC1 = new FixtureVector[11];
    public FixtureVector[] currentBonusStartC2 = new FixtureVector[11];
    public int[] totalBonuses = new int[11];
    public GeometryFixture Utils = new();
    public int CoordinateEntries;
    public FixtureVector LastSpeed;
    public TimerFixture() => Instance = this;
    private void CheckPlayerCoords(CCSPlayerController player, FixtureVector speed)
    {
        if (playerTimers[1].IsTimerBlocked) throw new Exception("Native entry must run with timing enabled.");
        CoordinateEntries++;
        LastSpeed = speed;
        playerTimers[1].inStartzone = true;
    }
}

public sealed class TimerStateFixture
{
    public bool IsTimerBlocked { get; set; } = true;
    public bool inStartzone { get; set; }
    public ZoneFixture CurrentZoneInfo { get; set; } = new();
}

public sealed class ZoneFixture
{
    public bool InMainMapStartZone { get; set; }
    public bool InBonusStartZone { get; set; }
}

public readonly record struct FixtureVector(float X, float Y, float Z);

public sealed class GeometryFixture
{
    public bool IsVectorInsideBox(FixtureVector point, FixtureVector a, FixtureVector b, bool fakezone) =>
        point.X >= Math.Min(a.X, b.X) && point.X <= Math.Max(a.X, b.X) &&
        point.Y >= Math.Min(a.Y, b.Y) && point.Y <= Math.Max(a.Y, b.Y) &&
        point.Z >= Math.Min(a.Z, b.Z) && point.Z <= Math.Max(a.Z, b.Z + (fakezone ? 25 : 0));
}
