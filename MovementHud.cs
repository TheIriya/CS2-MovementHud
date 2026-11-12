using System.Drawing;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace MovementHud;

public sealed class MovementHudConfig
{
    public float MaxSpeed { get; set; } = 260.0f;
}

public sealed partial class MovementHudPlugin : BasePlugin
{
    private const int HudUpdateIntervalTicks = 6;
    private const int MaxCheckpoints = 4;
    private readonly Dictionary<ulong, List<Checkpoint>> _checkpoints = new();
    private readonly Dictionary<ulong, int> _jumpVisibleUntil = new();
    private readonly HashSet<ulong> _hudDisabled = new();
    private HashSet<ulong> _legsHidden = new();
    private readonly HashSet<(IntPtr Player, string Command)> _pendingChatCommands = new();
    private readonly Dictionary<IntPtr, (CCSWeaponBaseVData Data, float Primary, float Secondary)> _weaponSpeeds = new();
    private MovementHudConfig _config = new();
    private int _tick;

    public FakeConVar<bool> Enable260Speed = new(
        "mh_speed", "Enable the configured weapon movement speed", true);
    public FakeConVar<bool> EnableCheckpoints = new(
        "mh_checkpoints", "Enable checkpoint save and teleport commands", true);

    private string ConfigPath => Path.Combine(ModuleDirectory, "movementhud.json");
    private string HiddenLegsPath => Path.Combine(ModuleDirectory, "movementhud-hidelegs.json");

    public override string ModuleName => "Movement HUD and Checkpoint";
    public override string ModuleVersion => "1.5.0";
    public override string ModuleAuthor => "TheIriya";
    public override string ModuleDescription => "Speed, key press HUD, and checkpoint plugin";

    public override void Load(bool hotReload)
    {
        LoadConfig();
        LoadHiddenLegs();
        RegisterFakeConVars(this);
        RegisterPlayerProtection();
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterListener<Listeners.OnMapEnd>(RestoreWeaponSpeeds);
        AddCommand("css_cp", "保存检查点", (player, command) => OnPlayerCommand(player, command, "cp"));
        AddCommand("css_tp", "读取最近检查点", (player, command) => OnPlayerCommand(player, command, "tp"));
        AddCommand("css_tp1", "读取上一个检查点", (player, command) => OnPlayerCommand(player, command, "tp1"));
        AddCommand("css_tp2", "读取上上个检查点", (player, command) => OnPlayerCommand(player, command, "tp2"));
        AddCommand("css_panel", "开关移动 HUD", (player, command) => OnPlayerCommand(player, command, "panel"));
        AddCommand("css_hidelegs", "开关隐藏腿", (player, command) => OnPlayerCommand(player, command, "hidelegs"));
        AddCommand("css_movementhud_reload", "重载移动 HUD 配置", ReloadConfigCommand);
        AddCommandListener("say", OnSayCommand, HookMode.Pre);
        AddCommandListener("say_team", OnSayCommand, HookMode.Pre);
        HookUserMessage(118, OnChatMessage, HookMode.Pre); // CS_UM_SayText2
    }

    public override void Unload(bool hotReload)
    {
        RestoreWeaponSpeeds();
        foreach (var player in Utilities.GetPlayers())
        {
            if (IsUsable(player) && _legsHidden.Contains(player.SteamID))
                ApplyLegVisibility(player, false);
        }
    }

    private void OnTick()
    {
        _tick++;

        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsUsable(player))
                continue;

            ApplyMovementSpeed(player);
            if (_legsHidden.Contains(player.SteamID))
                ApplyLegVisibility(player, true);

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

    private void LoadHiddenLegs()
    {
        try
        {
            if (File.Exists(HiddenLegsPath))
                _legsHidden = JsonSerializer.Deserialize<HashSet<ulong>>(File.ReadAllText(HiddenLegsPath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.LogError(ex, "无法读取隐藏腿设置：{Path}", HiddenLegsPath);
        }
    }

    private bool SaveHiddenLegs()
    {
        try
        {
            var temporaryPath = HiddenLegsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_legsHidden));
            File.Move(temporaryPath, HiddenLegsPath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogError(ex, "无法保存隐藏腿设置：{Path}", HiddenLegsPath);
            return false;
        }
    }

    private static void ApplyLegVisibility(CCSPlayerController player, bool hidden)
    {
        if (!player.PawnIsAlive || player.PlayerPawn.Value is not { IsValid: true } pawn)
            return;

        var color = pawn.Render;
        var alpha = hidden ? 254 : 255;
        if (color.A == alpha)
            return;

        // CS2 hides the first-person legs when the player model is not fully opaque.
        pawn.Render = Color.FromArgb(alpha, color.R, color.G, color.B);
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
    }

    private void ToggleHideLegs(CCSPlayerController player)
    {
        if (!IsUsable(player) || player.IsBot || player.SteamID == 0)
            return;

        var hidden = _legsHidden.Add(player.SteamID);
        if (!hidden)
            _legsHidden.Remove(player.SteamID);

        if (!SaveHiddenLegs())
        {
            if (hidden)
                _legsHidden.Remove(player.SteamID);
            else
                _legsHidden.Add(player.SteamID);
            player.PrintToChat("\x02[隐藏腿] 设置保存失败，请稍后重试。");
            return;
        }

        ApplyLegVisibility(player, hidden);
        player.PrintToChat(hidden ? "\x04[隐藏腿] 已隐藏" : "\x04[隐藏腿] 已显示");
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

    private void OnPlayerCommand(CCSPlayerController? player, CommandInfo command, string name)
    {
        if (player is null)
        {
            command.ReplyToCommand("该命令只能由玩家使用。");
            return;
        }

        if (command.CallingContext == CommandCallingContext.Chat)
            QueueChatCommand(player, name);
        else
            ExecutePlayerCommand(player, name);
    }

    private void ToggleHud(CCSPlayerController player)
    {
        var enabled = _hudDisabled.Remove(player.SteamID);
        player.PrintToChat(enabled ? "\x04[HUD] 已开启" : "\x04[HUD] 已关闭");
        if (!enabled)
            _hudDisabled.Add(player.SteamID);
    }

    private HookResult OnSayCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !TryParseChatCommand(command.ArgString, out var name))
            return HookResult.Continue;

        QueueChatCommand(player, name);
        return HookResult.Stop;
    }

    private HookResult OnChatMessage(UserMessage message)
    {
        if (!TryParseChatCommand(message.ReadString("param2"), out var name))
            return HookResult.Continue;

        // Some chat paths bypass the say listener. Block the outgoing chat itself too.
        var player = Utilities.GetPlayerFromIndex(message.ReadInt("entityindex"));
        if (player is not null)
            QueueChatCommand(player, name);
        message.Recipients.Clear();
        return HookResult.Stop;
    }

    private static bool TryParseChatCommand(string text, out string name)
    {
        name = "";
        var lower = text.Trim().Trim('"').Trim().ToLowerInvariant();
        if (lower.Length < 2)
            return false;

        var doublePrefix = lower.StartsWith("!!", StringComparison.Ordinal);
        var prefixLength = doublePrefix ? 2 : 1;
        if (!doublePrefix && lower[0] is not ('!' or '/' or '.'))
            return false;

        name = lower[prefixLength..].Trim();
        return name is "panel" or "hidelegs" or "cp" or "tp" or "tp1" or "tp2";
    }

    private void QueueChatCommand(CCSPlayerController player, string name)
    {
        var key = (player.Handle, name);
        // The say hook, outgoing chat, and CSS chat dispatch may see the same command.
        if (!_pendingChatCommands.Add(key))
            return;

        Server.NextFrame(() =>
        {
            _pendingChatCommands.Remove(key);
            if (IsUsable(player))
                ExecutePlayerCommand(player, name);
        });
    }

    private void ExecutePlayerCommand(CCSPlayerController player, string name)
    {
        switch (name)
        {
            case "panel": ToggleHud(player); break;
            case "hidelegs": ToggleHideLegs(player); break;
            case "cp": SaveCheckpoint(player); break;
            case "tp": TeleportCheckpoint(player); break;
            case "tp1": TeleportCheckpoint(player, 1); break;
            case "tp2": TeleportCheckpoint(player, 2); break;
        }
    }

    private void SaveCheckpoint(CCSPlayerController player)
    {
        if (!EnableCheckpoints.Value)
        {
            player.PrintToChat("\x02[检查点] 此地图不能使用存点功能。");
            return;
        }

        if (!TryGetCheckpointPawn(player, out var pawn))
        {
            player.PrintToChat("\x02[检查点] 只有存活状态才能存点。");
            return;
        }

        var origin = pawn.AbsOrigin;
        var rotation = pawn.As<CCSPlayerPawn>().EyeAngles;
        var velocity = pawn.AbsVelocity;
        if (origin is null || rotation is null || velocity is null)
        {
            player.PrintToChat("\x02[检查点] 无法读取当前位置。");
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
        player.PrintToChat("\x04[检查点] 存点成功");
    }

    private void TeleportCheckpoint(CCSPlayerController player, int number = 0)
    {
        if (!EnableCheckpoints.Value)
        {
            player.PrintToChat("\x02[检查点] 此地图不能使用存点功能。");
            return;
        }

        if (!TryGetCheckpointPawn(player, out var pawn))
        {
            player.PrintToChat("\x02[检查点] 只有存活状态才能读点。");
            return;
        }

        if (!_checkpoints.TryGetValue(player.SteamID, out var saved) || saved.Count <= number)
        {
            player.PrintToChat(number > 0
                ? $"\x02[检查点] 第 {number} 个检查点不存在。"
                : "\x02[检查点] 没有检查点，请先使用 !cp。");
            return;
        }

        var index = saved.Count - 1 - number;
        if (index < 0)
        {
            player.PrintToChat($"\x02[检查点] 第 {number} 个回溯点不存在。");
            return;
        }

        var checkpoint = saved[index];
        pawn.Teleport(checkpoint.Position, checkpoint.Angles, checkpoint.Velocity);
        player.PrintToChat("\x04[检查点] 读点成功");
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
