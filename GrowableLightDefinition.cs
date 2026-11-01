#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SilverpineMods.CustomGrowables;

// Matches Item Loader's light fields, with world-unit stage placement offsets.
// XY is relative to the planted tile; Z is relative to the native terrain plane.
internal sealed class GrowableLightDefinition
{
    private static readonly GrowableLightDefinition DewPreset = MorningDew();
    private static readonly GrowableLightDefinition LanternPreset = Lantern();
    public bool enabled = true;
    public float[]? color;
    public float radius = 1f;
    public float intensity = 1f;
    public bool flicker;
    public float offsetX;
    public float offsetY;
    public float offsetZ = -.55f;

    [JsonExtensionData]
    public IDictionary<string, JToken>? extensionData;

    [JsonIgnore]
    internal Color Color => color is { Length: 3 }
        ? new Color(color[0], color[1], color[2])
        : new Color(1f, .89451975f, .49411762f);

    internal static GrowableLightDefinition MorningDew() => new();
    internal static GrowableLightDefinition Lantern() => new()
    {
        color = new[] { 1f, .68f, .41f }, radius = 5f, intensity = 2f, flicker = true
    };

    internal string PresetName => Matches(DewPreset) ? "Morning Dew" : Matches(LanternPreset) ? "Lantern" : "Custom";

    internal bool Matches(GrowableLightDefinition other) =>
        enabled == other.enabled && Color.Equals(other.Color) && radius == other.radius &&
        intensity == other.intensity && flicker == other.flicker &&
        offsetX == other.offsetX && offsetY == other.offsetY && offsetZ == other.offsetZ;

    internal GrowableLightDefinition Copy() => new()
    {
        enabled = enabled, color = color?.ToArray(), radius = radius, intensity = intensity,
        flicker = flicker, offsetX = offsetX, offsetY = offsetY, offsetZ = offsetZ,
        extensionData = extensionData?.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone())
    };

    internal void Validate(string path)
    {
        if (color != null && (color.Length != 3 || color.Any(value =>
                !StageArtIdentity.Finite(value) || value < 0 || value > 1)))
            throw new InvalidDataException(path + ".color must contain three finite values from 0 to 1.");
        if (!StageArtIdentity.Finite(radius) || radius < .1f || radius > 50f)
            throw new InvalidDataException(path + ".radius must be between 0.1 and 50.");
        if (!StageArtIdentity.Finite(intensity) || intensity < 0 || intensity > 20f)
            throw new InvalidDataException(path + ".intensity must be between 0 and 20.");
        if (!StageArtIdentity.Finite(offsetX) || !StageArtIdentity.Finite(offsetY) ||
            !StageArtIdentity.Finite(offsetZ) || Math.Abs(offsetX) > 100 || Math.Abs(offsetY) > 100 || Math.Abs(offsetZ) > 10)
            throw new InvalidDataException(path + " requires finite X/Y offsets from -100 to 100 and depth from -10 to 10.");
    }
}
