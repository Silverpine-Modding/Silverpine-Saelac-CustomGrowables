#nullable enable

using Newtonsoft.Json;
using HarmonyLib;
using Silverpine.ModdingTools;
using SilverpineMods.CustomItemLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace SilverpineMods.CustomGrowables;

internal static class GrowableRegistry
{
    private static readonly Dictionary<string, GrowableDefinition> ById =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Sprite> CustomSprites =
        new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, Sprite>? baseGameSprites;
    private static GameObject? nativeTreeRenderTemplate;
    private static bool warnedMissingTreeRenderTemplate;

    internal static int Count => ById.Count;

    internal static IReadOnlyList<string> GetBaseGameSpriteNames()
    {
        baseGameSprites ??= BuildBaseGameSpriteCatalog();
        return baseGameSprites.Keys
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool TryGetBaseGameSpriteSettings(
        string nameOrPath,
        out float pixelsPerUnit,
        out Vector2 normalizedPivot)
    {
        try
        {
            Sprite sprite = ResolveBaseGameSprite(nameOrPath);
            pixelsPerUnit = sprite.pixelsPerUnit;
            normalizedPivot = new Vector2(
                sprite.rect.width <= 0f
                    ? 0.5f
                    : sprite.pivot.x / sprite.rect.width,
                sprite.rect.height <= 0f
                    ? 0.5f
                    : sprite.pivot.y / sprite.rect.height);
            return true;
        }
        catch
        {
            pixelsPerUnit = 32f;
            normalizedPivot = new Vector2(0.5f, 0.5f);
            return false;
        }
    }

    internal static void RegisterAll(IReadOnlyList<CustomItemInfo> items)
    {
        foreach (CustomItemInfo item in items)
        {
            if (ById.ContainsKey(item.QualifiedId))
                continue;
            try
            {
                RegisterItem(item);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError(
                    $"Skipped growable '{item.QualifiedId}' in " +
                    $"'{item.SourceJsonPath}': {exception}");
            }
        }
    }

    internal static bool TryGet(
        string qualifiedId,
        out GrowableDefinition definition) =>
        ById.TryGetValue(NormalizeId(qualifiedId), out definition!);

    internal static bool TryCreateItem(string identifier, out Item item)
    {
        if (CustomItemApi.TryCreateItem(identifier, out item))
            return true;

        Item? template = ItemLibrary.Items.FirstOrDefault(candidate =>
            candidate != null && string.Equals(
                candidate.name,
                identifier,
                StringComparison.OrdinalIgnoreCase));
        if (template != null)
        {
            item = template.DeepClone();
            return true;
        }

        item = null!;
        return false;
    }

    internal static GameObject Plant(
        GrowableDefinition definition,
        Vector2Int position,
        Quality quality = Quality.Normal)
    {
        GameObject instance = UnityEngine.Object.Instantiate(
            definition.Template,
            position.ToVector3(definition.Template.transform.position.z),
            definition.Template.transform.rotation);
        instance.hideFlags = HideFlags.None;
        try
        {
            instance.GetComponent<CustomGrowable>().SetSeedQuality(quality);
            // Allocate before removing existing plants. This inactive instance
            // has not registered with Turfs yet.
            if (definition.replaceExistingPlants)
            {
                foreach (GameObject existing in Turfs.GetTurfGameObjects(position))
                {
                    CustomGrowable? growable = existing.GetComponent<CustomGrowable>();
                    if (growable != null) growable.PrepareForRemoval();
                }
                PlayerAbility_Construct.DestroyPlantAtPosition(position);
            }
            if (!instance.activeSelf) instance.SetActive(true);
        }
        catch { UnityEngine.Object.Destroy(instance); throw; }
        return instance;
    }

    private static void RegisterItem(CustomItemInfo itemInfo)
    {
        string? json = CustomItemApi.GetItemExtensionJson(
            itemInfo.QualifiedId,
            "growable");
        if (string.IsNullOrWhiteSpace(json))
            return;

        GrowableDefinition definition =
            JsonConvert.DeserializeObject<GrowableDefinition>(json!) ??
            throw new InvalidDataException(
                "The growable extension is not a JSON object.");
        string qualifiedId = itemInfo.QualifiedId;
        string? seedConflict = SeedCompatibility.FindConflict(itemInfo.Template);
        if (seedConflict != null)
            throw new InvalidDataException(SeedCompatibility.Explain(seedConflict));

        if (string.IsNullOrWhiteSpace(definition.visibleName))
            definition.visibleName = DeriveVisibleName(itemInfo.DisplayName);
        definition.Validate(itemInfo.SourceJsonPath, qualifiedId);
        definition.DisplayName = definition.visibleName!.Trim();
        definition.PrefabName = CreatePrefabName(qualifiedId);

        Plugin.Log.LogInfo($"Preparing growable '{qualifiedId}'.");
        ValidateHarvestItems(definition);
        SerializablePrefabRegistration? registration = null;
        List<int> pendingGlbStages;
        try
        {
            pendingGlbStages = ResolveSprites(definition, itemInfo.GetSprite());
            definition.Template = CreateTemplate(definition);
            registration = SerializablePrefabs.Register(
                Plugin.PluginGuid,
                definition.PrefabName,
                definition.Template,
                new SerializablePrefabOptions
                {
                    RequireNpcVisibleObject = true,
                    OnInstanceRestored = RestoreInstance
                });

            ById.Add(qualifiedId, definition);
            CustomItemApi.AddTemplateComponent(
                Plugin.PluginGuid,
                qualifiedId,
                new ItemComponent_CustomGrowableSeed(qualifiedId, GrowableQuality.FromItem(itemInfo.Template)));
        }
        catch
        {
            ById.Remove(qualifiedId);
            if (registration != null) registration.Unregister(destroyTemplate: true);
            else if (definition.Template != null) UnityEngine.Object.Destroy(definition.Template);
            foreach (Sprite sprite in definition.OwnedSprites) UnityEngine.Object.Destroy(sprite);
            foreach (Texture2D texture in definition.OwnedTextures) UnityEngine.Object.Destroy(texture);
            throw;
        }

        Plugin.Log.LogInfo(
            $"Registered growable '{qualifiedId}' with " +
            $"{definition.stages!.Count} stages.");
        if (pendingGlbStages.Count > 0)
            RenderPendingGlbStagesAsync(definition, pendingGlbStages);
    }

    private static List<int> ResolveSprites(
        GrowableDefinition definition,
        Sprite fallback)
    {
        var pendingGlbStages = new List<int>();
        for (int index = 0; index < definition.stages!.Count; index++)
        {
            GrowableStageDefinition stage = definition.stages[index];
            if (!string.IsNullOrWhiteSpace(stage.sprite))
            {
                stage.RuntimeSprite = CreateStageVisual(ResolveBaseGameSprite(stage.sprite!), stage, definition.scale);
                definition.OwnedSprites.Add(stage.RuntimeSprite);
            }
            else if (!string.IsNullOrWhiteSpace(stage.image))
            {
                stage.RuntimeSprite = CreateStageVisual(LoadCustomSprite(
                    stage.image!,
                    definition.QualifiedId + ":stage:" + index), stage, definition.scale);
                definition.OwnedSprites.Add(stage.RuntimeSprite);
            }
            else if (TryLoadGlbStageCache(
                         definition,
                         index,
                         stage,
                         out Sprite cached))
            {
                stage.RuntimeSprite = cached;
            }
            else
            {
                string modelPath = CustomItemApi.ResolveGlbModelSourcePath(
                    definition.PackDirectory,
                    stage.model!,
                    requireExists: false);
                if (!File.Exists(modelPath))
                    throw new InvalidDataException(
                        $"Growable '{definition.QualifiedId}' stage {index} " +
                        "has no source GLB and its CIL distribution cache " +
                        "could not be loaded. Render the stage preview before " +
                        "distributing the cache-only pack.");
                stage.RuntimeSprite = index > 0
                    ? definition.stages[index - 1].RuntimeSprite
                    : fallback;
                pendingGlbStages.Add(index);
            }
        }
        return pendingGlbStages;
    }

    private static bool TryLoadGlbStageCache(
        GrowableDefinition definition,
        int index,
        GrowableStageDefinition stage,
        out Sprite sprite)
    {
        sprite = null!;
        try
        {
            if (!CustomItemApi.TryLoadCachedGlbSprite(
                    definition.PackDirectory,
                    definition.PackId,
                    definition.ItemId,
                    stage.model!,
                    stage.ModelRotation,
                    stage.zoom,
                    stage.resolution,
                    definition.QualifiedId + ":stage:" + index + ":glb-cache",
                    stage.CacheSuffix(index),
                    out Sprite cached))
                return false;
            sprite = AdjustOwnedGlbSprite(cached, definition, stage);
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning(
                $"Ignoring invalid CIL growable cache for " +
                $"'{definition.QualifiedId}' stage {index}: " +
                exception.Message);
            return false;
        }
    }

    private static async void RenderPendingGlbStagesAsync(
        GrowableDefinition definition,
        IReadOnlyList<int> stageIndexes)
    {
        foreach (int index in stageIndexes)
        {
            GrowableStageDefinition stage = definition.stages![index];
            try
            {
                string spriteName = definition.QualifiedId + ":stage:" +
                    index + ":glb";
                Sprite rendered = await CustomItemApi.RenderAndCacheGlbSpriteAsync(
                    definition.PackDirectory,
                    definition.PackId,
                    definition.ItemId,
                    stage.model!,
                    stage.ModelRotation,
                    stage.zoom,
                    stage.resolution,
                    spriteName,
                    stage.CacheSuffix(index));
                Sprite adjusted = AdjustOwnedGlbSprite(rendered, definition, stage);
                stage.RuntimeSprite = adjusted;
                RefreshDefinitionArt(definition, index, adjusted);
                Plugin.Log.LogInfo(
                    $"Rendered one-direction GLB sprite for growable " +
                    $"'{definition.QualifiedId}' stage {index}.");
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError(
                    $"Could not render GLB sprite for growable " +
                    $"'{definition.QualifiedId}' stage {index}; its current " +
                    $"fallback sprite will remain active: {exception}");
            }
        }
    }

    internal static Sprite CreateStageVisual(Sprite source, GrowableStageDefinition stage, float baseScale)
    {
        bool native = !string.IsNullOrWhiteSpace(stage.sprite);
        float ppu = native ? source.pixelsPerUnit : stage.pixelsPerUnit;
        Vector2 pixelPivot = native ? source.pivot : new Vector2(stage.pivotX * source.rect.width, stage.pivotY * source.rect.height);
        // Matching native art is explicit and independent of adjacent stages or asynchronous GLB fallbacks.
        if (!native && !string.IsNullOrWhiteSpace(stage.anchorSprite))
        {
            Sprite anchor = ResolveBaseGameSprite(stage.anchorSprite!);
            pixelPivot = anchor.pivot / anchor.pixelsPerUnit * ppu;
        }
        float scale = baseScale * stage.scale;
        float width = source.rect.width / ppu * scale, height = source.rect.height / ppu * scale;
        if (!StageArtIdentity.Finite(width) || !StageArtIdentity.Finite(height) || width <= 0 || height <= 0)
            throw new InvalidDataException("Stage scale/PPU produces an unrenderable world size.");
        Vector2 pivot = new(
            StageArtIdentity.ShiftPivot(pixelPivot.x, source.rect.width, ppu, scale, stage.offsetX),
            StageArtIdentity.ShiftPivot(pixelPivot.y, source.rect.height, ppu, scale, stage.offsetY));
        Sprite adjusted = Sprite.Create(
            source.texture, source.rect, pivot, ppu, 0, SpriteMeshType.FullRect);
        adjusted.name = source.name;
        return adjusted;
    }

    private static Sprite AdjustOwnedGlbSprite(Sprite source, GrowableDefinition definition, GrowableStageDefinition stage)
    {
        try
        {
            Sprite adjusted = CreateStageVisual(source, stage, definition.scale);
            definition.OwnedSprites.Add(adjusted);
            definition.OwnedTextures.Add(source.texture);
            return adjusted;
        }
        catch { UnityEngine.Object.Destroy(source.texture); throw; }
        finally { UnityEngine.Object.Destroy(source); }
    }

    internal static void ValidateReferences(GrowableDefinition definition)
    {
        ValidateHarvestItems(definition);
        foreach (GrowableStageDefinition stage in definition.stages!)
        {
            if (!string.IsNullOrWhiteSpace(stage.sprite)) ResolveBaseGameSprite(stage.sprite!);
            if (!string.IsNullOrWhiteSpace(stage.anchorSprite)) ResolveBaseGameSprite(stage.anchorSprite!);
            if (!string.IsNullOrWhiteSpace(stage.image))
            {
                Texture2D texture = new(2, 2);
                try
                {
                    if (!texture.LoadImage(File.ReadAllBytes(stage.image!), false))
                        throw new InvalidDataException("Cannot decode stage image: " + stage.image);
                }
                finally { UnityEngine.Object.Destroy(texture); }
            }
            else if (!string.IsNullOrWhiteSpace(stage.model))
            {
                string source = CustomItemApi.ResolveGlbModelSourcePath(definition.PackDirectory, stage.model!, false);
                int index = definition.stages.IndexOf(stage);
                if (CustomItemApi.TryLoadCachedGlbSprite(definition.PackDirectory, definition.PackId, definition.ItemId,
                    stage.model!, stage.ModelRotation, stage.zoom, stage.resolution, "growable-validation",
                    stage.CacheSuffix(index), out Sprite cached))
                {
                    UnityEngine.Object.Destroy(cached.texture);
                    UnityEngine.Object.Destroy(cached);
                }
                else if (!File.Exists(source))
                    throw new InvalidDataException($"Stage {index} has no usable cached GLB sprite. Restore the model and render the stage before saving.");
            }
        }
    }

    private static void RefreshDefinitionArt(
        GrowableDefinition definition,
        int stageIndex,
        Sprite sprite)
    {
        if (stageIndex == 0 && definition.Template != null)
        {
            SpriteRenderer? renderer =
                definition.Template.GetComponent<SpriteRenderer>();
            if (renderer != null)
                renderer.sprite = sprite;
        }
        foreach (CustomGrowable growable in
                 Resources.FindObjectsOfTypeAll<CustomGrowable>())
            growable.RefreshArt(definition.QualifiedId);
    }

    internal static Sprite ResolveBaseGameSprite(string nameOrPath)
    {
        if (nameOrPath.Contains('/'))
        {
            Sprite? direct = Resources.Load<Sprite>(nameOrPath);
            if (direct != null)
                return direct;
        }

        baseGameSprites ??= BuildBaseGameSpriteCatalog();
        if (baseGameSprites.TryGetValue(nameOrPath, out Sprite sprite))
            return sprite;
        throw new InvalidDataException(
            $"Base-game sprite '{nameOrPath}' was not found. Use its exact " +
            "asset name from a loaded native prefab, or an explicit Resources " +
            "path when the sprite is not used by a prefab.");
    }

    private static Dictionary<string, Sprite> BuildBaseGameSpriteCatalog()
    {
        var result = new Dictionary<string, Sprite>(
            StringComparer.OrdinalIgnoreCase);
        foreach (SpriteRenderer renderer in
                 Resources.FindObjectsOfTypeAll<SpriteRenderer>())
        {
            if (renderer == null || renderer.GetComponent<CustomGrowable>() != null) continue;
            Sprite? sprite = renderer?.sprite;
            if (sprite != null && !string.IsNullOrWhiteSpace(sprite.name))
                result.TryAdd(sprite.name, sprite);
        }

        FieldInfo? matureSpriteField = AccessTools.Field(
            typeof(Crop),
            "matureSprite");
        if (matureSpriteField != null)
            foreach (Crop crop in Resources.FindObjectsOfTypeAll<Crop>())
            {
                Sprite? sprite = matureSpriteField.GetValue(crop) as Sprite;
                if (sprite != null && !string.IsNullOrWhiteSpace(sprite.name))
                    result.TryAdd(sprite.name, sprite);
            }

        Plugin.Log.LogInfo(
            $"Indexed {result.Count} base-game prefab sprite assets for " +
            "growable stage lookup.");
        return result;
    }

    private static Sprite LoadCustomSprite(
        string path,
        string spriteName)
    {
        // Stage geometry is applied separately; one source texture can serve every
        // stage/pivot/offset using this image. Check before file I/O or decoding.
        if (CustomSprites.TryGetValue(path, out Sprite cached)) return cached;
        string extension = Path.GetExtension(path);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Unsupported growable image format '{extension}'. Use PNG, " +
                "JPG, or JPEG.");

        Texture2D texture = new(2, 2, TextureFormat.ARGB32, false);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(path), false))
                throw new InvalidDataException("Unity could not decode growable image: " + path);
            texture.name = spriteName;
            texture.filterMode = FilterMode.Point;
            Sprite sprite = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 32f);
            sprite.name = spriteName;
            CustomSprites.Add(path, sprite);
            return sprite;
        }
        catch
        {
            UnityEngine.Object.Destroy(texture);
            throw;
        }
    }

    private static void ValidateHarvestItems(GrowableDefinition definition)
    {
        foreach (GrowableHarvestDefinition output in definition.harvest!)
            if (!TryCreateItem(output.item!, out _))
                throw new InvalidDataException(
                    $"Harvest item '{output.item}' does not name a registered " +
                    "custom item ID or base-game item.");
    }

    private static GameObject CreateTemplate(GrowableDefinition definition)
    {
        GameObject template = new(definition.PrefabName);
        try
        {
            template.SetActive(false);
            template.transform.localScale = Vector3.one * definition.scale * definition.stages![0].scale;

            SpriteRenderer renderer = template.AddComponent<SpriteRenderer>();
            renderer.sprite = definition.stages![0].RuntimeSprite;

            template.AddComponent<TurfRegistrar>();
            if (definition.blocksMovement)
            {
                TurfCollider collider = template.AddComponent<TurfCollider>();
                collider.passable = false;
                collider.abyss = false;
            }
            CustomGrowable growable = template.AddComponent<CustomGrowable>();
            growable.Configure(definition.QualifiedId);
            YSorter sorter = template.AddComponent<YSorter>();
            ApplyNativeTreeRendering(template, renderer, sorter);
            return template;
        }
        catch { UnityEngine.Object.Destroy(template); throw; }
    }

    private static void ApplyNativeTreeRendering(
        GameObject template,
        SpriteRenderer renderer,
        YSorter sorter)
    {
        nativeTreeRenderTemplate ??=
            Resources.Load<GameObject>("prefabs/prefab_tree_oak");
        nativeTreeRenderTemplate ??=
            Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(
                candidate =>
                    candidate != null &&
                    candidate.name.Equals(
                        "prefab_tree_oak",
                        StringComparison.OrdinalIgnoreCase) &&
                    candidate.GetComponent<SpriteRenderer>() != null &&
                    candidate.GetComponent<YSorter>() != null);

        if (nativeTreeRenderTemplate == null)
        {
            if (!warnedMissingTreeRenderTemplate)
            {
                warnedMissingTreeRenderTemplate = true;
                Plugin.Log.LogWarning(
                    "Could not resolve Silverpine's oak tree render style; " +
                    "growables will use Unity's default sorting layer.");
            }
            return;
        }

        SpriteRenderer? treeRenderer =
            nativeTreeRenderTemplate.GetComponent<SpriteRenderer>();
        YSorter? treeSorter = nativeTreeRenderTemplate.GetComponent<YSorter>();
        if (treeRenderer == null || treeSorter == null)
            return;

        renderer.sortingLayerID = treeRenderer.sortingLayerID;
        // New SpriteRenderers default to unlit. Share the native material so
        // every art source responds to the sun and nearby point lights.
        renderer.sharedMaterial = treeRenderer.sharedMaterial;
        template.layer = nativeTreeRenderTemplate.layer;
        sorter.offset = treeSorter.offset;
        Vector3 position = template.transform.position;
        position.z = nativeTreeRenderTemplate.transform.position.z;
        template.transform.position = position;
    }

    private static void RestoreInstance(GameObject instance)
    {
        CustomGrowable? growable = instance.GetComponent<CustomGrowable>();
        if (growable == null)
            throw new InvalidOperationException(
                $"Restored growable prefab '{instance.name}' has no " +
                $"{nameof(CustomGrowable)} component.");
        growable.RestoreInstance();
    }

    private static string DeriveVisibleName(string itemName)
    {
        string value = itemName.Trim();
        if (value.StartsWith("Bag of ", StringComparison.OrdinalIgnoreCase))
            value = value.Substring("Bag of ".Length);
        if (value.EndsWith(" Seeds", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(0, value.Length - " Seeds".Length);
        else if (value.EndsWith(" Seed", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(0, value.Length - " Seed".Length);
        else if (value.EndsWith(" Sapling", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(0, value.Length - " Sapling".Length);
        return string.IsNullOrWhiteSpace(value) ? itemName.Trim() : value;
    }

    private static string CreatePrefabName(string qualifiedId)
    {
        var sanitized = new StringBuilder();
        foreach (char character in qualifiedId)
            sanitized.Append(char.IsLetterOrDigit(character) ? character : '_');
        string safe = sanitized.ToString();
        if (safe.Length > 64)
            safe = safe.Substring(0, 64);

        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(qualifiedId));
        var suffix = new StringBuilder(12);
        for (int index = 0; index < 6; index++)
            suffix.Append(hash[index].ToString("x2"));
        return "customgrowable_crop_" + safe + "_" + suffix;
    }

    private static string NormalizeId(string value)
    {
        string normalized = value?.Trim() ?? "";
        return normalized.StartsWith("custom:", StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring("custom:".Length)
            : normalized;
    }
}
