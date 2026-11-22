using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class NativeApiBinaryChecks
{
    internal static void Run(Action<bool, string> check, AssemblyDefinition game, AssemblyDefinition plugin)
    {
        bool Calls(MethodDefinition method, string type, string name) => method.Body.Instructions.Any(i =>
            i.Operand is MethodReference m && m.DeclaringType.FullName == type && m.Name == name);
        bool Instantiate(Instruction instruction) => instruction.Operand is GenericInstanceMethod m &&
            m.DeclaringType.FullName == "UnityEngine.Object" && m.Name == "Instantiate" &&
            m.GenericArguments.Count == 1 && m.GenericArguments[0].FullName == "UnityEngine.GameObject" &&
            m.Parameters.Count == 3 && m.Parameters[1].ParameterType.FullName == "UnityEngine.Vector3" &&
            m.Parameters[2].ParameterType.FullName == "UnityEngine.Quaternion";
        var crop = game.MainModule.GetType("Crop");
        check(crop.Fields.Any(f => f.Name == "mature" && f.FieldType.FullName == "System.Boolean") &&
                crop.Fields.Any(f => f.Name == "visibleName" && f.FieldType.FullName == "System.String"),
            "installed native Crop exposes the exact snapshot fields used by the API");
        foreach (var (type, name) in new[] { ("Crop", "HarvestInto"), ("ResourceNode", "AddItem") })
        {
            var method = game.MainModule.GetType(type).Methods.Single(m => m.Name == name);
            check(!method.IsStatic && method.Parameters[0].ParameterType.Name == "Inventory" &&
                method.Body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "Inventory" &&
                    m.Name == "TryAddItem" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "Item") == 1,
                "installed native harvest has exactly one supported delivery hook site: " + type);
        }
        var add = game.MainModule.GetType("ResourceNode").Methods.Single(m => m.Name == "AddItem");
        var body = add.Body.Instructions.ToList();
        int quality = body.FindIndex(i => i.Operand is MethodReference m && m.Name == "set_Quality");
        int delivery = body.FindIndex(i => i.Operand is MethodReference m && m.Name == "TryAddItem");
        check(quality >= 0 && delivery > quality, "native herb output quality is finalized before the skills modifier hook");
        var nativeSeed = game.MainModule.GetType("ItemComponent_Seeds");
        var callbacks = nativeSeed.NestedTypes.SelectMany(t => t.Methods)
            .Where(m => !m.IsStatic && m.HasBody && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 0 &&
                m.Body.Instructions.Any(Instantiate) && Calls(m, "Item", "RemoveItemFromInventory")).ToArray();
        check(callbacks.Length == 1 && callbacks[0].Body.Instructions.Count(Instantiate) == 1 &&
                callbacks[0].DeclaringType.Fields.Count(f => f.FieldType.FullName == "ItemComponent_Seeds") == 1,
            "installed native seeds have exactly one delayed spawn/debit callback with an accessible owning seed component");
        check(!nativeSeed.Methods.Single(m => m.Name == "OnUse").Body.Instructions.Any(Instantiate),
            "native OnUse does not itself plant; the API completion hook cannot count cancelled interaction starts");
        var timed = game.MainModule.GetType("InGameTimeCrop").Methods.Single(m => m.Name == "OnPostProcessTurn");
        check(timed.Body.Instructions.Count(Instantiate) == 1 && Calls(timed, "TurfCollider", "CanSeePlayer") &&
                Calls(timed, "WorldInfoManager", "GetSeasonFor"),
            "installed timed crops have one replacement hook after native season/visibility checks");
        var mature = crop.Methods.Single(m => m.Name == "Mature");
        check(mature.Body.Instructions.Any(i => i.OpCode.Code == Code.Stfld && i.Operand is FieldReference f && f.Name == "mature"),
            "installed Crop.Mature commits readiness before the API postfix observes it");
        const string ns = "SilverpineMods.CustomGrowables.";
        var api = plugin.MainModule.GetType(ns + "GrowableApi");
        check(api.IsPublic && api.Methods.Any(m => m.IsPublic && m.IsStatic && m.Name == "TryGetPlant") &&
                new[] { "Planted", "GrowthChanged", "PreparingHarvestItem", "Harvested" }.All(name =>
                    api.Events.Any(e => e.Name == name && e.AddMethod.IsPublic && e.AddMethod.IsStatic)),
            "compiled public API exports plant queries and all four skills events");
        var custom = plugin.MainModule.GetType(ns + "CustomGrowable");
        check(Calls(custom.Methods.Single(m => m.Name == "HarvestInto"), ns + "GrowableApi", "PrepareOutput") &&
                Calls(custom.Methods.Single(m => m.Name == "HarvestInto"), ns + "GrowableApi", "NotifyHarvested") &&
                Calls(custom.Methods.Single(m => m.Name == "Advance"), ns + "GrowableApi", "NotifyGrowth"),
            "compiled custom runtime integrates both modifiers and committed harvest/growth notifications");
        var seed = plugin.MainModule.GetType(ns + "ItemComponent_CustomGrowableSeed");
        check(seed.NestedTypes.SelectMany(t => t.Methods).Where(m => m.HasBody).Any(m =>
                Calls(m, ns + "GrowableApi", "NotifyPlanted")),
            "custom seed completion publishes planting from inside its delayed action");
        var nativePatch = plugin.MainModule.GetType(ns + "NativeSeedPlantPatch");
        check(Calls(nativePatch.Methods.Single(m => m.Name == "Finalizer"), ns + "NativeGrowableHooks", "EndPlanting") &&
                Calls(nativePatch.Methods.Single(m => m.Name == "TargetMethod"), "System.Type", "GetNestedTypes"),
            "native planting scope is finalized even on exceptions and discovers the compiler-generated completion method");
        var attributes = plugin.MainModule.GetTypes().Where(t => t.Namespace == ns.TrimEnd('.'))
            .SelectMany(t => t.CustomAttributes).Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ToArray();
        check(!attributes.Any(a => a.ConstructorArguments.Any(arg => arg.Value is TypeReference t &&
                (t.FullName == "Inventory" || t.FullName == "UnityEngine.Object"))),
            "API avoids global Inventory and Unity Object patches that would count unrelated transactions");
    }
}
