using System.Collections;
using System.Reflection;
using CounterStrikeSharp.API.Core;
using SharpTimerAPI;

namespace MovementHud;

// SharpTimerAPI can toggle timing, but enabling it also respawns at the main start.
// Use the official plugin's public state to restore only our own temporary block,
// after SharpTimer has recognized a new main/bonus start-zone entry.
internal sealed class SharpTimerStartZoneRecovery
{
    private readonly object _plugin;
    private readonly object _timer;
    private readonly IDictionary _timers;
    private readonly int _slot;
    private readonly PropertyInfo _blocked;
    private readonly PropertyInfo _inStart;
    private readonly PropertyInfo _mainStart;
    private readonly PropertyInfo _bonusStart;
    private readonly Type _vectorType;
    private readonly MethodInfo _insideBox;
    private readonly MethodInfo _checkCoordinates;

    private SharpTimerStartZoneRecovery(object plugin, int slot)
    {
        _plugin = plugin;
        _slot = slot;
        _timers = (IDictionary)Field("playerTimers");
        _timer = _timers[slot] ?? throw new InvalidOperationException("SharpTimer player state is unavailable.");
        _blocked = BoolProperty(_timer, "IsTimerBlocked", writable: true);
        _inStart = BoolProperty(_timer, "inStartzone", writable: true);
        var zone = _timer.GetType().GetProperty("CurrentZoneInfo")?.GetValue(_timer)
            ?? throw new MissingMemberException("SharpTimer CurrentZoneInfo is unavailable.");
        _mainStart = BoolProperty(zone, "InMainMapStartZone", writable: true);
        _bonusStart = BoolProperty(zone, "InBonusStartZone", writable: true);
        // Validate fake-zone support before disabling timing, rather than failing later.
        foreach (var name in new[] { "useTriggers", "useTriggersAndFakeZones", "currentMapStartC1",
                     "currentMapStartC2", "currentBonusStartC1", "currentBonusStartC2", "totalBonuses", "Utils" })
            _ = Field(name);
        _vectorType = Field("currentMapStartC1").GetType();
        if (_vectorType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float) }) is null)
            throw new MissingMethodException("SharpTimer coordinate type is incompatible.");
        _insideBox = Field("Utils").GetType().GetMethod("IsVectorInsideBox",
            new[] { _vectorType, _vectorType, _vectorType, typeof(bool) })
            ?? throw new MissingMethodException("SharpTimer IsVectorInsideBox is unavailable.");
        _checkCoordinates = _plugin.GetType().GetMethod("CheckPlayerCoords", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(CCSPlayerController), _vectorType }, null)
            ?? throw new MissingMethodException("SharpTimer CheckPlayerCoords is unavailable.");
    }

    internal static SharpTimerStartZoneRecovery Capture(ISharpTimerManager manager, int slot)
    {
        // Resolve from the live capability provider's assembly, never a cached plugin DLL.
        var type = manager.GetType().Assembly.GetType("SharpTimer.SharpTimer", throwOnError: true)!;
        var plugin = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? throw new MissingMemberException("SharpTimer Instance is unavailable.");
        return new SharpTimerStartZoneRecovery(plugin, slot);
    }

    internal static SharpTimerStartZoneRecovery Capture(object plugin, int slot) => new(plugin, slot);

    internal bool IsCurrent => ReferenceEquals(Field("playerTimers"), _timers) && ReferenceEquals(_timers[_slot], _timer) &&
        (_plugin.GetType().GetField("Instance", BindingFlags.Public | BindingFlags.Static) is not { } instance ||
         ReferenceEquals(instance.GetValue(null), _plugin));
    internal bool IsBlocked => (bool)_blocked.GetValue(_timer)!;

    internal void ResetStartEntry()
    {
        // A teleport out of an old start must not count as returning to the start.
        _inStart.SetValue(_timer, false);
        var zone = _timer.GetType().GetProperty("CurrentZoneInfo")!.GetValue(_timer)!;
        _mainStart.SetValue(zone, false);
        _bonusStart.SetValue(zone, false);
    }

    internal bool HasEnteredTriggerStart()
    {
        var zone = _timer.GetType().GetProperty("CurrentZoneInfo")!.GetValue(_timer)!;
        return (bool)_mainStart.GetValue(zone)! || (bool)_bonusStart.GetValue(zone)!;
    }

    internal bool IsInsideFakeStart(float x, float y, float z)
    {
        var triggers = (bool)Field("useTriggers");
        if (triggers && !(bool)Field("useTriggersAndFakeZones")) return false;

        // Use SharpTimer's own geometry test, including its configured fake-zone height.
        bool Inside(object c1, object c2)
        {
            var vector = Activator.CreateInstance(_vectorType, x, y, z)!;
            if (c1.Equals(Activator.CreateInstance(c1.GetType())) ||
                c2.Equals(Activator.CreateInstance(c2.GetType()))) return false;
            return (bool)_insideBox.Invoke(Field("Utils"), new[] { vector, c1, c2, (object)true })!;
        }

        if (!triggers && Inside(Field("currentMapStartC1"), Field("currentMapStartC2"))) return true;
        var starts = (Array)Field("currentBonusStartC1");
        var ends = (Array)Field("currentBonusStartC2");
        foreach (var bonus in (int[])Field("totalBonuses"))
        {
            if (bonus > 0 && bonus < starts.Length && bonus < ends.Length &&
                starts.GetValue(bonus) is { } c1 && ends.GetValue(bonus) is { } c2 && Inside(c1, c2))
                return true;
        }
        return false;
    }

    internal void Restore()
    {
        if (IsCurrent) _blocked.SetValue(_timer, false);
    }

    internal void RestoreAtFakeStart(CCSPlayerController player, float speedX, float speedY, float speedZ)
    {
        if (!IsCurrent) return;
        Restore();
        // Run the native entry logic now, even if SharpTimer already ticked this frame.
        // This arms main starts and initializes bonus/replay state before a quick exit.
        try
        {
            _checkCoordinates.Invoke(_plugin, new[] { player, Activator.CreateInstance(_vectorType, speedX, speedY, speedZ)! });
        }
        catch
        {
            _blocked.SetValue(_timer, true);
            throw;
        }
    }

    private object Field(string name) => _plugin.GetType().GetField(name)?.GetValue(_plugin)
        ?? throw new MissingFieldException($"SharpTimer {name} is unavailable.");

    private static PropertyInfo BoolProperty(object target, string name, bool writable)
    {
        var property = target.GetType().GetProperty(name);
        if (property?.PropertyType != typeof(bool) || !property.CanRead || (writable && !property.CanWrite))
            throw new MissingMemberException($"SharpTimer {name} is incompatible.");
        return property;
    }
}
