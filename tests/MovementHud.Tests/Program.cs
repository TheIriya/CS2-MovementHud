using CounterStrikeSharp.API.Core;
using MovementHud;
using SharpTimerAPI;

// No game server is needed: the fake manager models SharpTimer's public API behavior.
var player = (CCSPlayerController)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(CCSPlayerController));
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}

foreach (var bonus in new[] { false, true })
{
    var manager = new FakeTimerManager { MainRunning = !bonus, BonusRunning = bonus, Recording = true };
    Check(SharpTimerCheckpointGuard.StopTimer(manager, player) == TimerStopResult.Stopped, "Enabled timer must be stopped.");
    Check(!manager.Enabled && !manager.MainRunning && !manager.BonusRunning && !manager.Recording, "Main/bonus run and replay must be cancelled.");
    manager.Finish();
    Check(manager.DatabaseWrites == 0 && manager.RankingUpdates == 0, "Checkpoint run must not finish or save.");
    Check(ReferenceEquals(manager.LastPlayer, player), "Only the caller's timer is changed.");
    Check(SharpTimerCheckpointGuard.StopTimer(manager, player) == TimerStopResult.AlreadyDisabled, "Repeated teleport must keep timing off.");
    Check(manager.Toggles == 1, "Already-disabled timing must not be toggled back on.");
    Check(manager.Restarts == 0, "Teleport must not respawn the player through RestartTimer.");
}

var idle = new FakeTimerManager();
Check(SharpTimerCheckpointGuard.StopTimer(idle, player) == TimerStopResult.Stopped, "Enabled but idle timer must be disabled before teleport.");
var disabled = new FakeTimerManager { Enabled = false };
Check(SharpTimerCheckpointGuard.StopTimer(disabled, player) == TimerStopResult.AlreadyDisabled && disabled.Toggles == 0, "Practice mode must remain disabled.");

var cooldown = new FakeTimerManager { IgnoreToggle = true, MainRunning = true };
Check(SharpTimerCheckpointGuard.StopTimer(cooldown, player) == TimerStopResult.StillEnabled, "Ignored toggle must reject teleport.");
Check(cooldown.MainRunning && cooldown.Toggles == 1, "Do not claim a cooldown-blocked run was stopped.");
cooldown.IgnoreToggle = false;
Check(SharpTimerCheckpointGuard.StopTimer(cooldown, player) == TimerStopResult.Stopped, "Retry after cooldown must stop the timer.");
cooldown.Finish();
Check(cooldown.DatabaseWrites == 0, "Retry must still prevent a recorded finish.");

foreach (var failAt in new[] { "read", "toggle", "verify" })
{
    var failing = new FakeTimerManager { FailAt = failAt };
    try
    {
        SharpTimerCheckpointGuard.StopTimer(failing, player);
        throw new Exception("API failure must not return a successful stop.");
    }
    catch (InvalidOperationException) { checks++; }
}
RecoveryChecks.Run(Check, player);
ConfigChecks.Run(Check);
Console.WriteLine($"PASS: {checks} checks (SharpTimer cancellation/recovery, API errors, config migration, live overrides, and reload defaults).");

sealed class FakeTimerManager : ISharpTimerManager
{
    public bool Enabled = true, MainRunning, BonusRunning, Recording, IgnoreToggle;
    public string? FailAt;
    public int Toggles, Restarts, Reads, DatabaseWrites, RankingUpdates;
    public CCSPlayerController? LastPlayer;

    public bool IsTimerOn(CCSPlayerController player)
    {
        LastPlayer = player;
        Reads++;
        if (FailAt == "read" || (FailAt == "verify" && Reads > 1)) throw new InvalidOperationException("API unavailable.");
        return Enabled;
    }

    public void ToggleTimer(CCSPlayerController player)
    {
        LastPlayer = player;
        Toggles++;
        if (FailAt == "toggle") throw new InvalidOperationException("API unavailable.");
        if (IgnoreToggle) return;
        Enabled = !Enabled;
        MainRunning = BonusRunning = Recording = false;
    }

    public void Finish()
    {
        if (!Enabled || (!MainRunning && !BonusRunning)) return;
        DatabaseWrites++;
        RankingUpdates++;
    }

    public void RestartTimer(CCSPlayerController player) => Restarts++;
    public void BlockTimerCmd(CCSPlayerController player, bool block) => throw new NotSupportedException();
    public void BlockRespawnCmd(CCSPlayerController player, bool block) => throw new NotSupportedException();
}
