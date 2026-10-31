#nullable enable
using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;

namespace SilverpineMods.CustomGrowables;

internal static class EditorValidation
{
    internal static bool TryFloat(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && StageArtIdentity.Finite(value);

    internal static bool TryInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    internal static void RequireEditable(GrowableDefinition definition)
    {
        if (definition.stages != null && definition.stages.Exists(stage => stage == null))
            throw new InvalidDataException("Stages contain a null entry. Repair the pack JSON and reload; no data has been changed.");
        if (definition.harvest != null && definition.harvest.Exists(output => output == null))
            throw new InvalidDataException("Harvest results contain a null entry. Repair the pack JSON and reload; no data has been changed.");
    }

    internal static bool SameDocument(string? original, string? current) =>
        JToken.DeepEquals(Parse(original), Parse(current));

    private static JToken Parse(string? json) => string.IsNullOrWhiteSpace(json) ? JValue.CreateNull() : JToken.Parse(json!);
}
