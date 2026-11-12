using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

// A map-clipped cell boundary: a range covering the map outlines its edges
// instead of placing the entire circle outside the visible/playable area.
[StaticConstructorOnStartup]
internal sealed class LongRangeOverlay
{
    private static readonly Material Edge = CreateMaterial();
    private readonly List<Matrix4x4> edges = new();
    private Map cachedMap;
    private IntVec3 cachedCenter, cachedSize;
    private float cachedRange = -1f;

    private static Material CreateMaterial()
    {
        var source = MatLoader.LoadMat("Misc/FieldEdge");
        var material = MaterialPool.MatFrom((Texture2D)source.mainTexture, ShaderDatabase.Transparent, Color.white, 2900);
        material.mainTexture.wrapMode = TextureWrapMode.Clamp;
        material.enableInstancing = true;
        return material;
    }

    public void Draw(Map map, IntVec3 center, float range)
    {
        if (map != cachedMap || center != cachedCenter || map.Size != cachedSize || range != cachedRange)
        {
            cachedMap = map; cachedCenter = center; cachedSize = map.Size; cachedRange = range;
            edges.Clear();
            double radiusSquared = (double)range * range;
            bool Covered(int x, int z) => x >= 0 && z >= 0 && x < map.Size.x && z < map.Size.z
                && (double)(x - center.x) * (x - center.x) + (double)(z - center.z) * (z - center.z) <= radiusSquared;
            void Add(int x, int z, int direction) => edges.Add(Matrix4x4.TRS(
                new IntVec3(x, 0, z).ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays),
                new Rot4(direction).AsQuat, Vector3.one));
            // Rebuild only after range, position or map changes; draw only edges per frame.
            for (int z = 0; z < map.Size.z; z++)
                for (int x = 0; x < map.Size.x; x++)
                {
                    if (!Covered(x, z)) continue;
                    if (!Covered(x, z + 1)) Add(x, z, 0);
                    if (!Covered(x + 1, z)) Add(x, z, 1);
                    if (!Covered(x, z - 1)) Add(x, z, 2);
                    if (!Covered(x - 1, z)) Add(x, z, 3);
                }
        }
        if (edges.Count > 0) Graphics.DrawMeshInstanced(MeshPool.plane10, 0, Edge, edges);
    }
}
