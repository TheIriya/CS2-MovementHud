using System.Globalization;
using System.Numerics;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace MovementHud;

public sealed class MovementHudConfig
{
    public float MaxSpeed { get; set; } = 260.0f;
}

public sealed class MovementHudPlugin : BasePlugin
{
    private const int HudUpdateIntervalTicks = 6;
    private const int MaxCheckpoints = 4;
    private readonly Dictionary<ulong, List<Checkpoint>> _checkpoints = new();
    private readonly Dictionary<ulong, int> _jumpVisibleUntil = new();
    private readonly HashSet<ulong> _hudDisabled = new();
    private readonly Dictionary<IntPtr, (CCSWeaponBaseVData Data, float Primary, float Secondary)> _weaponSpeeds = new();
    private MovementHudConfig _config = new();
    private int _tick;

    public FakeConVar<bool> Enable260Speed = new(
        "mh_speed", "Enable the configured weapon movement speed", true);
    public FakeConVar<bool> EnableCheckpoints = new(
        "mh_checkpoints", "Enable checkpoint save and teleport commands", true);

    private string ConfigPath => Path.Combine(ModuleDirectory, "movementhud.json");

    public override string ModuleName => "Movement HUD and Checkpoint";
    public override string ModuleVersion => "1.3.2";
    public override string ModuleAuthor => "TheIriya";
    public override string ModuleDescription => "Speed, key press HUD, and checkpoint plugin";

    public override void Load(bool hotReload)
    {
        LoadConfig();
        RegisterFakeConVars(this);
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterListener<Listeners.OnMapEnd>(RestoreWeaponSpeeds);
        AddCommand("css_cp", "保存检查点", SaveCheckpointCommand);
        AddCommand("css_tp", "读取最近检查点", TeleportCheckpointCommand);
        AddCommand("css_tp1", "读取第一个检查点", TeleportCheckpoint1Command);
        AddCommand("css_tp2", "读取第二个检查点", TeleportCheckpoint2Command);
        AddCommand("css_panel", "开关移动 HUD", PanelCommand);
        AddCommand("css_movementhud_reload", "重载移动 HUD 配置", ReloadConfigCommand);
        AddCommandListener("say", OnSayCommand, HookMode.Pre);
        AddCommandListener("say_team", OnSayCommand, HookMode.Pre);
    }

    public override void Unload(bool hotReload) => RestoreWeaponSpeeds();

    private void OnTick()
    {
        _tick++;

        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsUsable(player))
                continue;

            ApplyMovementSpeed(player);

            if (_hudDisabled.Contains(player.SteamID))
                continue;

            var subject = GetObservedPlayer(player) ?? player;
            var pawn = subject.Pawn.Value;
            if (pawn is null)
                continue;

            var buttons = subject.Buttons;
            if (buttons.HasFlag(PlayerButtons.Jump))
                _jumpVisibleUntil[subject.SteamID] = _tick + HudUpdateIntervalTicks;

            if (_tick % HudUpdateIntervalTicks != 0)
                continue;

            var jumpPressed = _jumpVisibleUntil.TryGetValue(subject.SteamID, out var visibleUntil) && visibleUntil >= _tick;
            var speed = pawn.AbsVelocity.Length2D();
            var speedText = speed.ToString("N0", CultureInfo.InvariantCulture);
            var keyText = string.Join(" ", new[]
            {
                KeyState("A", buttons.HasFlag(PlayerButtons.Moveleft)),
                KeyState("W", buttons.HasFlag(PlayerButtons.Forward)),
                KeyState("S", buttons.HasFlag(PlayerButtons.Back)),
                KeyState("D", buttons.HasFlag(PlayerButtons.Moveright)),
                KeyState("C", buttons.HasFlag(PlayerButtons.Duck)),
                KeyState("Jump", jumpPressed)
            });
            var html = "<font color='#e8eef2'><b>Speed: </b></font>" +
                $"<font color='#4ea9ff'><b>{speedText}</b></font>" +
                "<font color='#9ba9b0'>u/s</font><br>" +
                $"<font size='16'>{keyText}</font>";

            player.PrintToCenterHtml(html, 1);
        }
    }

    private void LoadConfig()
    {
        Directory.CreateDirectory(ModuleDirectory);
        if (!File.Exists(ConfigPath))
            File.WriteAllText(ConfigPath, "{\n  \"MaxSpeed\": 260\n}\n");

        try
        {
            _config = JsonSerializer.Deserialize<MovementHudConfig>(File.ReadAllText(ConfigPath)) ?? new();
            if (!float.IsFinite(_config.MaxSpeed) || _config.MaxSpeed <= 0)
                _config.MaxSpeed = 260.0f;
        }
        catch
        {
            _config = new();
        }
    }

    private void ReloadConfigCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null)
            return;

        RestoreWeaponSpeeds();
        LoadConfig();
        command.ReplyToCommand("[HUD] 配置已重载");
    }

    private void ApplyMovementSpeed(CCSPlayerController player)
    {
        if (!Enable260Speed.Value)
        {
            RestoreWeaponSpeeds();
            return;
        }

        if (player.Pawn.Value is not { } pawn)
            return;

        var weapon = pawn.WeaponServices?.ActiveWeapon.Value;
        if (weapon is null || !weapon.IsValid)
            return;

        var data = weapon.As<CCSWeaponBase>().VData;
        if (data is null)
            return;

        var speeds = data.MaxSpeed.Values;
        _weaponSpeeds.TryAdd(data.Handle, (data, speeds[0], speeds[1]));
        speeds[0] = _config.MaxSpeed;
        speeds[1] = _config.MaxSpeed;
    }

    private void RestoreWeaponSpeeds()
    {
        foreach (var saved in _weaponSpeeds.Values)
        {
            var speeds = saved.Data.MaxSpeed.Values;
            speeds[0] = saved.Primary;
            speeds[1] = saved.Secondary;
        }

        _weaponSpeeds.Clear();
    }

    private void SaveCheckpointCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand("该命令只能由玩家使用。");
            return;
        }

        SaveCheckpoint(player);
    }

    private void TeleportCheckpointCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand("该命令只能由玩家使用。");
            return;
        }

        TeleportCheckpoint(player);
    }

    private void TeleportCheckpoint1Command(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand("该命令只能由玩家使用。");
            return;
        }

        TeleportCheckpoint(player, 1);
    }

    private void TeleportCheckpoint2Command(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand("该命令只能由玩家使用。");
            return;
        }

        TeleportCheckpoint(player, 2);
    }

    private void PanelCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand("该命令只能由玩家使用。");
            return;
        }

        ToggleHud(player);
    }

    private void ToggleHud(CCSPlayerController player, bool silent = false)
    {
        var enabled = _hudDisabled.Remove(player.SteamID);
        if (!silent)
            player.PrintToChat(enabled ? "\x04[HUD] 已开启" : "\x04[HUD] 已关闭");
        if (!enabled)
            _hudDisabled.Add(player.SteamID);
    }

    private HookResult OnSayCommand(CCSPlayerController? player, CommandInfo command)
    {
        var text = command.ArgString.Trim().Trim('"');
        if (player is null || text.Length < 2)
            return HookResult.Continue;

        var lower = text.ToLowerInvariant();
        var silent = lower.StartsWith("!!", StringComparison.Ordinal);
        var prefixLength = silent ? 2 : 1;
        if (!silent && lower[0] is not ('!' or '/' or '.'))
            return HookResult.Continue;
        var body = lower[prefixLength..];

        switch (body)
        {
            case "panel": ToggleHud(player, silent); return HookResult.Handled;
            case "cp": SaveCheckpoint(player, silent); return HookResult.Handled;
            case "tp": TeleportCheckpoint(player, 0, silent); return HookResult.Handled;
            case "tp1": TeleportCheckpoint(player, 1, silent); return HookResult.Handled;
            case "tp2": TeleportCheckpoint(player, 2, silent); return HookResult.Handled;
            default: return HookResult.Continue;
        }
    }

    private void SaveCheckpoint(CCSPlayerController player, bool silent = false)
    {
        if (!EnableCheckpoints.Value)
        {
            if (!silent) player.PrintToChat("\x02[检查点] 此地图不能使用存点功能。");
            return;
        }

        if (!TryGetCheckpointPawn(player, out var pawn))
        {
            if (!silent) player.PrintToChat("\x02[检查点] 只有存活状态才能存点。");
            return;
        }

        var origin = pawn.AbsOrigin;
        var rotation = pawn.As<CCSPlayerPawn>().EyeAngles;
        var velocity = pawn.AbsVelocity;
        if (origin is null || rotation is null || velocity is null)
        {
            if (!silent) player.PrintToChat("\x02[检查点] 无法读取当前位置。");
            return;
        }

        if (!_checkpoints.TryGetValue(player.SteamID, out var saved))
            _checkpoints[player.SteamID] = saved = new List<Checkpoint>();

        if (saved.Count == MaxCheckpoints)
            saved.RemoveAt(0);
        saved.Add(new Checkpoint(
            new Vector3(origin.X, origin.Y, origin.Z),
            new Vector3(rotation.X, rotation.Y, rotation.Z),
            new Vector3(velocity.X, velocity.Y, velocity.Z)));
        if (!silent) player.PrintToChat("\x04[检查点] 存点成功");
    }

    private void TeleportCheckpoint(CCSPlayerController player, int number = 0, bool silent = false)
    {
        if (!EnableCheckpoints.Value)
        {
            if (!silent) player.PrintToChat("\x02[检查点] 此地图不能使用存点功能。");
            return;
        }

        if (!TryGetCheckpointPawn(player, out var pawn))
        {
            if (!silent) player.PrintToChat("\x02[检查点] 只有存活状态才能读点。");
            return;
        }

        if (!_checkpoints.TryGetValue(player.SteamID, out var saved) || saved.Count <= number)
        {
            if (!silent) player.PrintToChat(number > 0
                ? $"\x02[检查点] 第 {number} 个检查点不存在。"
                : "\x02[检查点] 没有检查点，请先使用 !cp。");
            return;
        }

        var index = saved.Count - 1 - number;
        if (index < 0)
        {
            if (!silent) player.PrintToChat($"\x02[检查点] 第 {number} 个回溯点不存在。");
            return;
        }

        var checkpoint = saved[index];
        pawn.Teleport(checkpoint.Position, checkpoint.Angles, checkpoint.Velocity);
        if (!silent) player.PrintToChat("\x04[检查点] 读点成功");
    }

    private static bool TryGetCheckpointPawn(CCSPlayerController player, out CCSPlayerPawn pawn)
    {
        pawn = null!;
        if (!IsUsable(player) || !player.PawnIsAlive || player.Pawn.Value is not { } playerPawn ||
            playerPawn.ObserverServices?.ObserverTarget.Value is not null)
            return false;

        pawn = playerPawn.As<CCSPlayerPawn>();
        return true;
    }

    private void OnClientDisconnect(int slot)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player is null)
            return;

        _checkpoints.Remove(player.SteamID);
        _jumpVisibleUntil.Remove(player.SteamID);
        _hudDisabled.Remove(player.SteamID);
    }

    private static CCSPlayerController? GetObservedPlayer(CCSPlayerController player)
    {
        var targetIndex = player.Pawn.Value?.ObserverServices?.ObserverTarget.Value?.Index;
        if (targetIndex is null)
            return null;

        return Utilities.GetPlayers().FirstOrDefault(candidate =>
            candidate.Slot != player.Slot && candidate.Pawn.Value?.Index == targetIndex);
    }

    private static bool IsUsable(CCSPlayerController player) =>
        player.IsValid && player.Connected == PlayerConnectedState.PlayerConnected;

    private static string KeyState(string label, bool pressed) => pressed
        ? $"<font color='#55d889'><b>{label}</b></font>"
        : $"<font color='#6d777c'>{label}</font>";

    private sealed record Checkpoint(Vector3 Position, Vector3 Angles, Vector3 Velocity);
}
