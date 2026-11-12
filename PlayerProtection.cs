using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;

namespace MovementHud;

public sealed partial class MovementHudPlugin
{
    public FakeConVar<bool> BlockPlayerKnife = new(
        "mh_block_knife", "Block player knife damage to T/CT players (1 = immune, 0 = normal)", true);
    public FakeConVar<bool> BlockPlayerBullets = new(
        "mh_block_bullets", "Block player bullet, shotgun and Zeus damage to T/CT players (1 = immune, 0 = normal)", true);
    public FakeConVar<bool> BlockPlayerGrenades = new(
        "mh_block_grenades", "Block player grenade and fire damage to T/CT players (1 = immune, 0 = normal)", true);
    public FakeConVar<bool> BlockPlayerFlash = new(
        "mh_block_flash", "Block player flashbang blindness for T/CT players (1 = immune, 0 = normal)", true);

    private void RegisterPlayerProtection()
    {
        RegisterListener<Listeners.OnPlayerTakeDamagePre>(OnPlayerDamagePre);
        RegisterEventHandler<EventPlayerBlind>(OnPlayerBlind, HookMode.Post);
    }

    private HookResult OnPlayerDamagePre(CCSPlayerPawn victim, CTakeDamageInfo info)
    {
        if (!IsCombatPlayer(victim) || info.Inflictor.Value is not { IsValid: true } inflictor)
            return HookResult.Continue;

        var kind = GetPlayerDamageKind(inflictor.DesignerName, info.BitsDamageType);
        if (!IsDamageBlocked(kind))
            return HookResult.Continue;

        // Map damage may credit a player. Verify the actual inflictor as well.
        var playerSource = kind == PlayerDamageKind.Grenade
            ? HasPlayerGrenadeSource(inflictor)
            : IsCombatPlayer(info.Attacker.Value) &&
              (IsCombatPlayer(inflictor) || IsCombatPlayer(inflictor.OwnerEntity.Value));
        return playerSource ? HookResult.Handled : HookResult.Continue;
    }

    private bool IsDamageBlocked(PlayerDamageKind kind) => kind switch
    {
        PlayerDamageKind.Knife => BlockPlayerKnife.Value,
        PlayerDamageKind.Bullet => BlockPlayerBullets.Value,
        PlayerDamageKind.Grenade => BlockPlayerGrenades.Value,
        _ => false
    };

    private static PlayerDamageKind GetPlayerDamageKind(string source, DamageTypes_t damageType)
    {
        // Grenade impacts can use melee flags; classify projectiles before weapons.
        if (IsGrenadeProjectile(source) || source == "inferno")
            return PlayerDamageKind.Grenade;

        // Keep trigger_hurt, env_explosion and other map entities effective regardless
        // of their damage flags, owner or credited attacker (including shock damage).
        if (source is not ("player" or "player_pawn") && !source.StartsWith("weapon_", StringComparison.Ordinal))
            return PlayerDamageKind.None;

        // Zeus may report the weapon itself or the firing pawn as its inflictor.
        if (source == "weapon_taser" ||
            (damageType & (DamageTypes_t.DMG_BULLET | DamageTypes_t.DMG_BUCKSHOT | DamageTypes_t.DMG_SHOCK)) != 0)
            return PlayerDamageKind.Bullet;
        if ((damageType & (DamageTypes_t.DMG_SLASH | DamageTypes_t.DMG_CLUB)) != 0)
            return PlayerDamageKind.Knife;
        return PlayerDamageKind.None;
    }

    private static bool IsGrenadeProjectile(string source) => source is
        "hegrenade_projectile" or "flashbang_projectile" or "molotov_projectile" or
        "incgrenade_projectile" or "smokegrenade_projectile" or "decoy_projectile" or
        "tagrenade_projectile";

    private static bool HasPlayerGrenadeSource(CBaseEntity source)
    {
        if (IsGrenadeProjectile(source.DesignerName))
        {
            var grenade = source.As<CBaseGrenade>();
            return IsCombatPlayer(grenade.Thrower.Value) || IsCombatPlayer(grenade.OriginalThrower.Value);
        }

        return source.DesignerName == "inferno" && IsCombatPlayer(source.OwnerEntity.Value);
    }

    private HookResult OnPlayerBlind(EventPlayerBlind @event, GameEventInfo info)
    {
        if (!BlockPlayerFlash.Value || @event.Userid is not { IsValid: true } victim ||
            victim.PlayerPawn.Value is not { IsValid: true } pawn || !IsCombatPlayer(pawn))
            return HookResult.Continue;

        var flash = Utilities.GetEntityFromIndex<CBaseEntity>(@event.Entityid);
        if (flash is not { IsValid: true, DesignerName: "flashbang_projectile" } ||
            !HasPlayerGrenadeSource(flash))
            return HookResult.Continue;

        // Clear this flash without leaving FlashMaxAlpha at zero: later map flashes
        // must still be able to blind this player.
        pawn.BlindUntilTime = 0;
        pawn.BlindStartTime = 0;
        pawn.FlashDuration = 0;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_flFlashDuration");
        return HookResult.Continue;
    }

    private static bool IsCombatPlayer(CBaseEntity? entity) =>
        entity is { IsValid: true, TeamNum: 2 or 3, DesignerName: "player" or "player_pawn" or "cs_player_controller" };

    private enum PlayerDamageKind { None, Knife, Bullet, Grenade }
}
