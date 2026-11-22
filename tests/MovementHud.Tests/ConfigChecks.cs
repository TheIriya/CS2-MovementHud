using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using MovementHud;

internal static class ConfigChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "MovementHud-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // BasePlugin needs a live server; initialize only the managed options used by LoadConfig.
            var plugin = (MovementHudPlugin)RuntimeHelpers.GetUninitializedObject(typeof(MovementHudPlugin));
            plugin.ModulePath = Path.Combine(directory, "MovementHud.dll");
            plugin.Enable260Speed = new("mh_speed", "", true);
            plugin.EnableCheckpoints = new("mh_checkpoints", "", true);
            plugin.EnableHud = new("mh_hud", "", true);
            plugin.EnableSharpTimerGuard = new("mh_sharptimer_guard", "", true);
            plugin.RequireCrouchedTeleport = new("mh_checkpoint_crouch", "", true);
            plugin.CheckpointSound = new("mh_sound_checkpoint", "", "");
            plugin.CheckpointErrorSound = new("mh_sound_checkpoint_error", "", "");
            plugin.TeleportSound = new("mh_sound_teleport", "", "");
            plugin.BlockPlayerKnife = new("mh_block_knife", "", true);
            plugin.BlockPlayerBullets = new("mh_block_bullets", "", true);
            plugin.BlockPlayerGrenades = new("mh_block_grenades", "", true);
            plugin.BlockPlayerFlash = new("mh_block_flash", "", true);
            var load = typeof(MovementHudPlugin).GetMethod("LoadConfig", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var path = Path.Combine(directory, "movementhud.json");
            var sounds = new (string Key, string Default, Func<string> Read)[]
            {
                (nameof(MovementHudConfig.CheckpointSound), "sounds/ui/buttonclick.vsnd", () => plugin.CheckpointSound.Value),
                (nameof(MovementHudConfig.CheckpointErrorSound), "sounds/ui/weapon_cant_buy.vsnd", () => plugin.CheckpointErrorSound.Value),
                (nameof(MovementHudConfig.TeleportSound), "sounds/buttons/blip1.vsnd", () => plugin.TeleportSound.Value)
            };

            File.WriteAllText(path, """{"MaxSpeed":315,"EnableHud":false,"RequireCrouchForCrouchedCheckpoint":false,"CustomSetting":"preserve"}""");
            load.Invoke(plugin, null);
            var upgraded = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            check(upgraded["MaxSpeed"]!.GetValue<int>() == 315 && upgraded["CustomSetting"]!.GetValue<string>() == "preserve",
                "Upgrading an existing config must preserve its values and unknown settings.");
            check(!plugin.EnableHud.Value && !plugin.RequireCrouchedTeleport.Value,
                "Existing false defaults must be applied, not replaced by built-in true defaults.");
            check(plugin.EnableSharpTimerGuard.Value && upgraded["EnableSharpTimerGuard"]!.GetValue<bool>(),
                "Missing options must use and persist their default values.");
            foreach (var sound in sounds)
                check(upgraded[sound.Key]?.GetValue<string>() == sound.Default && sound.Read() == sound.Default,
                    $"A missing {sound.Key} must use and persist {sound.Default}.");

            plugin.EnableHud.Value = true;
            plugin.EnableSharpTimerGuard.Value = false;
            check(plugin.EnableHud.Value && !plugin.EnableSharpTimerGuard.Value, "Console options must support live overrides.");
            check(File.ReadAllText(path).Contains("\"EnableHud\": false"), "Live overrides must not rewrite configured defaults.");
            load.Invoke(plugin, null);
            check(!plugin.EnableHud.Value && plugin.EnableSharpTimerGuard.Value, "Reload must reset live overrides to JSON defaults.");

            const string customSound = "sounds/custom/checkpoint.vsnd";
            upgraded[nameof(MovementHudConfig.CheckpointSound)] = customSound;
            upgraded[nameof(MovementHudConfig.CheckpointErrorSound)] = "";
            upgraded[nameof(MovementHudConfig.TeleportSound)] = null;
            File.WriteAllText(path, upgraded.ToJsonString());
            plugin.TeleportSound.Value = customSound;
            load.Invoke(plugin, null);
            check(plugin.CheckpointSound.Value == customSound, "Reload must apply a custom checkpoint sound.");
            check(plugin.CheckpointErrorSound.Value == "", "An empty sound in JSON must mute playback.");
            check(plugin.TeleportSound.Value == "sounds/buttons/blip1.vsnd", "A null sound in JSON must restore the default.");

            var switches = new Dictionary<string, Func<bool>>
            {
                ["EnableSpeed"] = () => plugin.Enable260Speed.Value,
                ["EnableCheckpoints"] = () => plugin.EnableCheckpoints.Value,
                ["EnableHud"] = () => plugin.EnableHud.Value,
                ["EnableSharpTimerGuard"] = () => plugin.EnableSharpTimerGuard.Value,
                ["RequireCrouchForCrouchedCheckpoint"] = () => plugin.RequireCrouchedTeleport.Value,
                ["BlockPlayerKnife"] = () => plugin.BlockPlayerKnife.Value,
                ["BlockPlayerBullets"] = () => plugin.BlockPlayerBullets.Value,
                ["BlockPlayerGrenades"] = () => plugin.BlockPlayerGrenades.Value,
                ["BlockPlayerFlash"] = () => plugin.BlockPlayerFlash.Value
            };
            foreach (var enabled in new[] { false, true })
            {
                foreach (var key in switches.Keys) upgraded[key] = enabled;
                File.WriteAllText(path, upgraded.ToJsonString());
                load.Invoke(plugin, null);
                foreach (var option in switches)
                    check(option.Value() == enabled, $"{option.Key} must initialize the matching live server option.");
            }

            File.Delete(path);
            load.Invoke(plugin, null);
            var created = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            check(created.Count == 10 + sounds.Length && switches.Values.All(read => read()),
                "A new config must include MaxSpeed and all switch and sound options.");
            foreach (var sound in sounds)
                check(created[sound.Key]?.GetValue<string>() == sound.Default && sound.Read() == sound.Default,
                    $"A new config must apply and store the default for {sound.Key}.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
