using System;
using global::LogicalFloors;
using global::LogicalFloors.API;
using Verse;

namespace ModularTurrets.Compat.LogicalFloors;

[StaticConstructorOnStartup]
public static class Bootstrap
{
    static Bootstrap() => FloorCompatibility.Register(new LogicalFloorsCompatibility());
}
