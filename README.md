# Movement HUD and Checkpoints
[![MIT License](https://img.shields.io/badge/license-MIT-blue.svg?style=flat)](http://choosealicense.com/licenses/mit/)<br>
A [CounterStrikeSharp](https://github.com/roflmuffin/counterstrikesharp) plugin for CS2 MG, Surf, and BHOP servers.

## HUD

![Movement HUD preview](images/image.png)

- Displays player speed and A/W/S/D/C/Jump key states.
- Shows the observed player's speed and key states while spectating.
- Toggle the HUD with `!panel`, `/panel`, or the console command `css_panel`.
- `mh_hud 0` disables the HUD server-wide. Players cannot override this with `!panel`; their personal preference is retained when it is enabled again.

## Checkpoints

- `!cp` saves a checkpoint. The four most recent checkpoints are kept.
- `!tp` returns to the latest checkpoint, `!tp1` to the previous one, and `!tp2` to the one before that.
- Also supports `/`, `.`, and `!!` prefixes. Console commands: `css_cp`, `css_tp`, `css_tp1`, and `css_tp2`.
- `!!cp`, `!!tp`, `!!tp1`, `!!tp2`, and `!!panel` show success or error feedback without posting the command in chat.
- All command feedback is visible only to the caller, including commands entered with `!`, `/`, `.`, `!!`, or through the console.
- Checkpoints store position, view angles, velocity, and whether the player was crouched.
- With `mh_checkpoint_crouch 1` (default), the player's posture must match the saved checkpoint: crouched checkpoints can only be loaded while crouching, and standing checkpoints can only be loaded while standing. Set it to `0` to disable both checks. Rejected teleports do not change timing.
- Saving and teleporting require the player to be alive and not spectating. Error messages appear in red.

### SharpTimer Integration

- Uses [SharpTimer/SharpTimer](https://github.com/SharpTimer/SharpTimer)'s `SharpTimerAPI` (`sharptimer:manager`).
- Controlled by `mh_sharptimer_guard` (default `1`). Set it to `0` to allow checkpoint teleports without touching SharpTimer or requiring its API. Disabling it also releases any temporary timer blocks created by MovementHud; cancelled runs remain cancelled.
- Before `tp`, `tp1`, or `tp2` teleports a player, timing is disabled through the API and checked again. This cancels main and bonus runs so the checkpoint-assisted run cannot finish, enter rankings, or be saved to the database. All supported chat prefixes and console commands use the same check.
- Saving a checkpoint with `cp` or requesting a nonexistent checkpoint does not affect timing. Already-disabled timing stays disabled.
- If the API is unavailable or SharpTimer ignores the toggle during command cooldown/replay, the teleport is cancelled. Retry after the cooldown ends.
- Returning to a main or bonus start zone automatically restores timing; leaving it starts a fresh run without `!r`. Teleporting straight to a finish still cannot save a result. Manually disabled timing stays disabled.
- Automatic recovery uses SharpTimer's public player state because the API's enable operation also respawns the player. Custom start triggers and configured coordinate zones are recognized by SharpTimer itself. If that state is incompatible, checkpoint teleporting is rejected.
- Install the **shared API supplied with your SharpTimer version** at `game/csgo/addons/counterstrikesharp/shared/SharpTimerAPI/SharpTimerAPI.dll`. Keep that shared DLL out of the MovementHud plugin directory.

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

`MaxSpeed` in `movementhud.json` sets the custom weapon movement speed limit (default `260`). Use `mh_speed 0` to restore the original weapon speed limits.

## Configuration

On first load, the plugin creates `movementhud.json` in its plugin directory. Existing files receive missing options automatically while retaining their current values:

```json
{
  "MaxSpeed": 260,
  "EnableSpeed": true,
  "EnableCheckpoints": true,
  "EnableHud": true,
  "EnableSharpTimerGuard": true,
  "RequireCrouchForCrouchedCheckpoint": true,
  "BlockPlayerKnife": true,
  "BlockPlayerBullets": true,
  "BlockPlayerGrenades": true,
  "BlockPlayerFlash": true,
  "CheckpointSound": "sounds/ui/buttonclick.vsnd",
  "CheckpointErrorSound": "sounds/ui/weapon_cant_buy.vsnd",
  "TeleportSound": "sounds/buttons/blip1.vsnd"
}
```

JSON values provide the startup defaults. Server console/RCON, `server.cfg`, and map configurations can override the switches immediately with `0` or `1`, without writing to the JSON file.

| JSON default | Server option | Enabled behavior |
| --- | --- | --- |
| `EnableSpeed` | `mh_speed` | Use custom weapon movement speed |
| `EnableCheckpoints` | `mh_checkpoints` | Allow checkpoint save/teleport commands |
| `EnableHud` | `mh_hud` | Allow HUD display |
| `EnableSharpTimerGuard` | `mh_sharptimer_guard` | Cancel checkpoint-assisted runs and restore timing at a start zone |
| `RequireCrouchForCrouchedCheckpoint` | `mh_checkpoint_crouch` | Require the saved crouching/standing posture when loading a checkpoint |
| `BlockPlayerKnife` | `mh_block_knife` | Block player knife damage |
| `BlockPlayerBullets` | `mh_block_bullets` | Block player bullet/Zeus damage |
| `BlockPlayerGrenades` | `mh_block_grenades` | Block player grenade/fire damage |
| `BlockPlayerFlash` | `mh_block_flash` | Block player flashbang blindness |

Run `css_movementhud_reload` from the server console after editing the JSON file. Reloading applies `MaxSpeed`, the switches, and the sound paths from the JSON file. Restarting the plugin also applies those defaults. Server and map commands can override them at runtime.

### Checkpoint sounds

The plugin plays a sound for the player who saves a checkpoint, fails a checkpoint command, or teleports to a checkpoint. Set the sound paths in `movementhud.json` and use `""` to mute an individual sound. For server or map overrides, use `mh_sound_checkpoint`, `mh_sound_checkpoint_error`, and `mh_sound_teleport`. To load the sample overrides, copy the file into the server's `game/csgo/cfg/` directory and run `exec movementhud.cfg` from the server console or `server.cfg`. [Sample movementhud.cfg](cfg/movementhud.cfg)

## Build and Install

Run `dotnet publish -c Release`, copy `bin/Release/net8.0/publish/MovementHud.dll` to `game/csgo/addons/counterstrikesharp/plugins/MovementHud/`, then restart the server or load the plugin.

The build uses the official .NET 8 SharpTimer API reference in `lib/SharpTimerAPI/`; it is not copied into the plugin output. See its README for source and license details. At runtime, use the shared API installed with SharpTimer.

Run `dotnet run --project tests/MovementHud.Tests -c Release` to check the checkpoint timer guard without a game server.
