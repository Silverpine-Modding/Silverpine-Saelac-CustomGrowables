#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SilverpineMods.CustomGrowables;

internal static class NativeGrowablePatchCode
{
    internal static MethodInfo Method(Type type, string name) =>
        type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!;
    internal static bool IsPlantInstantiation(CodeInstruction instruction) =>
        instruction.operand is MethodInfo method && method.DeclaringType == typeof(UnityEngine.Object) &&
        method.Name == "Instantiate" && method.IsGenericMethod &&
        method.GetGenericArguments().SequenceEqual(new[] { typeof(UnityEngine.GameObject) }) &&
        method.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[]
            { typeof(UnityEngine.GameObject), typeof(UnityEngine.Vector3), typeof(UnityEngine.Quaternion) });

    internal static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> input,
        Func<CodeInstruction, bool> matches, string replacement, bool loadThis)
    {
        var code = input.ToList();
        // A changed game signature must fail visibly, never silently miss XP or
        // accidentally intercept unrelated native calls.
        if (code.Count(matches) != 1) throw new InvalidOperationException("Expected one native growable hook site: " + replacement);
        foreach (CodeInstruction instruction in code)
        {
            if (!matches(instruction)) { yield return instruction; continue; }
            if (loadThis)
            {
                var argument = new CodeInstruction(OpCodes.Ldarg_0);
                argument.MoveLabelsFrom(instruction);
                argument.MoveBlocksFrom(instruction);
                yield return argument;
            }
            instruction.opcode = OpCodes.Call;
            instruction.operand = Method(typeof(NativeGrowableHooks), replacement);
            yield return instruction;
        }
    }
}

[HarmonyPatch]
internal static class NativeGrowableHarvestPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return NativeGrowablePatchCode.Method(typeof(Crop), "HarvestInto");
        yield return NativeGrowablePatchCode.Method(typeof(ResourceNode), "AddItem");
    }
    private static bool Prefix(UnityEngine.MonoBehaviour __instance) => NativeGrowableHooks.CanHarvest(__instance);
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        NativeGrowablePatchCode.Replace(instructions,
            i => i.operand is MethodInfo m && m == NativeGrowablePatchCode.Method(typeof(Inventory), nameof(Inventory.TryAddItem)),
            nameof(NativeGrowableHooks.Deliver), true);
}

[HarmonyPatch(typeof(Crop), "Mature")]
internal static class NativeCropMaturePatch
{
    private static void Prefix(Crop __instance, out GrowableInfo? __state)
    {
        __state = null;
        if (GrowableApi.HasGrowthListeners) NativeGrowableHooks.TryGetInfo(__instance.gameObject, out __state!);
    }
    private static void Postfix(Crop __instance, GrowableInfo? __state)
    {
        if (__state != null && !__state.IsHarvestReady && NativeGrowableHooks.TryGetInfo(__instance.gameObject, out var after) && after.IsHarvestReady)
            GrowableApi.NotifyGrowth(__state, after, GrowableGrowthReason.Growth);
    }
}

[HarmonyPatch(typeof(InGameTimeCrop), "OnPostProcessTurn")]
internal static class NativeTimedCropMaturePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        NativeGrowablePatchCode.Replace(instructions, NativeGrowablePatchCode.IsPlantInstantiation,
            nameof(NativeGrowableHooks.InstantiateMature), true);
}

[HarmonyPatch]
internal static class NativeSeedPlantPatch
{
    private static MethodBase TargetMethod()
    {
        // Hook the successful delayed action, NOT OnUse (which can be rejected
        // or cancelled). Locate by the actual calls instead of compiler numbering.
        var candidates = typeof(ItemComponent_Seeds).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0 && m.GetMethodBody() != null)
            .Where(m =>
            {
                var code = PatchProcessor.ReadMethodBody(m).Select(pair => new CodeInstruction(pair.Key, pair.Value)).ToArray();
                return code.Any(NativeGrowablePatchCode.IsPlantInstantiation) && code.Any(i =>
                    i.operand is MethodInfo call && call.DeclaringType == typeof(Item) && call.Name == nameof(Item.RemoveItemFromInventory));
            }).ToArray();
        if (candidates.Length != 1) throw new InvalidOperationException("Cannot identify native seed planting completion hook.");
        return candidates[0];
    }
    private static void Prefix(object __instance, out NativeGrowableHooks.PlantingScope __state) =>
        __state = NativeGrowableHooks.BeginPlanting(__instance);
    private static Exception? Finalizer(Exception? __exception, NativeGrowableHooks.PlantingScope? __state)
    {
        NativeGrowableHooks.EndPlanting(__state);
        return __exception;
    }
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        NativeGrowablePatchCode.Replace(instructions, NativeGrowablePatchCode.IsPlantInstantiation,
            nameof(NativeGrowableHooks.InstantiateSeed), false);
}
