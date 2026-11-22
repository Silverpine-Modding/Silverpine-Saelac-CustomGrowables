#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SilverpineMods.CustomGrowables;

// No global inventory/Instantiate patches, no per-frame scans, no new save data.
internal static class NativeGrowableHooks
{
    private static readonly FieldInfo CropMature = typeof(Crop).GetField("mature", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo CropName = typeof(Crop).GetField("visibleName", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly ConditionalWeakTable<object, HarvestStamp> Harvests = new();
    private sealed class HarvestStamp { public bool Busy, Delivered; }

    internal static bool TryGetInfo(GameObject instance, out GrowableInfo info)
    {
        info = null!;
        if (instance.TryGetComponent<Crop>(out var crop))
        {
            bool mature = CropMature.GetValue(crop) is true;
            info = Info(instance, GrowableKind.NativeCrop, Quality.Normal, mature ? 1 : 0, 2, mature,
                (string?)CropName.GetValue(crop));
            return true;
        }
        if (instance.TryGetComponent<InGameTimeCrop>(out _))
        { info = Info(instance, GrowableKind.NativeTimedCrop, Quality.Normal, 0, 2, false); return true; }
        if (instance.TryGetComponent<ResourceNode>(out var herb) && herb.resourceNodeType == ResourceNodeType.Herb)
        { info = Info(instance, GrowableKind.NativeHerb, GrowableQuality.Normalize(herb.quality), 0, 1, true); return true; }
        if (instance.name.StartsWith("prefab_crop_", StringComparison.OrdinalIgnoreCase))
        { info = Info(instance, GrowableKind.NativePlant, Quality.Normal, 0, 1, false); return true; }
        return false;
    }

    internal static GrowableInfo Info(GameObject instance, GrowableKind kind, Quality quality, int stage, int stages,
        bool ready, string? displayName = null)
    {
        string name = instance.name;
        if (name.EndsWith("(Clone)", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 7).TrimEnd();
        return new GrowableInfo(instance, "native:" + name, displayName ?? name, kind, quality, stage, stages, ready);
    }

    // Replaces only the TryAddItem call inside Crop.HarvestInto/ResourceNode.AddItem.
    // Native strange-plant randomization and tool-quality limits have already run.
    internal static bool Deliver(Inventory inventory, Item item, MonoBehaviour source)
    {
        if (!GrowableApi.HasHarvestListeners || SerializationManager.loadingSave ||
            !TryGetInfo(source.gameObject, out var info)) return inventory.TryAddItem(item);
        HarvestStamp stamp = Harvests.GetValue(source, _ => new HarvestStamp());
        if (stamp.Busy || stamp.Delivered) return false;
        stamp.Busy = true;
        try
        {
            List<Item> outputs;
            try { outputs = GrowableApi.PrepareOutput(info, inventory, item); }
            catch (Exception error)
            {
                Plugin.Log.LogWarning("Native harvest modifiers failed; using the original output: " + error);
                outputs = new List<Item> { item };
            }
            var added = new List<Item>();
            foreach (Item output in outputs)
            {
                bool accepted;
                try { accepted = inventory.TryAddItem(output); }
                catch (Exception error)
                {
                    accepted = inventory.ContainsItem(output);
                    Plugin.Log.LogWarning("Native harvest inventory callback failed: " + error.Message);
                }
                if (accepted) added.Add(output);
            }
            // Native capacity behavior is preserved: rejected items are not
            // magically dropped or reported as granted. Award XP from this event.
            stamp.Delivered = added.Count > 0 || outputs.Count == 0;
            GrowableApi.NotifyHarvested(info, inventory, added, new List<Item>());
            return added.Count > 0;
        }
        finally { stamp.Busy = false; }
    }

    internal static bool CanHarvest(MonoBehaviour source) =>
        !Harvests.TryGetValue(source, out var stamp) || (!stamp.Busy && !stamp.Delivered);

    [ThreadStatic] private static PlantingScope? planting;
    internal sealed class PlantingScope
    {
        internal PlantingScope? Previous;
        internal GameObject? Actor, Spawned;
        internal Item? Seed;
        internal Inventory? Inventory;
        internal bool SeedWasPresent, Ended;
    }

    internal static PlantingScope BeginPlanting(object closure)
    {
        var scope = new PlantingScope { Previous = planting, Actor = Player.Instance == null ? null : Player.Instance.gameObject };
        foreach (FieldInfo field in closure.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            if (field.FieldType == typeof(ItemComponent_Seeds) && field.GetValue(closure) is ItemComponent_Seeds seeds)
            { scope.Seed = seeds.item; break; }
        scope.Inventory = scope.Seed?.inventory;
        scope.SeedWasPresent = scope.Seed != null && scope.Inventory != null && scope.Inventory.ContainsItem(scope.Seed);
        planting = scope;
        return scope;
    }

    internal static GameObject InstantiateSeed(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        GameObject spawned = UnityEngine.Object.Instantiate(prefab, position, rotation);
        if (planting != null) planting.Spawned = spawned;
        return spawned;
    }

    internal static void EndPlanting(PlantingScope? scope)
    {
        if (scope == null || scope.Ended) return;
        scope.Ended = true;
        planting = scope.Previous;
        // Native inventory callbacks may throw after the debit. The committed
        // facts (spawned plant + removed seed) determine success, not that error.
        if (scope.Spawned != null && scope.SeedWasPresent &&
            !scope.Inventory!.ContainsItem(scope.Seed!))
            GrowableApi.NotifyPlanted(scope.Spawned, scope.Actor, scope.Seed);
    }

    internal static GameObject InstantiateMature(GameObject prefab, Vector3 position, Quaternion rotation, InGameTimeCrop source)
    {
        GrowableInfo? before = null;
        if (GrowableApi.HasGrowthListeners) TryGetInfo(source.gameObject, out before!);
        GameObject spawned = UnityEngine.Object.Instantiate(prefab, position, rotation);
        if (before != null)
        {
            // A native sapling can become a Tree, grass, or decorative foliage,
            // not necessarily a harvestable herb. Do not imply a harvest action.
            if (!TryGetInfo(spawned, out var after))
                after = Info(spawned, GrowableKind.NativePlant, Quality.Normal, 1, 2, false);
            GrowableApi.NotifyGrowth(before, after, GrowableGrowthReason.Growth);
        }
        return spawned;
    }
}
