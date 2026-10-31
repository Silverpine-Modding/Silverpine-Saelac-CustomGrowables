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
    }
}
