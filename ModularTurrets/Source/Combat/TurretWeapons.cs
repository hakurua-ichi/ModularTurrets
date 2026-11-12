using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private Dictionary<string, int> muzzleIndices = new();
    private int missileLane;
    internal int TakeMissileLane()
    {
        int lane = missileLane & 7;
        missileLane = (lane + 1) & 7;
        return lane;
    }
    private readonly List<(Vector3 from, Vector3 to, int until, BeamVisualExtension visual)> beams = new();
    private static Material beamMaterial;
    public Vector3 MuzzleFor(Thing equipment)
    {
        return currentShotMuzzle is { } shot && ReferenceEquals(shot.Equipment, equipment) ? shot.Point : ResolveShotMuzzle(equipment).Point;
    }
    public void ShowBeam(Vector3 from, Vector3 to) => ShowBeam(from, to, null);
    public void ShowBeam(Vector3 from, Vector3 to, BeamVisualExtension visual)
    {
        if (visual != null && !visual.Valid) visual = null;
        if (beams.Count >= 24) beams.RemoveAt(0);
        beams.Add((from, to, Find.TickManager.TicksGame + (visual?.durationTicks ?? 5), visual));
    }
    private void DrawBeams()
    {
        beams.RemoveAll(b => b.until < Find.TickManager.TicksGame);
        beamMaterial ??= SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.25f, 0.85f, 1f));
        foreach (var beam in beams)
        {
            if (beam.visual != null)
                BeamVisuals.Draw(beam.from, beam.to, beam.visual, (beam.until - Find.TickManager.TicksGame) / (float)beam.visual.durationTicks);
            else
                GenDraw.DrawLineBetween(beam.from + Altitudes.AltIncVect * 4f, beam.to + Altitudes.AltIncVect * 4f, beamMaterial, 0.06f);
        }
    }
}
