using System;
using Verse;

namespace ModularTurrets;

// Core has one ordinary floor. Optional assemblies supply floor semantics;
// an active floor mod with a missing adapter must not become ordinary floor 0.
[StaticConstructorOnStartup]
public static class FloorCompatibility
{
    private static IFloorCompatibility adapter;
    private static readonly bool required = ModLister.AnyModActiveNoSuffix(new[] { "moont.dd.logicalfloors" });
    public static void Register(IFloorCompatibility implementation)
    {
        if (implementation == null) throw new ArgumentNullException(nameof(implementation));
        if (adapter != null && !ReferenceEquals(adapter, implementation))
            throw new InvalidOperationException("A modular turret floor adapter is already registered.");
        adapter = implementation;
    }
    public static bool TryGetFloor(Thing thing, out int floor)
    {
        if (adapter != null) return adapter.TryGetFloor(thing, out floor);
        floor = 0; return !required;
    }
    public static bool TryEnter(Map map, int floor, out IDisposable scope)
    {
        if (adapter != null) return adapter.TryEnter(map, floor, out scope);
        scope = null; return !required && floor == 0;
    }
    public static bool TryShowFloor(Map map, int floor)
        => adapter != null ? adapter.TryShowFloor(map, floor) : !required && floor == 0;
    public static bool TrySetFloor(Thing thing, int floor)
        => adapter != null ? adapter.TrySetFloor(thing, floor) : !required && floor == 0;
    public static bool TryGetViewFloor(Map map, out int floor)
    {
        if (adapter != null) return adapter.TryGetViewFloor(map, out floor);
        floor = 0; return !required;
    }
}
