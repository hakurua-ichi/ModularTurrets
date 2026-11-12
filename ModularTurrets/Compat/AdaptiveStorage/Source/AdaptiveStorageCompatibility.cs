using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.AdaptiveStorage;

[StaticConstructorOnStartup]
internal static class AdaptiveStorageCompatibility
{
    private static Type workerType, optimizedType, unsupportedType;
    private static PropertyInfo thingProperty, graphicProperty, offsetProperty, scaleProperty,
        rotationProperty, extraRotationProperty, combinedRotationProperty;
    private static MethodInfo graphicAngleMethod;
    private static bool disabled;

    static AdaptiveStorageCompatibility()
    {
        if (!ModLister.AnyModActiveNoSuffix(new[] { "adaptive.storage.framework" })) return;
        try
        {
            workerType = AccessTools.TypeByName("AdaptiveStorage.ItemGraphicWorker");
            Type dataType = AccessTools.TypeByName("AdaptiveStorage.PrintDatas.PrintData");
            optimizedType = AccessTools.TypeByName("AdaptiveStorage.PrintDatas.OptimizedPrintData");
            unsupportedType = AccessTools.TypeByName("AdaptiveStorage.PrintDatas.UnsupportedGraphicPrintData");
            if (workerType == null || dataType == null || optimizedType == null || unsupportedType == null)
                throw new MissingMemberException("ASF print data types changed.");
            thingProperty = RequireProperty(dataType, "Thing", typeof(Thing));
            graphicProperty = RequireProperty(dataType, "Graphic", typeof(Graphic));
            offsetProperty = RequireProperty(dataType, "DrawOffset", typeof(Vector3), writable: true);
            scaleProperty = RequireProperty(dataType, "RotatedDrawScale", typeof(Vector2));
            rotationProperty = RequireProperty(dataType, "ThingRotation", typeof(Rot4));
            extraRotationProperty = RequireProperty(dataType, "ExtraRotation", typeof(float));
            combinedRotationProperty = RequireProperty(optimizedType, "CombinedRotation", typeof(float));
            graphicAngleMethod = AccessTools.Method(typeof(Graphic), "AngleFromRot", new[] { typeof(Rot4) })
                ?? throw new MissingMethodException("Core graphic rotation method changed.");
            MethodInfo target = AccessTools.DeclaredMethod(workerType, "UpdatePrintData", new[]
            {
                dataType, AccessTools.TypeByName("AdaptiveStorage.ThingClass")
                    ?? throw new MissingMemberException("ASF storage type changed.")
            }) ?? throw new MissingMethodException("ASF item graphic worker changed.");
            new Harmony("local.modularturrets.adaptivestorage").Patch(target,
                postfix: new HarmonyMethod(typeof(AdaptiveStorageCompatibility), nameof(AdjustLoosePartOffset)));
        }
        catch (Exception error) { Disable(error); }
    }

    private static PropertyInfo RequireProperty(Type type, string name, Type propertyType, bool writable = false)
    {
        PropertyInfo property = AccessTools.Property(type, name);
        if (property?.GetMethod == null || property.PropertyType != propertyType || writable && property.SetMethod == null)
            throw new MissingMemberException(type.FullName, name);
        return property;
    }

    private static void AdjustLoosePartOffset(object __instance, object printData)
    {
        if (disabled || __instance.GetType() != workerType) return;
        try
        {
            // Do not reinterpret another mod's worker or custom print-data path.
            Type type = printData.GetType();
            if (type != optimizedType && type != unsupportedType) return;
            if (graphicProperty.GetValue(printData) is not Graphic_LoosePartOutline graphic
                || thingProperty.GetValue(printData) is not ThingWithComps thing
                || thing.def.category != ThingCategory.Item || thing.GetComp<CompTurretPart>() == null) return;
            Rot4 rotation = (Rot4)rotationProperty.GetValue(printData);
            Vector3 original = graphic.DrawOffset(rotation);
            if (original.x == 0f && original.z == 0f) return;
            Vector2 scale = (Vector2)scaleProperty.GetValue(printData);
            float angle = type == optimizedType ? (float)combinedRotationProperty.GetValue(printData)
                : (float)graphicAngleMethod.Invoke(graphic, new object[] { rotation }) + (float)extraRotationProperty.GetValue(printData);
            Vector3 offset = (Vector3)offsetProperty.GetValue(printData);
            if (!float.IsFinite(original.x) || !float.IsFinite(original.z) || !float.IsFinite(scale.x)
                || !float.IsFinite(scale.y) || !float.IsFinite(angle) || !float.IsFinite(offset.x) || !float.IsFinite(offset.z)) return;

            // Loose artwork is centered on the ground by this graphic offset.
            // ASF includes it in both worker and graphic offsets, while scaling
            // and rotating the transparent artwork around the quad's center.
            float radians = angle * (MathF.PI / 180f);
            float sin = MathF.Sin(radians), cos = MathF.Cos(radians);
            float scaledX = original.x * scale.x, scaledZ = original.z * scale.y;
            offset.x += scaledX * cos + scaledZ * sin - 2f * original.x;
            offset.z += scaledZ * cos - scaledX * sin - 2f * original.z;
            if (!float.IsFinite(offset.x) || !float.IsFinite(offset.z)) return;
            offsetProperty.SetValue(printData, offset);
        }
        catch (Exception error) { Disable(error); }
    }

    private static void Disable(Exception error)
    {
        disabled = true;
        Log.Warning(L.T("MT_AdaptiveStorageUnavailable", error.Message));
    }
}
