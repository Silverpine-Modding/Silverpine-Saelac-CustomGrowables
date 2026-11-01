#nullable enable
using System;
using System.Linq;
using UnityEngine;

namespace SilverpineMods.CustomGrowables;

internal sealed partial class GrowableEditorUI
{
    private void DrawStageLight(GrowableStageDefinition stage, int index)
    {
        GUILayout.Space(12);
        GUILayout.Label("Stage light / glow", editorHeading!);
        DrawToggle("Emit light in this stage", stage.light?.enabled == true, value =>
        {
            stage.light ??= GrowableLightDefinition.MorningDew();
            stage.light.enabled = value;
            ClearStageLightInputs(index);
            MarkDirty();
            GUIUtility.ExitGUI();
        });
        if (stage.light?.enabled != true)
        {
            GUILayout.Label("Optional area light. Other stages keep their own settings.", editorMuted!);
            return;
        }

        GrowableLightDefinition light = stage.light;
        string prefix = $"stage.{index}.light.";
        DrawChoice("Light preset", light.PresetName, new[] { "Custom", "Morning Dew", "Lantern" }, value =>
        {
            if (value == "Custom") return;
            GrowableLightDefinition preset = value == "Morning Dew"
                ? GrowableLightDefinition.MorningDew() : GrowableLightDefinition.Lantern();
            // Reset known light settings, retaining any future/third-party data.
            preset.extensionData = light.extensionData;
            stage.light = preset;
            ClearStageLightInputs(index);
            MarkDirty();
        });
        GUILayout.Label("RGB channels: 0–1. Radius is in tiles, independent of the artwork's scale.", editorMuted!);
        Color color = light.Color;
        DrawFloat("Red", prefix + "red", color.r, value => SetLightColor(light, 0, value));
        DrawFloat("Green", prefix + "green", color.g, value => SetLightColor(light, 1, value));
        DrawFloat("Blue", prefix + "blue", color.b, value => SetLightColor(light, 2, value));
        DrawFloat("Light radius (0.1–50)", prefix + "radius", light.radius, value => Set(ref light.radius, value));
        DrawFloat("Intensity (0–20)", prefix + "intensity", light.intensity, value => Set(ref light.intensity, value));
        DrawToggle("Lantern flicker", light.flicker, value => Set(ref light.flicker, value));
        GUILayout.Label("Light position relative to the planted tile, not the sprite pivot. X right / Y up.", editorMuted!);
        DrawFloat("Light offset X", prefix + "x", light.offsetX, value => Set(ref light.offsetX, value));
        DrawFloat("Light offset Y", prefix + "y", light.offsetY, value => Set(ref light.offsetY, value));
        DrawFloat("Light depth from terrain (Z)", prefix + "z", light.offsetZ, value => Set(ref light.offsetZ, value));
        GUILayout.Label("Depth is relative to the ground, not tree sorting depth. Native -0.55 puts the light at world Z 0.45. " +
            "Morning Dew is a small warm glow; Lantern matches Item Loader's defaults. " +
            "The artwork preview does not simulate world lighting.", editorMuted!);
    }

    private void SetLightColor(GrowableLightDefinition light, int channel, float value)
    {
        Color previous = light.Color;
        if (light.color == null || light.color.Length != 3)
            light.color = new[] { previous.r, previous.g, previous.b };
        if (light.color[channel] == value) return;
        light.color[channel] = value;
        MarkDirty();
    }

    private void ClearStageLightInputs(int index)
    {
        string prefix = $"stage.{index}.light.";
        foreach (string key in numberText.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
        {
            numberText.Remove(key);
            invalidNumbers.Remove(key);
        }
    }
}
