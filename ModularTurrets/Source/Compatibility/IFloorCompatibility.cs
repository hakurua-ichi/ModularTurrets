using System;
using Verse;

namespace ModularTurrets;

public interface IFloorCompatibility
{
    bool TryGetFloor(Thing thing, out int floor);
    bool TryEnter(Map map, int floor, out IDisposable scope);
    bool TryShowFloor(Map map, int floor);
    bool TrySetFloor(Thing thing, int floor);
    bool TryGetViewFloor(Map map, out int floor);
}
