using CounterStrikeSharp.API.Core;
using SharpTimerAPI;

namespace MovementHud;

internal enum TimerStopResult { AlreadyDisabled, Stopped, StillEnabled }

internal static class SharpTimerCheckpointGuard
{
    internal static TimerStopResult StopTimer(ISharpTimerManager manager, CCSPlayerController player)
    {
        // IsTimerOn reports whether timing is enabled, not whether a run is active.
        // Disable it even outside a run so a checkpoint cannot enter a ranked run.
        if (!manager.IsTimerOn(player))
            return TimerStopResult.AlreadyDisabled;

        // RestartTimer respawns the player; ToggleTimer clears both main and bonus runs.
        manager.ToggleTimer(player);
        return manager.IsTimerOn(player) ? TimerStopResult.StillEnabled : TimerStopResult.Stopped;
    }
}
