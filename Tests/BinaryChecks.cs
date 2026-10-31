using Mono.Cecil;

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
