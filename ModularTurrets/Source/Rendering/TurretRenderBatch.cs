using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Verse;
using MeshDrawCommand = Waylith.Rendering.MeshDrawCommand;
using MeshDrawSubmission = Waylith.Rendering.MeshDrawSubmission;
using ShaderRegistry = Waylith.Rendering.ShaderRegistry;

namespace ModularTurrets;

/// <summary>Shares unchanged cutout sprites across the current dynamic draw pass.</summary>
[StaticConstructorOnStartup]
internal static class TurretRenderBatch
{
    private sealed class PlaneBatch
    {
        private readonly List<Matrix4x4> matrices = new();
        internal bool checkedScope, eligible;
        internal float depthBin;
        internal bool Pending => matrices.Count != 0;
        private Mesh mesh;
        private Material material;
        private int layer;
        private long firstOrder;

        internal bool Add(Mesh nextMesh, Matrix4x4 matrix, Material nextMaterial, int nextLayer)
        {
            // Different keys are barriers. Bound each run to DrawBatch's limit.
            if (Pending && (mesh != nextMesh || material != nextMaterial || layer != nextLayer || matrices.Count == 1023)) Flush();
            if (faulted) return false;
            bool first = !Pending;
            if (first)
            {
                mesh = nextMesh; material = nextMaterial; layer = nextLayer;
                firstOrder = MeshDrawSubmission.ReserveSubmissionOrderRange(1);
            }
            matrices.Add(matrix);
            if (first) pendingPlanes++;
            return true;
        }

        internal void Flush(bool draw = true)
        {
            if (!Pending) return;
            bool previousSubmission = internalSubmission;
            internalSubmission = true;
            try
            {
                if (!draw) return;
                Material replacement = null;
                if (matrices.Count > 1 && !faulted)
                {
                    try { replacement = InstancedMaterialFor(material); }
                    catch (Exception exception) { DisableAfterFailure(exception); }
                }
                if (replacement == null) { ReplayOrdinary(); return; }
                ownEnqueueEntered = false;
                try
                {
                    using (MeshDrawSubmission.UseSubmissionOrder(firstOrder))
                        Graphics.DrawMeshInstanced(mesh, 0, replacement, matrices, null,
                            UnityEngine.Rendering.ShadowCastingMode.On, true, layer, null);
                }
                catch (Exception exception)
                {
                    DisableAfterFailure(exception);
                    // Before Enqueue nothing was committed. After entry, another
                    // patch could throw after commit: never duplicate that run.
                    if (!ownEnqueueEntered) ReplayOrdinary();
                }
            }
            finally
            {
                matrices.Clear();
                pendingPlanes--;
                internalSubmission = previousSubmission;
            }
        }

        private void ReplayOrdinary()
        {
            for (int i = 0; i < matrices.Count; i++)
            {
                try
                {
                    // Reuse the run's reserved position; sequence breaks ties.
                    using (MeshDrawSubmission.UseSubmissionOrder(firstOrder))
                        Graphics.DrawMesh(mesh, matrices[i], material, layer);
                }
                catch (Exception exception) { DisableAfterFailure(exception); }
            }
        }
    }

    private static readonly Dictionary<float, PlaneBatch> planes = new();
    private static readonly Dictionary<Material, Material> instancedMaterials = new();
    private static readonly Dictionary<Material, int> materialVersions = new();
    private static AccessTools.FieldRef<DynamicDrawManager, Map> ownerMap;
    private static AccessTools.FieldRef<bool> hasScopedOrder;
    private static bool warnedShader, warnedFailure, faulted, retryRequested, patchesReady;
    private static bool active, scopeEnabled, scopePrepared;
    [ThreadStatic] private static bool internalSubmission, ownEnqueueEntered;
    private static int depth, pendingPlanes, scopeThread;
    private static Matrix4x4 view;
    private static Map scopeMap;
    private static int mapSizeX, mapSizeZ;
    internal static bool Enabled = true;

    // Applied next scope, independently of the helper's runtime Enabled override.
    internal static void RequestRetry() => retryRequested = true;

    private static Material InstancedMaterialFor(Material source)
    {
        if (source.shader != ShaderDatabase.Cutout || source.passCount != 1
            || source.renderQueue != ShaderDatabase.Cutout.renderQueue) return null;
        if (!ShaderRegistry.TryResolve("Cutout", out var original)
            || !InstancedCutoutShader.TryGet(original, out var replacement, out var replacementWrapper)
            || original.VertexShaderFileName != "SceneDefault.vert.glsl"
            || replacement.VertexShaderFileName != "Transparent.vert.glsl"
            || original.FragmentShaderFileName != "Cutout.frag.glsl"
            || !InstancedCutoutShader.HasExpectedFragment(replacement)
            || original.HasErrors || replacement.HasErrors || !HasInstanceTransforms(replacement)
            || replacement.PipelineState != original.PipelineState
            || replacement.DefaultRenderQueue != original.DefaultRenderQueue
            || replacement.FlipTextureST != original.FlipTextureST)
        {
            if (!warnedShader)
            {
                warnedShader = true;
                Log.Warning("[Modular Turrets] Instanced sprite shader unavailable or changed; using ordinary sprites.");
            }
            return null;
        }
        int version = source.engineMaterial.Version;
        if (instancedMaterials.TryGetValue(source, out Material cached))
        {
            if (materialVersions[source] != version || cached.shader != replacementWrapper)
            {
                cached.shader = replacementWrapper;
                // Synchronize shared originals without changing them. The setter
                // also removes keywords absent from the source; Copy only adds.
                cached.CopyPropertiesFromMaterial(source);
                cached.shaderKeywords = source.shaderKeywords;
                cached.renderQueue = source.renderQueue;
                cached.engineMaterial.WrapModeOverride = source.engineMaterial.WrapModeOverride;
                materialVersions[source] = version;
            }
            return cached;
        }
        Material clone = new Material(source)
        {
            name = source.name + "_MTInstanced", shader = replacementWrapper,
            renderQueue = source.renderQueue, enableInstancing = true
        };
        clone.engineMaterial.WrapModeOverride = source.engineMaterial.WrapModeOverride;
        instancedMaterials.Add(source, clone);
        materialVersions.Add(source, version);
        return clone;
    }

    private static bool HasInstanceTransforms(Waylith.Rendering.Shader shader)
    {
        foreach (var slot in shader.MaterialLayout.StorageBufferSlots)
            if (slot.Binding == 2 && slot.PropertyName == "_InstanceTransforms") return true;
        return false;
    }

    static TurretRenderBatch()
    {
        try { ApplyPatch(); }
        catch (Exception exception) { DisableAfterFailure(exception); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyPatch()
    {
        // Resolve private fields inside guarded setup, never a field initializer.
        ownerMap = AccessTools.FieldRefAccess<DynamicDrawManager, Map>("map");
        hasScopedOrder = AccessTools.StaticFieldRefAccess<bool>(AccessTools.Field(typeof(MeshDrawSubmission), "HasScopedOrder"));
        var original = AccessTools.Method(typeof(DynamicDrawManager), nameof(DynamicDrawManager.DrawDynamicThings));
        var enqueue = AccessTools.Method(typeof(MeshDrawSubmission), nameof(MeshDrawSubmission.Enqueue),
            new[] { typeof(MeshDrawCommand).MakeByRefType() });
        if (original == null || enqueue == null) throw new MissingMethodException("Dynamic draw submission API");
        var harmony = new Harmony("local.modularturrets.sprite-batching");
        harmony.Patch(enqueue, prefix: new HarmonyMethod(typeof(TurretRenderBatch), nameof(BeforeEnqueue)));
        harmony.Patch(original,
            prefix: new HarmonyMethod(typeof(TurretRenderBatch), nameof(BeginForManager)),
            postfix: new HarmonyMethod(typeof(TurretRenderBatch), nameof(End)),
            finalizer: new HarmonyMethod(typeof(TurretRenderBatch), nameof(Cleanup)));
        patchesReady = true;
    }

    private static void BeginForManager(DynamicDrawManager __instance, out bool __state)
    {
        bool ownsCurrentMap = false;
        try { ownsCurrentMap = ownerMap(__instance) == Find.CurrentMap; }
        catch (Exception exception) { DisableAfterFailure(exception); }
        BeginScope(ownsCurrentMap, out __state);
    }

    // The actual prefix also verifies ownerMap. Keep the isolated harness API.
    private static void Begin(out bool __state) => BeginScope(true, out __state);

    private static void BeginScope(bool ownsCurrentMap, out bool __state)
    {
        __state = true;
        depth++;
        active = false;
        try
        {
            if (depth == 1)
            {
                Flush(false);
                foreach (PlaneBatch plane in planes.Values) plane.checkedScope = false;
                scopeEnabled = false;
                scopePrepared = false;
                scopeThread = Environment.CurrentManagedThreadId;
                if (retryRequested && ownsCurrentMap)
                {
                    retryRequested = false;
                    faulted = warnedFailure = false;
                    InstancedCutoutShader.RequestRetry();
                }
                scopeMap = Find.CurrentMap;
                scopeEnabled = patchesReady && !faulted && Enabled && ModularTurretsMod.Instance?.Settings?.SpriteInstancing != false
                    && ownsCurrentMap && !MeshDrawSubmission.IsCapturing && !hasScopedOrder()
                    && scopeMap != null;
            }
            else if (!MeshDrawSubmission.IsCapturing)
                // Capture-local draws must not absorb pending outer commands.
                Flush();
        }
        catch (Exception exception)
        {
            DisableAfterFailure(exception);
            Flush(false);
        }
    }

    private static bool PrepareScopeView()
    {
        // No camera/view work on a map that never draws an eligible mod part.
        scopePrepared = true;
        Camera camera = Find.Camera;
        if (camera == null || !camera.orthographic || !ReferenceEquals(scopeMap, Find.CurrentMap))
        {
            scopeEnabled = false;
            return false;
        }
        view = camera.worldToCameraMatrix;
        mapSizeX = scopeMap.Size.x;
        mapSizeZ = scopeMap.Size.z;
        active = scopeEnabled = camera.transform.forward.y < 0f
            && Math.Abs(view.m20) * (mapSizeX + 32f) + Math.Abs(view.m22) * (mapSizeZ + 32f) <= 1f / 16384f;
        return active;
    }

    private static bool PlaneEligible(float y, out float bin)
    {
        bin = DepthBin(-16f, y, -16f);
        return float.IsFinite(bin)
            && bin == DepthBin(mapSizeX + 16f, y, -16f)
            && bin == DepthBin(-16f, y, mapSizeZ + 16f)
            && bin == DepthBin(mapSizeX + 16f, y, mapSizeZ + 16f);
    }

    private static float DepthBin(float x, float y, float z)
        => MathF.Round(MathF.Abs(view.m20 * x + view.m21 * y + view.m22 * z + view.m23) * 4096f);

    private static void BeforeEnqueue(in MeshDrawCommand command)
    {
        // Flush keeps its run pending until submission returns, so this also
        // preserves commit tracking while avoiding TLS work on empty scopes.
        if (pendingPlanes == 0) return;
        if (internalSubmission) { ownEnqueueEntered = true; return; }
        // No dictionary/bounds work without pending mod sprites. Other threads
        // and capture lists do not interact with this main-thread scope.
        if (!active || Environment.CurrentManagedThreadId != scopeThread
            || MeshDrawSubmission.IsCapturing) return;
        try
        {
            int queue = command.RenderQueueOverride > 0 ? command.RenderQueueOverride : command.Material.RenderQueue;
            int depthQueue = queue;
            var sceneGrab = command.Material.Shader.SceneGrab;
            if (sceneGrab != null && queue < sceneGrab.Queue) queue = sceneGrab.Queue;
            if (queue != ShaderDatabase.Cutout.renderQueue) return;
            if (depthQueue <= 2500) { Flush(); return; }
            float bin = SubmissionDepthBin(in command);
            foreach (PlaneBatch plane in planes.Values)
                if (plane.Pending && (!float.IsFinite(bin) || plane.depthBin == bin)) plane.Flush();
        }
        catch (Exception exception)
        {
            DisableAfterFailure(exception);
            Flush();
        }
    }

    private static float SubmissionDepthBin(in MeshDrawCommand command)
    {
        var center = command.Instancing?.SortCentre;
        if (center.HasValue)
        {
            var point = center.Value;
            return DepthBin(point.X, point.Y, point.Z);
        }
        // Exact bounds/section rule and matrix convention of TransparentDepthOrder.
        var bounds = command.Mesh.Bounds;
        bool section = false;
        var submeshes = command.Mesh.CpuStorage.SubMeshes;
        if ((uint)command.SubmeshIndex < (uint)submeshes.Length)
        {
            var selected = submeshes[command.SubmeshIndex].Bounds;
            if (!selected.IsEmpty && (selected.MinX != 0f || selected.MinY != 0f || selected.MinZ != 0f
                || selected.MaxX != 0f || selected.MaxY != 0f || selected.MaxZ != 0f))
            {
                section = selected.MinY != bounds.MinY || selected.MaxY != bounds.MaxY;
                bounds = selected;
            }
        }
        float x = (bounds.MinX + bounds.MaxX) * .5f;
        float y = section ? bounds.MinY : (bounds.MinY + bounds.MaxY) * .5f;
        float z = (bounds.MinZ + bounds.MaxZ) * .5f;
        var matrix = command.Matrix;
        return DepthBin(matrix.M11 * x + matrix.M12 * y + matrix.M13 * z + matrix.M14,
            matrix.M21 * x + matrix.M22 * y + matrix.M23 * z + matrix.M24,
            matrix.M31 * x + matrix.M32 * y + matrix.M33 * z + matrix.M34);
    }

    private static void End(ref bool __state)
    {
        if (!__state) return;
        __state = false;
        FinishScope(true);
    }

    private static Exception Cleanup(Exception __exception, bool __state)
    {
        if (__state) FinishScope(false);
        return __exception;
    }

    private static void FinishScope(bool draw)
    {
        try
        {
            if (depth == 1) { active = false; Flush(draw); }
        }
        finally
        {
            if (depth > 0) depth--;
            active = depth == 1 && scopeEnabled && scopePrepared && !faulted;
            if (depth == 0)
            {
                scopeEnabled = scopePrepared = false;
                scopeMap = null;
            }
        }
    }

    private static void Flush(bool draw = true)
    {
        foreach (PlaneBatch plane in planes.Values) plane.Flush(draw);
    }

    private static void DisableAfterFailure(Exception exception)
    {
        faulted = true;
        active = scopeEnabled = false;
        if (warnedFailure) return;
        warnedFailure = true;
        try { Log.Warning("[Modular Turrets] Sprite instancing disabled for this session after a rendering error; ordinary sprites remain enabled. " + exception.Message); }
        catch { /* Logging must not replace the game's original draw exception. */ }
    }

    internal static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, bool allowBatch = true)
    {
        bool ownFallback = false;
        try
        {
            if (scopeEnabled && depth == 1 && !internalSubmission && Environment.CurrentManagedThreadId == scopeThread
                && allowBatch && !MeshDrawSubmission.IsCapturing && !hasScopedOrder()
                && SystemInfo.supportsInstancing && mesh != null && material != null
                && material.shader == ShaderDatabase.Cutout && material.passCount == 1
                && material.renderQueue == ShaderDatabase.Cutout.renderQueue
                && (scopePrepared || PrepareScopeView()) && active)
            {
                if (!planes.TryGetValue(matrix.m13, out PlaneBatch plane))
                    planes.Add(matrix.m13, plane = new PlaneBatch());
                if (!plane.checkedScope)
                {
                    plane.checkedScope = true;
                    plane.eligible = PlaneEligible(matrix.m13, out plane.depthBin);
                }
                // The caller supplies horizontal unit quads. Check their entire
                // transformed bounds against the equal-depth-bin region.
                float halfX = (Math.Abs(matrix.m00) + Math.Abs(matrix.m02)) * .5f;
                float halfZ = (Math.Abs(matrix.m20) + Math.Abs(matrix.m22)) * .5f;
                if (plane.eligible && float.IsFinite(halfX) && float.IsFinite(halfZ)
                    && float.IsFinite(matrix.m03) && float.IsFinite(matrix.m23)
                    && matrix.m03 - halfX >= -16f && matrix.m03 + halfX <= mapSizeX + 16f
                    && matrix.m23 - halfZ >= -16f && matrix.m23 + halfZ <= mapSizeZ + 16f)
                {
                    // Distinct altitudes can quantize to one rank as well.
                    if (pendingPlanes != 0)
                        foreach (PlaneBatch other in planes.Values)
                            if (other != plane && other.Pending && other.depthBin == plane.depthBin) other.Flush();
                    if (plane.Add(mesh, matrix, material, layer)) return;
                }
            }
            // These two shared unit meshes have their ordinary sort centre at
            // local zero. Handle our fallback once, without Enqueue repeating
            // material/submesh/bounds work for every side or damaged sprite.
            if (active && !internalSubmission && pendingPlanes != 0
                && Environment.CurrentManagedThreadId == scopeThread && !MeshDrawSubmission.IsCapturing
                && (ReferenceEquals(mesh, MeshPool.plane10) || ReferenceEquals(mesh, MeshPool.plane10Flip))
                && material != null && material.shader == ShaderDatabase.Cutout && material.passCount == 1
                && material.renderQueue == ShaderDatabase.Cutout.renderQueue)
            {
                float bin = DepthBin(matrix.m03, matrix.m13, matrix.m23);
                foreach (PlaneBatch plane in planes.Values)
                    if (plane.Pending && (!float.IsFinite(bin) || plane.depthBin == bin)) plane.Flush();
                ownFallback = true;
            }
        }
        catch (Exception exception)
        {
            DisableAfterFailure(exception);
            Flush();
        }
        if (!ownFallback) { Graphics.DrawMesh(mesh, matrix, material, layer); return; }
        bool previousSubmission = internalSubmission;
        internalSubmission = true;
        try { Graphics.DrawMesh(mesh, matrix, material, layer); }
        finally { internalSubmission = previousSubmission; }
    }
}
