using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using SharpTimerAPI;

namespace MovementHud;

public sealed partial class MovementHudPlugin
{
    private readonly Dictionary<int, (nint Player, SharpTimerStartZoneRecovery Recovery)> _timerRecoveries = new();

    private void OnSharpTimerGuardChanged(object? sender, bool enabled)
    {
        if (!enabled)
            ClearSharpTimerRecoveries();
    }

    private bool PrepareSharpTimerForTeleport(CCSPlayerController player, out bool timerStopped)
    {
        timerStopped = false;
        try
        {
            // Resolve on each teleport so a SharpTimer reload does not leave a stale manager.
            var manager = ISharpTimerManager.Capability.Get();
            if (manager is null)
            {
                player.PrintToChat("\x02[检查点] SharpTimerAPI 未就绪，已取消读点，避免成绩被错误记录。");
                return false;
            }

            _timerRecoveries.TryGetValue(player.Slot, out var pending);
            var recovery = pending.Player == player.Handle && pending.Recovery?.IsCurrent == true
                ? pending.Recovery : null;
            // Check compatibility before stopping the timer. Manual practice mode stays off.
            if (manager.IsTimerOn(player))
                recovery = SharpTimerStartZoneRecovery.Capture(manager, player.Slot);

            var result = SharpTimerCheckpointGuard.StopTimer(manager, player);
            if (result == TimerStopResult.StillEnabled)
            {
                // ToggleTimer can be ignored during SharpTimer's command cooldown/replay.
                // Never teleport until the API confirms that timing is disabled.
                player.PrintToChat("\x02[检查点] 计时未能关闭（可能处于指令冷却或回放中），已取消读点，请稍后重试。");
                return false;
            }

            if (recovery is not null)
            {
                recovery.ResetStartEntry();
                _timerRecoveries[player.Slot] = (player.Handle, recovery);
            }
            timerStopped = recovery is not null;
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SharpTimerAPI 无法确认玩家 {SteamID} 的计时已关闭，已取消读点。", player.SteamID);
            player.PrintToChat("\x02[检查点] 无法确认计时已关闭，已取消读点，请联系管理员检查 SharpTimerAPI。");
            return false;
        }
    }

    private void UpdateSharpTimerRecoveries()
    {
        foreach (var slot in _timerRecoveries.Keys.ToArray())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is null || !IsUsable(player) || player.Handle != _timerRecoveries[slot].Player)
            {
                _timerRecoveries.Remove(slot);
                continue;
            }
            TryRecoverSharpTimer(player);
        }
    }

    private void TryRecoverSharpTimer(CCSPlayerController player)
    {
        if (!_timerRecoveries.TryGetValue(player.Slot, out var pending) || pending.Player != player.Handle)
            return;
        try
        {
            var recovery = pending.Recovery;
            if (!recovery.IsCurrent || !recovery.IsBlocked)
            {
                _timerRecoveries.Remove(player.Slot);
                return;
            }
            if (!player.PawnIsAlive) return;

            var position = player.PlayerPawn.Value?.AbsOrigin;
            if (recovery.HasEnteredTriggerStart())
            {
                recovery.Restore();
                _timerRecoveries.Remove(player.Slot);
            }
            else if (position is not null && recovery.IsInsideFakeStart(position.X, position.Y, position.Z))
            {
                var velocity = player.PlayerPawn.Value!.AbsVelocity;
                recovery.RestoreAtFakeStart(player, velocity.X, velocity.Y, velocity.Z);
                _timerRecoveries.Remove(player.Slot);
            }
        }
        catch (Exception ex)
        {
            // Leave timing blocked if upstream state is incompatible; never allow a CP finish.
            _timerRecoveries.Remove(player.Slot);
            Logger.LogError(ex, "无法在起点恢复玩家 {SteamID} 的 SharpTimer 计时。", player.SteamID);
            player.PrintToChat("\x02[检查点] 无法自动恢复计时，请使用 !r 重新开始并联系管理员。");
        }
    }

    private HookResult OnSharpTimerStartTouch(CEntityIOOutput output, string name,
        CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
    {
        // Post runs after SharpTimer recognizes custom, main, and bonus trigger starts.
        // Recover here as well as on tick, so a rapid entry/exit needs no extra frame.
        if (_timerRecoveries.Count != 0 && activator.IsValid && activator.DesignerName == "player")
        {
            var player = activator.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
            if (player is not null && IsUsable(player)) TryRecoverSharpTimer(player);
        }
        return HookResult.Continue;
    }

    private void ClearSharpTimerRecoveries()
    {
        foreach (var pending in _timerRecoveries.Values)
        {
            try { pending.Recovery.Restore(); }
            catch (Exception ex) { Logger.LogError(ex, "无法清理 SharpTimer 临时计时开关。"); }
        }
        _timerRecoveries.Clear();
    }
}
