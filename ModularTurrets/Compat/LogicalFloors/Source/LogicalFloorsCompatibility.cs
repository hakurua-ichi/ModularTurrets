using System;
using global::LogicalFloors;
using global::LogicalFloors.API;
using Verse;

namespace ModularTurrets.Compat.LogicalFloors;

public sealed class LogicalFloorsCompatibility : IFloorCompatibility
{
    public bool TryGetFloor(Thing thing, out int floor)
    {
        try { floor = Floors.Of(thing); return true; }
        catch { floor = 0; return false; }
    }
    public bool TryEnter(Map map, int floor, out IDisposable scope)
    {
        scope = null;
        try { scope = FloorApi.EnterFloor(map, floor); return scope != null; }
        catch { return false; }
    }
    public bool TryShowFloor(Map map, int floor)
    {
        try { FloorApi.ShowFloor(map, floor); return FloorApi.GetViewFloor(map) == floor; }
        catch { return false; }
    }
    public bool TrySetFloor(Thing thing, int floor)
    {
        try { Floors.Set(thing, floor); return Floors.Of(thing) == floor; }
        catch { return false; }
    }
    public bool TryGetViewFloor(Map map, out int floor)
    {
        try { floor = FloorApi.GetViewFloor(map); return true; }
        catch { floor = 0; return false; }
    }
}
