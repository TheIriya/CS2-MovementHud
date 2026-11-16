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
            plugin.BlockPlayerKnife = new("mh_block_knife", "", true);
            plugin.BlockPlayerBullets = new("mh_block_bullets", "", true);
            plugin.BlockPlayerGrenades = new("mh_block_grenades", "", true);
            plugin.BlockPlayerFlash = new("mh_block_flash", "", true);
            var load = typeof(MovementHudPlugin).GetMethod("LoadConfig", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var path = Path.Combine(directory, "movementhud.json");

            File.WriteAllText(path, """{"MaxSpeed":315,"EnableHud":false,"RequireCrouchForCrouchedCheckpoint":false,"CustomSetting":"preserve"}""");
            load.Invoke(plugin, null);
            var upgraded = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            check(upgraded["MaxSpeed"]!.GetValue<int>() == 315 && upgraded["CustomSetting"]!.GetValue<string>() == "preserve",
                "Upgrading an existing config must preserve its values and unknown settings.");
            check(!plugin.EnableHud.Value && !plugin.RequireCrouchedTeleport.Value,
                "Existing false defaults must be applied, not replaced by built-in true defaults.");
            check(plugin.EnableSharpTimerGuard.Value && upgraded["EnableSharpTimerGuard"]!.GetValue<bool>(),
                "Missing options must use and persist their default values.");

            plugin.EnableHud.Value = true;
            plugin.EnableSharpTimerGuard.Value = false;
            check(plugin.EnableHud.Value && !plugin.EnableSharpTimerGuard.Value, "Console options must support live overrides.");
            check(File.ReadAllText(path).Contains("\"EnableHud\": false"), "Live overrides must not rewrite configured defaults.");
            load.Invoke(plugin, null);
            check(!plugin.EnableHud.Value && plugin.EnableSharpTimerGuard.Value, "Reload must reset live overrides to JSON defaults.");

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
            check(created.Count == 10 && switches.Values.All(read => read()), "A new config must include all nine enabled switches and MaxSpeed.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
