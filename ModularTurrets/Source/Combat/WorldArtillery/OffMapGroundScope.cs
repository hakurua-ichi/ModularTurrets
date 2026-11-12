using System;
using Verse;

namespace ModularTurrets;

// Optional floor assemblies own external APIs. Ground is the only supported
// remote destination; unsupported operations must never use another floor.
internal static class OffMapGroundScope
{
    internal static bool TryEnter(Map map, out IDisposable scope)
        => FloorCompatibility.TryEnter(map, 0, out scope);
    internal static bool ShowGround(Map map)
        => FloorCompatibility.TryShowFloor(map, 0)
            && FloorCompatibility.TryGetViewFloor(map, out int floor) && floor == 0;
    internal static bool SetGround(Thing thing)
        => FloorCompatibility.TrySetFloor(thing, 0)
            && FloorCompatibility.TryGetFloor(thing, out int floor) && floor == 0;
}
