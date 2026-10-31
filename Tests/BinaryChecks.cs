using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BinaryChecks
{
    internal static void Run(Action<bool, string> check, string game)
    {
        string plugin = Path.Combine(game, "Plugin Development", "CustomGrowables", "bin", "Release", "CustomGrowables.dll");
        using var assembly = AssemblyDefinition.ReadAssembly(plugin);
        foreach (string dependency in new[] { "CustomItemLoader", "ModdingTools" })
        {
            string installed = Path.Combine(game, "BepInEx", "plugins", dependency, dependency + ".dll");
            using var api = AssemblyDefinition.ReadAssembly(installed);
            var available = api.MainModule.GetTypes().SelectMany(t => t.Methods.Select(m => m.FullName)
                .Concat(t.Fields.Select(f => f.FullName))).ToHashSet();
            var references = assembly.MainModule.GetMemberReferences().Where(m =>
                m.DeclaringType.Scope is AssemblyNameReference scope && scope.Name == dependency).ToArray();
            var missing = references.Where(r => !available.Contains(r.FullName)).Select(r => r.FullName).ToArray();
            check(missing.Length == 0, $"compiled Growables resolves all {references.Length} member references against installed {dependency}: " + string.Join(", ", missing));
        }
        var runtime = assembly.MainModule.GetType("SilverpineMods.CustomGrowables.CustomGrowable");
        check(!runtime.Methods.Any(m => m.Name == "OnDestroy") && runtime.Methods.Any(m => m.Name == "OnDisable"),
            "release artifact retains OnDisable cleanup without a plant OnDestroy method");
        var editor = assembly.MainModule.GetType("SilverpineMods.CustomGrowables.GrowableEditorUI");
        bool Calls(MethodDefinition method, string name) => method.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == name);
        var registry = assembly.MainModule.GetType("SilverpineMods.CustomGrowables.GrowableRegistry");
        var nativeRendering = registry.Methods.Single(m => m.Name == "ApplyNativeTreeRendering");
        check(Calls(nativeRendering, "get_sharedMaterial") && Calls(nativeRendering, "set_sharedMaterial") &&
                Calls(nativeRendering, "set_layer") && !Calls(nativeRendering, "get_material"),
            "world growables share the native lit material and object layer without allocating per-plant materials");
        var stageLight = assembly.MainModule.GetType("SilverpineMods.CustomGrowables.StageLight");
        check(!stageLight.Interfaces.Any() && !stageLight.Methods.Any(m => m.Name == "OnDestroy") &&
                Calls(runtime.Methods.Single(m => m.Name == "OnDisable"), "Clear") &&
                Calls(runtime.Methods.Single(m => m.Name == "ApplyStage"), "Apply"),
            "stage lights are non-serialized children applied with growth and cleaned up through OnDisable");
        check(!registry.Methods.Single(m => m.Name == "CreateTemplate").Body.Instructions.Any(i =>
                i.Operand is GenericInstanceMethod m && m.GenericArguments.Any(t => t.Name == "LightAttacher" || t.Name == "TileLight")),
            "adding stage glow does not change the prefab's native serializable component layout");
        var seed = assembly.MainModule.GetType("SilverpineMods.CustomGrowables.ItemComponent_CustomGrowableSeed");
        check(seed.Interfaces.Any(i => i.InterfaceType.Name == "IQualityItemComponent") &&
                Calls(registry.Methods.Single(m => m.Name == "Plant"), "SetSeedQuality") &&
                Calls(runtime.Methods.Single(m => m.Name == "HarvestInto"), "ApplyHarvest"),
            "native quality-capable seeds pass quality to plants and the harvest quality policy");
        check(Calls(editor.Methods.Single(m => m.Name == "DrawStage"), "DrawStageLight") &&
                Calls(editor.Methods.Single(m => m.Name == "CopyStageArt"), "Copy"),
            "stage light controls are wired into the editor and stage duplication deep-copies their settings");
        check(Calls(editor.Methods.Single(m => m.Name == "DrawHarvest"), "BonusChance") &&
                Calls(assembly.MainModule.GetType("SilverpineMods.CustomGrowables.GrowableDefinition").Methods.Single(m => m.Name == "Validate"), "ValidateBonusChances"),
            "multi-tier harvest probabilities are exposed by the editor and validated before registration/save");
        var load = editor.Methods.Single(m => m.Name == "LoadSelected");
        var loadInstructions = load.Body.Instructions.ToList();
        int migration = loadInstructions.FindIndex(i => i.Operand is MethodReference m && m.Name == "EnsureStageCacheIdentities");
        int loadedStatus = loadInstructions.FindIndex(i => i.OpCode.Code == Code.Ldstr && Equals(i.Operand, "Loaded "));
        bool WritesDirty(Instruction instruction, Code value) => instruction.OpCode.Code == Code.Stfld &&
            instruction.Operand is FieldReference field && field.Name == "dirty" && instruction.Previous?.OpCode.Code == value;
        check(migration >= 0 && loadedStatus > migration && loadInstructions.Skip(migration + 1)
                .Take(loadedStatus - migration - 1).Any(i => WritesDirty(i, Code.Ldc_I4_0)),
            "loading or discarding legacy stages clears migration-only dirty state before reporting success");
        check(Calls(load, "CancelOffsetDrag") && Calls(load, "ClearGlbPreview") && Calls(load, "ClearNumberInputs"),
            "discard clears pending offset drags, previews, and numeric input along with reloading the saved data");
        var headerInstructions = editor.Methods.Single(m => m.Name == "DrawHeader").Body.Instructions.ToList();
        int discard = headerInstructions.FindIndex(i => i.OpCode.Code == Code.Ldstr && Equals(i.Operand, "Discard"));
        int save = headerInstructions.FindIndex(i => i.OpCode.Code == Code.Ldstr && Equals(i.Operand, "Save Pack"));
        var discardCalls = headerInstructions.Skip(discard + 1).Take(save - discard - 1)
            .Select(i => i.Operand).OfType<MethodReference>().Select(m => m.Name).ToArray();
        check(discard >= 0 && save > discard && discardCalls.Contains("LoadSelected") &&
                discardCalls.Contains("ExitGUI") && !discardCalls.Contains("SaveSelected"),
            "Discard reloads and ends the GUI event without saving changes");
        check(editor.Methods.Single(m => m.Name == "MarkDirty").Body.Instructions.Any(i => WritesDirty(i, Code.Ldc_I4_1)) &&
                editor.Methods.Single(m => m.Name == "SelectItem").Body.Instructions.Any(i =>
                    i.OpCode.Code == Code.Ldfld && i.Operand is FieldReference field && field.Name == "dirty"),
            "actual edits still mark the editor dirty and guard against switching away unsaved");
        var button = editor.Methods.Single(m => m.Name == "DrawActionButton");
        check(Calls(button, "CalcSize") && Calls(button, "Width") && Calls(button, "MinHeight"),
            "short editor actions measure their text and reserve sufficient control height");
        check(Calls(editor.Methods.Single(m => m.Name == "DrawFieldLabel"), "CalcSize"),
            "field labels account for actual themed text width rather than a narrow fixed column");
        check(Calls(editor.Methods.Single(m => m.Name == "DrawItemPanel"), "DrawItemCard") &&
            Calls(editor.Methods.Single(m => m.Name == "DrawItemCard"), "FitEditorText"),
            "sidebar cards route both name and ID through width-fitted single-line rendering");
        check(Calls(editor.Methods.Single(m => m.Name == "OnGUI"), "DrawEditorTooltip"),
            "full values remain accessible through a rendered tooltip, not just unused GUIContent metadata");
        var textInput = editor.Methods.Single(m => m.Name == "DrawWrappedTextInput");
        check(Calls(textInput, "GetRect") && Calls(textInput, "TextMeasurement") &&
            Calls(textInput, "TextArea") && Calls(textInput, "EditableText"),
            "expanding text boxes use width-dependent layout, multiline input, and safe reference normalization");
        check(!Calls(textInput, "Height") && !Calls(textInput, "MinHeight") && !Calls(textInput, "MaxHeight"),
            "scroll views cannot collapse the content-measured minimum height of multiline input");
        var textField = editor.Methods.Single(m => m.Name == "DrawText");
        check(Calls(textField, "BeginVertical") && Calls(textField, "DrawWrappedTextInput") && !Calls(textField, "TextField"),
            "text labels sit above expanding full-width input boxes");
        check(editor.Methods.Single(m => m.Name == "DrawStage").Body.Instructions.Count(i =>
                i.Operand is MethodReference m && m.Name == "DrawText") == 3,
            "sprite, image, and GLB references all use expanding input boxes");
        check(Calls(editor.Methods.Single(m => m.Name == "DrawInt"), "TextField") &&
            Calls(editor.Methods.Single(m => m.Name == "DrawFloatInline"), "TextField") &&
            Calls(editor.Methods.Single(m => m.Name == "DrawItemPanel"), "TextField"),
            "numeric and search inputs stay single-line");
    }
}
