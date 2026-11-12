using System;
using System.Collections.Generic;
using Waylith.Rendering;
using EngineShader = Waylith.Rendering.Shader;
using UnityShader = UnityEngine.Shader;

namespace ModularTurrets;

// Private API candidate: refer to installed stages and supply their material layout explicitly.
internal static class InstancedCutoutShader
{
    internal const string Key = "MT_InstancedSpriteCutoutApi045";
    private const string DiagnosticKey = "MT_ProbeBaseline045";
    private const string DiagnosticFragment = "MT_ProbeBaseline045.frag.glsl";
    private static EngineShader observedCutout, observedTransparent, replacement, diagnosticShader;
    private static UnityShader wrapper, diagnosticWrapper;
    private static MaterialLayout observedCutoutLayout, observedTransparentLayout;
    private static ShaderVertexInputLayout observedVertexLayout;
    private static ShaderProgramDefinition observedCutoutProgram, observedTransparentProgram;
    private static int cutoutLayoutVersion, transparentLayoutVersion, cutoutProgramSignature, transparentProgramSignature;
    private static bool attempted;
    internal static string LastFailure { get; private set; }
    internal static int BuildAttempts { get; private set; }
    internal static int BuildSuccesses { get; private set; }

    internal static void RequestRetry() => attempted = false;

    // The isolated probe changes modes only with batching idle, and clears its own material caches.
    internal static void SetDiagnosticOverride(EngineShader shader, UnityShader unityWrapper)
    {
        if ((shader == null) != (unityWrapper == null)) throw new ArgumentException("Both diagnostic shader handles are required.");
        if (shader != null && (shader.Key != DiagnosticKey || shader.VertexShaderFileName != "Transparent.vert.glsl"
            || shader.FragmentShaderFileName != DiagnosticFragment || unityWrapper.passCount != 1
            || unityWrapper.renderQueue != shader.DefaultRenderQueue))
            throw new ArgumentException("Not the isolated legacy-shader comparison.");
        diagnosticShader = shader; diagnosticWrapper = unityWrapper;
    }

    internal static bool HasExpectedFragment(EngineShader shader)
        => ReferenceEquals(shader, diagnosticShader)
            ? shader?.Key == DiagnosticKey && shader.FragmentShaderFileName == DiagnosticFragment
            : shader?.Key == Key && shader.FragmentShaderFileName == "Cutout.frag.glsl";

    internal static bool TryGet(EngineShader cutout, out EngineShader shader, out UnityShader unityWrapper)
    {
        shader = null; unityWrapper = null;
        if (diagnosticShader != null)
        {
            shader = diagnosticShader; unityWrapper = diagnosticWrapper;
            return true;
        }
        if (!ShaderRegistry.TryResolve("Transparent", out var transparent)) return Fail("Stock Transparent shader is not registered.");
        bool sameSources = ReferenceEquals(cutout, observedCutout) && ReferenceEquals(transparent, observedTransparent);
        if (sameSources && replacement != null && wrapper != null
            && ShaderRegistry.TryResolve(Key, out var registered) && ReferenceEquals(registered, replacement))
        {
            if (!SourceStateMatches(cutout, transparent))
            {
                // A render-thread hot reload can change layouts without replacing the Shader objects.
                // Stop using the copied layout until the normal user retry revalidates it.
                replacement = null; wrapper = null; attempted = true;
                return Fail("Stock shader metadata changed in place; ordinary sprites retained until retry.");
            }
            shader = replacement; unityWrapper = wrapper;
            return true;
        }
        if (sameSources && attempted) return false;
        observedCutout = cutout; observedTransparent = transparent;
        replacement = null; wrapper = null; attempted = true; BuildAttempts++;
        if (!ValidateSources(cutout, transparent)) return false;
        ObserveSourceState(cutout, transparent);

        var original = observedCutoutLayout;
        var storage = new MaterialStorageBufferSlot[1];
        foreach (var slot in transparent.MaterialLayout.StorageBufferSlots)
            if (slot.Binding == 2 && slot.PropertyName == "_InstanceTransforms") storage[0] = slot;
        var blocks = new MaterialUniformBlockSlot[original.UniformBlocks.Length];
        for (int i = 0; i < blocks.Length; i++)
        {
            var block = original.UniformBlocks[i];
            blocks[i] = new MaterialUniformBlockSlot(block.PropertyId, block.Size,
                block.Members == null ? Array.Empty<MaterialUniformSlot>() : (MaterialUniformSlot[])block.Members.Clone(),
                block.Arrays == null ? Array.Empty<MaterialBlockArraySlot>() : (MaterialBlockArraySlot[])block.Arrays.Clone());
        }
        var layout = new MaterialLayout((MaterialUniformSlot[])original.UniformSlots.Clone(),
            (MaterialTextureSlot[])original.TextureSlots.Clone(), original.UniformSize,
            storage, original.HasErrors, blocks);
        if (layout.HasErrors) return Fail("The explicit material layout is invalid.");

        var program = CopyProgram(cutout.Program);
        program.vertexShaderFileName = "Transparent.vert.glsl";
        program.fragmentShaderFileName = "Cutout.frag.glsl";
        var candidate = new EngineShader(Key, Key, program, cutout.Keywords.ToArray(), layout,
            cutout.PipelineState, cutout.IconShaderKey, observedVertexLayout);
        if (candidate.HasErrors || candidate.PipelineState != cutout.PipelineState
            || candidate.DefaultRenderQueue != cutout.DefaultRenderQueue || candidate.FlipTextureST != cutout.FlipTextureST
            || !SourceStateMatches(cutout, transparent))
            return Fail("The API shader does not preserve the Cutout program contract.");

        // Do not retarget ShaderDatabase.Cutout or a cached ShaderDef wrapper.
        var candidateWrapper = UnityShader.FromEngineShader(Key, candidate);
        ShaderRegistry.Register(candidate);
        replacement = candidate; wrapper = candidateWrapper; LastFailure = null; BuildSuccesses++;
        shader = replacement; unityWrapper = wrapper;
        return true;
    }

    private static void ObserveSourceState(EngineShader cutout, EngineShader transparent)
    {
        observedCutoutLayout = cutout.MaterialLayout; observedTransparentLayout = transparent.MaterialLayout;
        observedVertexLayout = transparent.VertexInputLayout;
        observedCutoutProgram = cutout.Program; observedTransparentProgram = transparent.Program;
        cutoutLayoutVersion = observedCutoutLayout.VersionHash; transparentLayoutVersion = observedTransparentLayout.VersionHash;
        cutoutProgramSignature = ProgramSignature(observedCutoutProgram);
        transparentProgramSignature = ProgramSignature(observedTransparentProgram);
    }

    private static bool SourceStateMatches(EngineShader cutout, EngineShader transparent)
        => ReferenceEquals(cutout.MaterialLayout, observedCutoutLayout) && observedCutoutLayout.VersionHash == cutoutLayoutVersion
            && ReferenceEquals(transparent.MaterialLayout, observedTransparentLayout) && observedTransparentLayout.VersionHash == transparentLayoutVersion
            && ReferenceEquals(transparent.VertexInputLayout, observedVertexLayout)
            && ReferenceEquals(cutout.Program, observedCutoutProgram) && ReferenceEquals(transparent.Program, observedTransparentProgram)
            && ProgramSignature(observedCutoutProgram) == cutoutProgramSignature
            && ProgramSignature(observedTransparentProgram) == transparentProgramSignature;

    private static int ProgramSignature(ShaderProgramDefinition program)
        => HashCode.Combine(program.vertexShaderFileName, program.fragmentShaderFileName, program.useTexture,
            program.flipTextureST, program.transparent, (int)program.renderQueue,
            program.passShaderOverrides, program.textureWrapOverrides);

    private static bool ValidateSources(EngineShader cutout, EngineShader transparent)
    {
        if (cutout == null || transparent == null || cutout.HasErrors || transparent.HasErrors
            || cutout.VertexShaderFileName != "SceneDefault.vert.glsl" || cutout.FragmentShaderFileName != "Cutout.frag.glsl"
            || transparent.VertexShaderFileName != "Transparent.vert.glsl" || transparent.FragmentShaderFileName != "Transparent.frag.glsl"
            || cutout.Program.sceneGrab != null)
            return Fail("The installed stock shader programs are unavailable or changed.");
        var layout = cutout.MaterialLayout;
        if (layout.HasErrors || layout.StorageBufferSlots.Length != 0 || layout.UniformSize != 32
            || layout.UniformSlots.Length != 2 || layout.TextureSlots.Length != 1
            || layout.UniformSlots[0].PropertyName != "_Color" || layout.UniformSlots[0].Offset != 0
            || layout.UniformSlots[1].PropertyName != "_MainTex_ST" || layout.UniformSlots[1].Offset != 16
            || layout.TextureSlots[0].PropertyName != "_MainTex" || layout.TextureSlots[0].Binding != 1)
            return Fail("The installed Cutout material layout is not the supported sprite layout.");
        int matches = 0;
        foreach (var slot in transparent.MaterialLayout.StorageBufferSlots)
            if (slot.Binding == 2 && slot.PropertyName == "_InstanceTransforms") matches++;
        return matches == 1 || Fail("The installed instancing vertex has no unique transform-storage slot.");
    }

    private static ShaderProgramDefinition CopyProgram(ShaderProgramDefinition source) => new()
    {
        vertexShaderFileName = source.vertexShaderFileName, fragmentShaderFileName = source.fragmentShaderFileName,
        useTexture = source.useTexture, flipTextureST = source.flipTextureST, transparent = source.transparent,
        depthTest = source.depthTest, depthWrite = source.depthWrite, cullMode = source.cullMode,
        stencilEnabled = source.stencilEnabled, stencilCompare = source.stencilCompare,
        stencilPassOp = source.stencilPassOp, stencilFailOp = source.stencilFailOp, stencilDepthFailOp = source.stencilDepthFailOp,
        stencilRef = source.stencilRef, stencilReadMask = source.stencilReadMask, stencilWriteMask = source.stencilWriteMask,
        colorWrite = source.colorWrite, depthBiasConstantFactor = source.depthBiasConstantFactor,
        depthBiasSlopeFactor = source.depthBiasSlopeFactor, blendMode = source.blendMode,
        blendSrcFactor = source.blendSrcFactor, blendDstFactor = source.blendDstFactor, renderQueue = source.renderQueue,
        passShaderOverrides = source.passShaderOverrides == null ? null : new Dictionary<string, string>(source.passShaderOverrides),
        textureWrapOverrides = source.textureWrapOverrides == null ? null : new Dictionary<string, TextureWrapMode>(source.textureWrapOverrides),
        sceneGrab = source.sceneGrab
    };

    private static bool Fail(string reason) { LastFailure = reason; return false; }
}
