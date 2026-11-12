namespace ModularTurrets;

internal static class AssemblyWorkBalance
{
    internal const int Version = 1;

    // Saved orders keep their progress ratio and migrate only once, on load.
    internal static void Migrate(ref int version, ref float total, ref float left)
    {
        if (version >= Version) return;
        if (float.IsFinite(total) && total >= 0f) total *= 0.5f;
        if (float.IsFinite(left) && left >= 0f) left *= 0.5f;
        version = Version;
    }
}
