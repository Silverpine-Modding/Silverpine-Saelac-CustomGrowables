#nullable enable

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SilverpineMods.CustomItemLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SilverpineMods.CustomGrowables;

internal sealed class GrowableDefinition
{
    public string? visibleName;
    public string? clock = nameof(GrowableClock.RealTimeHours);
    public bool playerPropertyOnly = true;
    public bool allowRoofed;
    public bool growsInWinter;
    public string[]? growthSeasons;
    public bool blocksMovement;
    public bool replaceExistingPlants = true;
    public int interactionTurns = 5;
    public bool perennial;
    public int matureStage = -1;
    public int regrowStage = -1;
    public float scale = 1f;
    public List<GrowableStageDefinition>? stages;
    public List<GrowableHarvestDefinition>? harvest;

    [JsonExtensionData]
    public IDictionary<string, JToken>? extensionData;

    [JsonIgnore]
    internal string QualifiedId = "";

    [JsonIgnore]
    internal string PrefabName = "";

    [JsonIgnore]
    internal string DisplayName = "";

    [JsonIgnore]
    internal GrowableClock Clock;

    [JsonIgnore]
    internal int GrowthSeasonMask = GrowthSeasons.WithoutWinter;

    [JsonIgnore]
    internal GameObject Template = null!;

    [JsonIgnore]
    internal readonly List<Sprite> OwnedSprites = new();
    [JsonIgnore]
    internal readonly List<Texture2D> OwnedTextures = new();

    [JsonIgnore]
    internal string PackDirectory = "";

    [JsonIgnore]
    internal string PackId = "";

    [JsonIgnore]
    internal string ItemId = "";

    internal bool IsHarvestReady(int stageIndex) =>
        stages != null && stageIndex >= stages.Count - 1;

    internal int EffectiveMatureStage => matureStage >= 0
        ? matureStage
        : perennial && regrowStage >= 0
            ? regrowStage
            : Math.Max(0, (stages?.Count ?? 1) - 1);

    internal bool HasReachedMaturityStage(int stageIndex) =>
        stageIndex >= EffectiveMatureStage;

    internal double GetStageDuration(int stageIndex)
    {
        if (stages == null || stageIndex < 0 || stageIndex >= stages.Count - 1)
            return 0f;
        float duration = stages[stageIndex].duration;
        return Clock == GrowableClock.RealTimeHours
            ? duration * 3600d
            : duration;
    }

    internal string GetDurationDescription()
    {
        if (stages == null)
            return "";
        double total = 0;
        for (int index = 0; index < stages.Count - 1; index++)
            total += stages[index].duration;
        string value = total.ToString(
            total == Math.Round(total) ? "0" : "0.##",
            System.Globalization.CultureInfo.InvariantCulture);
        return Clock == GrowableClock.RealTimeHours
            ? value + (total == 1f ? " real-world hour" : " real-world hours")
            : value + (total == 1f ? " game turn" : " game turns");
    }

    internal void Validate(string jsonPath, string qualifiedId)
    {
        QualifiedId = qualifiedId;
        PackDirectory = Path.GetDirectoryName(jsonPath)!;
        int separator = qualifiedId.IndexOf(':');
        if (separator <= 0 || separator >= qualifiedId.Length - 1)
            throw new InvalidDataException(
                $"Growable item ID '{qualifiedId}' is not a qualified CIL ID.");
        PackId = qualifiedId.Substring(0, separator);
        ItemId = qualifiedId.Substring(separator + 1);
        DisplayName = string.IsNullOrWhiteSpace(visibleName)
            ? qualifiedId
            : visibleName!.Trim();

        if (!Enum.TryParse(clock, ignoreCase: true, out GrowableClock parsed) ||
            !Enum.IsDefined(typeof(GrowableClock), parsed))
            throw new InvalidDataException(
                $"growable.clock must be {nameof(GrowableClock.RealTimeHours)} " +
                $"or {nameof(GrowableClock.GameTurns)}.");
        Clock = parsed;
        GrowthSeasonMask = GrowthSeasons.Resolve(growthSeasons, growsInWinter);
        if (growthSeasons != null)
            growthSeasons = GrowthSeasons.NamesFor(GrowthSeasonMask);

        if (stages == null || stages.Count < 2)
            throw new InvalidDataException(
                "growable.stages requires at least an initial and mature stage.");
        if (stages.Any(stage => stage == null)) throw new InvalidDataException("Stages cannot contain null entries.");
        if (stages.Where(s => !string.IsNullOrEmpty(s.cacheId)).GroupBy(s => s.cacheId, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidDataException("Stage cache IDs must be unique.");
        for (int index = 0; index < stages.Count; index++)
            stages[index].Validate(
                jsonPath,
                PackId,
                ItemId,
                index,
                finalStage: index == stages.Count - 1);

        if (harvest == null || harvest.Count == 0)
            throw new InvalidDataException(
                "growable.harvest requires at least one item result.");
        if (harvest.Any(output => output == null)) throw new InvalidDataException("Harvest outputs cannot contain null entries.");
        for (int index = 0; index < harvest.Count; index++)
            harvest[index].Validate(index);

        if (interactionTurns < 1 || interactionTurns > 10000)
            throw new InvalidDataException(
                "growable.interactionTurns must be between 1 and 10000.");
        if (regrowStage < -1 || regrowStage >= stages.Count - 1)
            throw new InvalidDataException(
                "growable.regrowStage must be -1, or the zero-based index of " +
                "a non-mature stage.");
        if (matureStage < -1 || matureStage >= stages.Count)
            throw new InvalidDataException(
                "growable.matureStage must be -1, or a valid zero-based " +
                "stage index.");
        if (perennial && regrowStage < 0)
            throw new InvalidDataException(
                "A perennial growable requires growable.regrowStage.");
        if (perennial && regrowStage < EffectiveMatureStage)
            throw new InvalidDataException(
                "A perennial growable cannot return to a stage before its " +
                "matureStage after harvest.");
        if (float.IsNaN(scale) || float.IsInfinity(scale) ||
            scale < 0.1f || scale > 10f)
            throw new InvalidDataException(
                "growable.scale must be between 0.1 and 10.");
    }
}

internal sealed class GrowableStageDefinition
{
    public string? cacheId;
    public string? artAssetId;
    public string? artOriginalName;
    public string? anchorSprite;
    public float offsetX;
    public float offsetY;
    public string? sprite;
    public string? image;
    public string? model;
    public float[]? rotation;
    public float zoom = 1f;
    public int resolution = 512;
    public float scale = 1f;
    public float duration;
    public float pixelsPerUnit = 32f;
    public float pivotX = 0.5f;
    public float pivotY = 0.5f;

    [JsonExtensionData]
    public IDictionary<string, JToken>? extensionData;

    [JsonIgnore]
    internal Sprite RuntimeSprite = null!;

    internal string CacheSuffix(int index) => StageArtIdentity.Suffix(cacheId, index, model, rotation, zoom, resolution);

    [JsonIgnore]
    internal Vector3 ModelRotation => rotation is { Length: 3 }
        ? new Vector3(rotation[0], rotation[1], rotation[2])
        : new Vector3(20f, 135f, 0f);

    internal void Validate(
        string jsonPath,
        string packId,
        string itemId,
        int index,
        bool finalStage)
    {
        bool hasSprite = !string.IsNullOrWhiteSpace(sprite);
        bool hasImage = !string.IsNullOrWhiteSpace(image);
        bool hasModel = !string.IsNullOrWhiteSpace(model);
        int artSourceCount = (hasSprite ? 1 : 0) +
            (hasImage ? 1 : 0) + (hasModel ? 1 : 0);
        if (artSourceCount != 1)
            throw new InvalidDataException(
                $"growable.stages[{index}] must specify exactly one of " +
                "sprite (base-game art), image (custom 2D art), or model " +
                "(one-direction GLB art).");
        if (!finalStage &&
            (duration <= 0f || float.IsNaN(duration) ||
             float.IsInfinity(duration)))
            throw new InvalidDataException(
                $"growable.stages[{index}].duration must be positive.");
        if (pixelsPerUnit <= 0f || float.IsNaN(pixelsPerUnit) ||
            float.IsInfinity(pixelsPerUnit))
            throw new InvalidDataException(
                $"growable.stages[{index}].pixelsPerUnit must be positive.");
        StageArtIdentity.ValidateOffset(offsetX, offsetY);
        if (cacheId != null && (!Guid.TryParseExact(cacheId, "N", out _)))
            throw new InvalidDataException("Stage cache ID must be a 32-character GUID.");
        if (!StageArtIdentity.Finite(pivotX) || !StageArtIdentity.Finite(pivotY) ||
            pivotX < 0f || pivotX > 1f || pivotY < 0f || pivotY > 1f)
            throw new InvalidDataException(
                $"growable.stages[{index}] pivot values must be between 0 and 1.");
        if (rotation != null &&
            (rotation.Length != 3 || rotation.Any(value =>
                float.IsNaN(value) || float.IsInfinity(value))))
            throw new InvalidDataException(
                $"growable.stages[{index}].rotation must contain three " +
                "finite values.");
        if (float.IsNaN(zoom) || float.IsInfinity(zoom) ||
            zoom < 0.1f || zoom > 10f)
            throw new InvalidDataException(
                $"growable.stages[{index}].zoom must be between 0.1 and 10.");
        if (resolution < 32 || resolution > 1024)
            throw new InvalidDataException(
                $"growable.stages[{index}].resolution must be between 32 " +
                "and 1024.");
        if (float.IsNaN(scale) || float.IsInfinity(scale) ||
            scale < 0.1f || scale > 10f)
            throw new InvalidDataException(
                $"growable.stages[{index}].scale must be between 0.1 and 10.");

        if (hasImage)
        {
            string packDirectory = Path.GetDirectoryName(jsonPath)!;
            string root = Path.GetFullPath(packDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string relativePath = image!;
            string fullPath = Path.GetFullPath(
                Path.Combine(packDirectory, relativePath));
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"growable.stages[{index}].image " +
                    "cannot leave its pack folder.");
            if (!File.Exists(fullPath))
                throw new FileNotFoundException(
                    $"Growable stage image does not exist: {fullPath}",
                    fullPath);
            string extension = Path.GetExtension(fullPath);
            if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"growable.stages[{index}].image must be PNG, JPG, or JPEG.");
            image = fullPath;
        }
        else if (hasModel)
        {
            string modelReference = model!.Trim()
                .Replace(Path.DirectorySeparatorChar, '/');
            model = modelReference;
            if (!Path.GetExtension(modelReference).Equals(
                    ".glb",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"growable.stages[{index}].model must be a GLB file.");
            string packDirectory = Path.GetDirectoryName(jsonPath)!;
            string sourcePath = CustomItemApi.ResolveGlbModelSourcePath(
                packDirectory,
                modelReference,
                requireExists: false);
            string cachePath = GlbCacheApi.GetReadableGlbCachePath(
                packDirectory,
                packId,
                itemId,
                CacheSuffix(index));
            if (!File.Exists(sourcePath) && !File.Exists(cachePath))
                throw new FileNotFoundException(
                    $"Growable stage {index} has no shared authoring GLB and " +
                    $"no distributable cached sprite: {cachePath}",
                    sourcePath);
        }
        else
        {
            sprite = sprite!.Trim();
        }
    }
}

internal sealed class GrowableHarvestDefinition
{
    public string? item;
    public int minimum = 1;
    public int maximum = 1;
    public float chance = 1f;

    [JsonExtensionData]
    public IDictionary<string, JToken>? extensionData;

    internal void Validate(int index)
    {
        if (string.IsNullOrWhiteSpace(item))
            throw new InvalidDataException(
                $"growable.harvest[{index}].item is required.");
        item = item.Trim();
        if (minimum < 0 || maximum < minimum || maximum > 10000)
            throw new InvalidDataException(
                $"growable.harvest[{index}] requires 0 <= minimum <= " +
                "maximum <= 10000.");
        if (chance < 0f || chance > 1f || float.IsNaN(chance))
            throw new InvalidDataException(
                $"growable.harvest[{index}].chance must be between 0 and 1.");
    }
}

internal enum GrowableClock
{
    RealTimeHours,
    GameTurns
}
