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
        check(runtime.Methods.Any(m => m.Name == "OnDisable"),
            "plant cleanup also runs when temporarily disabled, not only when destroyed");
        var bootstrap = assembly.MainModule.GetType("SilverpineMods.CustomGrowables.Plugin");
        check(!bootstrap.Methods.Where(m => m.HasBody && (m.Name == "OnDestroy" || m.Name == "OnDisable"))
                .SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference m &&
                    (m.Name.StartsWith("Unpatch", StringComparison.Ordinal) ||
                     m.Name.StartsWith("Unregister", StringComparison.Ordinal) ||
                     m.Name.StartsWith("remove_", StringComparison.Ordinal))),
            "bootstrap host destruction cannot unpatch or unregister persistent gameplay hooks; object-local cleanup remains allowed");
        var editor = assembly.MainModule.GetType("SilverpineMods.CustomGrowables.GrowableEditorUI");
        bool Calls(MethodDefinition method, string name) => method.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == name);
        var applyStage = runtime.Methods.Single(m => m.Name == "ApplyStage");
        check(!runtime.Methods.Where(m => m.HasBody).Any(m => Calls(m, "SetSortingOrder")) &&
                Calls(applyStage, "RoundToInt") && Calls(applyStage, "set_sortingOrder"),
            "stage updates sort directly without calling YSorter before its Awake");
        using var gameAssembly = AssemblyDefinition.ReadAssembly(Path.Combine(game, "Silverpine_Data", "Managed", "Assembly-CSharp.dll"));
        var nativeSorter = gameAssembly.MainModule.GetType("YSorter");
        var nativeSort = nativeSorter.Methods.Single(m => m.Name == "SetSortingOrder");
        check(nativeSorter.Methods.Single(m => m.Name == "Awake").Body.Instructions.Any(i =>
                i.OpCode.Code == Code.Stfld && i.Operand is FieldReference f && f.Name == "spriteRenderer") &&
                nativeSort.Body.Instructions.Any(i => i.OpCode.Code == Code.Ldfld && i.Operand is FieldReference f && f.Name == "spriteRenderer") &&
                nativeSort.Body.Instructions.Any(i => i.OpCode.Code == Code.Ldc_R4 && Equals(i.Operand, 10f)) &&
                nativeSort.Body.Instructions.Any(i => i.OpCode.Code == Code.Ldfld && i.Operand is FieldReference f && f.Name == "offset") &&
                Calls(nativeSort, "RoundToInt") && Calls(nativeSort, "set_sortingOrder"),
            "installed native YSorter caches its renderer in Awake and uses rounded Y-times-ten plus offset");
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
        var qualityParticles = assembly.MainModule.GetType("SilverpineMods.CustomGrowables.QualityParticles");
        var applyParticles = qualityParticles.Methods.Single(m => m.Name == "Apply");
        bool CallsParticles(MethodDefinition method, string name) => method.Body.Instructions.Any(i =>
            i.Operand is MethodReference m && m.Name == name && m.DeclaringType.FullName == qualityParticles.FullName);
        check(qualityParticles.BaseType.FullName == "System.Object" && !qualityParticles.Interfaces.Any() &&
                CallsParticles(applyStage, "Apply") &&
                CallsParticles(runtime.Methods.Single(m => m.Name == "OnDisable"), "Clear") &&
                CallsParticles(runtime.Methods.Single(m => m.Name == "PrepareForRemoval"), "Clear"),
            "quality glitter is derived transient state cleaned up on disable/removal without new serialized behaviours");
        var nativeColor = gameAssembly.MainModule.GetType("QualityExtensions").Methods.Single(m => m.Name == "GetColor");
        check(applyParticles.Body.Instructions.Any(i => i.Operand is MethodReference m && m.FullName == nativeColor.FullName) &&
                applyParticles.Body.Instructions.Any(i => i.OpCode.Code == Code.Ldstr &&
                    Equals(i.Operand, "MiscPrefabs/prefab_effect_glitter_rarity")) && Calls(applyParticles, "Instantiate"),
            "quality glitter clones the native world prefab and uses the installed game's quality-color API");
        check(!Calls(applyParticles, "SetSortingOrder") && Calls(applyParticles, "set_sortingOrder") &&
                Calls(applyParticles, "Stop") && Calls(applyParticles, "set_startColor") && Calls(applyParticles, "Play"),
            "particle activation updates sorting directly and sets quality color before emission");
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
        var pickerScreen = editor.Methods.Single(m => m.Name == "DrawPickerScreen");
        check(Calls(pickerScreen, "DrawPickerOption"),
            "all picker rows use content-measured multiline rendering instead of single-line minimum-height buttons");
        var pickerRow = editor.Methods.Single(m => m.Name == "DrawPickerOption");
        check(pickerRow.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "GetRect" &&
                m.Parameters.Count >= 2 && m.Parameters[0].ParameterType.Name == "GUIContent" &&
                m.Parameters[1].ParameterType.Name == "GUIStyle") && Calls(pickerRow, "TextMeasurement") &&
                pickerRow.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "Button" &&
                    m.DeclaringType.FullName == "UnityEngine.GUI"),
            "picker labels reserve their measured content height before drawing the clickable row");
        check(!Calls(pickerRow, "MinHeight") && !Calls(pickerRow, "MaxHeight") && !Calls(pickerRow, "Height") &&
                !Calls(pickerRow, "FitEditorText"),
            "picker rows cannot shrink wrapped names and IDs to a forced height or truncate their text");
        var styleInstructions = editor.Methods.Single(m => m.Name == "EnsureTextStyles").Body.Instructions.ToList();
        int pickerStyleEnd = styleInstructions.FindIndex(i => i.OpCode.Code == Code.Stfld &&
            i.Operand is FieldReference f && f.Name == "pickerOptionStyle");
        // The initialization follows the previous style-field assignment.
        int pickerStyleStart = styleInstructions.Take(Math.Max(0, pickerStyleEnd)).ToList().FindLastIndex(i =>
            i.OpCode.Code == Code.Stfld && i.Operand is FieldReference);
        var pickerStyle = styleInstructions.Skip(pickerStyleStart + 1).Take(pickerStyleEnd - pickerStyleStart).ToArray();
        bool SetsStyle(string property, Code value) => pickerStyle.Any(i => i.Operand is MethodReference m &&
            m.Name == "set_" + property && i.Previous?.OpCode.Code == value);
        check(pickerStyleEnd >= 0 && SetsStyle("wordWrap", Code.Ldc_I4_1) && SetsStyle("richText", Code.Ldc_I4_0) &&
                SetsStyle("stretchHeight", Code.Ldc_I4_0) && pickerStyle.Any(i => i.Operand is MethodReference m &&
                    m.Name == "set_fixedHeight" && Equals(i.Previous?.Operand, 0f)) &&
                pickerStyle.Any(i => i.Operand is MethodReference m && m.Name == "set_alignment" &&
                    i.Previous?.OpCode.Code == Code.Ldc_I4_3),
            "picker row style wraps literal text, aligns left, and clears inherited fixed-height constraints");
        check(pickerScreen.Body.Instructions.Count(i => i.OpCode.Code == Code.Stfld && i.Operand is FieldReference f &&
                f.Name == "pickerScroll" && i.Previous?.Operand is MethodReference m && m.Name == "get_zero") == 3,
            "search changes reset picker scrolling as well as both page navigation actions");
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
