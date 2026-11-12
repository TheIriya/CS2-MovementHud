# Movement HUD and Checkpoints
[![MIT License](https://img.shields.io/badge/license-MIT-blue.svg?style=flat)](http://choosealicense.com/licenses/mit/)<br>
A [CounterStrikeSharp](https://github.com/roflmuffin/counterstrikesharp) plugin for CS2 MG, Surf, and BHOP servers.

## HUD

![Movement HUD preview](images/image.png)

- Displays player speed and A/W/S/D/C/Jump key states.
- Shows the observed player's speed and key states while spectating.
- Toggle the HUD with `!panel`, `/panel`, or the console command `css_panel`.

## Checkpoints

- `!cp` saves a checkpoint. The four most recent checkpoints are kept.
- `!tp` returns to the latest checkpoint, `!tp1` to the previous one, and `!tp2` to the one before that.
- Also supports `/`, `.`, and `!!` prefixes. Console commands: `css_cp`, `css_tp`, `css_tp1`, and `css_tp2`.
- `!!cp`, `!!tp`, `!!tp1`, `!!tp2`, and `!!panel` show success or error feedback without posting the command in chat.
- All command feedback is visible only to the caller, including commands entered with `!`, `/`, `.`, `!!`, or through the console.
- Checkpoints store position, view angles, and velocity.
- Saving and teleporting require the player to be alive and not spectating. Error messages appear in red.

## Hide Legs

- Toggle first-person leg visibility with `!hidelegs`, `/hidelegs`, `.hidelegs`, `!!hidelegs`, or the console command `css_hidelegs`.
- `!!hidelegs` shows feedback only to the caller without posting the command in chat.
- Settings are saved by SteamID in `movementhud-hidelegs.json` in the plugin directory and persist across reconnects, respawns, map changes, and server restarts.
- Run the command again to show your legs and update the saved setting. You can also toggle it while dead or spectating; the setting takes effect when you are alive again.

## Player Damage Protection

Four independent server options provide immunity to player attacks. All default to `1` (enabled). Set an option to `0` to allow that attack type. Changes take effect immediately without reloading the plugin.

| Option | Blocks when set to `1` |
| --- | --- |
| `mh_block_knife` | Player knife damage |
| `mh_block_bullets` | Player bullet, shotgun, and Zeus x27 (taser) damage |
| `mh_block_grenades` | Player grenade damage, including HE explosions, Molotov/incendiary fire, and grenade impacts |
| `mh_block_flash` | Blindness from player-thrown flashbangs |

- Protects both T and CT players from enemies, teammates, and their own grenades or flashes.
- Checks the actual damage source and grenade thrower/owner. Map `trigger_hurt`, map explosions, electrical hazards, falling, drowning, and other environmental damage remain effective, even if a map entity credits a player as the attacker.
- Map-generated flashes without a player thrower remain effective.
- Configure these options in the server console, `server.cfg`, or a map-specific configuration. For example, `mh_block_bullets 0` allows gunfire and Zeus damage while leaving the other protections enabled.

## Movement Speed

On first load, the plugin creates `movementhud.json` to configure movement speed:

```json
{
  "MaxSpeed": 260
}
```

- `MaxSpeed`: the weapon movement speed limit used when custom speed is enabled. Default: `260`.

Set the following options in the server console or `server.cfg`:

- `mh_speed 1`: enables custom weapon movement speed (default). Set to `0` to restore the original weapon speed limits.
- `mh_checkpoints 1`: enables checkpoint saving and teleporting (default). Set to `0` to disable these commands.

After changing `MaxSpeed`, run `css_movementhud_reload` from the server console to reload the configuration.

## Build and Install

Run `dotnet publish -c Release`, copy `bin/Release/net8.0/publish/MovementHud.dll` to `game/csgo/addons/counterstrikesharp/plugins/MovementHud/`, then restart the server or load the plugin.
