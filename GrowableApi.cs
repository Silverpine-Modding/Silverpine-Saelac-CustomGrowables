#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SilverpineMods.CustomGrowables;

public enum GrowableKind { Custom, NativeCrop, NativeTimedCrop, NativeHerb, NativePlant }

/// <summary>A point-in-time view. Object references are session-local, not save IDs.</summary>
public sealed class GrowableInfo
{
    public GameObject GameObject { get; }
    public string Id { get; }
    public string DisplayName { get; }
    public GrowableKind Kind { get; }
    public bool IsCustom => Kind == GrowableKind.Custom;
    public Vector2Int Position { get; }
    public Quality Quality { get; }
    public int StageIndex { get; }
    public int StageCount { get; }
    public bool IsHarvestReady { get; }
    public bool IsPerennial { get; }
    internal GrowableInfo(GameObject instance, string id, string name, GrowableKind kind,
        Quality quality, int stage, int stages, bool ready, bool perennial = false)
    {
        GameObject = instance; Id = id; DisplayName = name; Kind = kind;
        Position = instance.transform.GetVector2IntPosition(); Quality = quality;
        StageIndex = stage; StageCount = stages; IsHarvestReady = ready; IsPerennial = perennial;
    }
}

public sealed class GrowablePlantedEventArgs : EventArgs
{
    public GrowableInfo Plant { get; }
    public GameObject? Actor { get; }
    public Item? Seed { get; }
    internal GrowablePlantedEventArgs(GrowableInfo plant, GameObject? actor, Item? seed)
    { Plant = plant; Actor = actor; Seed = seed; }
}

public enum GrowableGrowthReason { Growth, OfflineCatchUp, Regrowth }

public sealed class GrowableGrowthEventArgs : EventArgs
{
    public GrowableInfo Before { get; }
    public GrowableInfo After { get; }
    public GrowableGrowthReason Reason { get; }
    internal GrowableGrowthEventArgs(GrowableInfo before, GrowableInfo after, GrowableGrowthReason reason)
    { Before = before; After = after; Reason = reason; }
}

/// <summary>One rolled output, after normal quality/tool rules, before delivery.
/// Only Count and Quality are supported mutation points. This is not an XP event.</summary>
public sealed class GrowableHarvestItemEventArgs : EventArgs
{
    public GrowableInfo Plant { get; }
    public GameObject? Actor { get; }
    public Item Item { get; }
    public bool SupportsQuality { get; }
    public int Count { get; set; } = 1;
    public Quality Quality { get; set; }
    internal GrowableHarvestItemEventArgs(GrowableInfo plant, Inventory inventory, Item item)
    {
        Plant = plant; Actor = inventory.owner; Item = item;
        SupportsQuality = item.TryGetItemComponent<IQualityItemComponent>(out var component);
        Quality = SupportsQuality ? GrowableQuality.Normalize(component.Quality) : Quality.Normal;
    }
}

/// <summary>One completed harvest with verified delivered outputs; inventory and
/// ground-drop lists are disjoint. Do not mutate the returned item instances.</summary>
public sealed class GrowableHarvestedEventArgs : EventArgs
{
    public GrowableInfo Plant { get; }
    public GameObject? Actor { get; }
    public Inventory Inventory { get; }
    public IReadOnlyList<Item> InventoryItems { get; }
    public IReadOnlyList<Item> DroppedItems { get; }
    public int ItemCount => InventoryItems.Count + DroppedItems.Count;
    internal GrowableHarvestedEventArgs(GrowableInfo plant, Inventory inventory, List<Item> added, List<Item> dropped)
    {
        Plant = plant; Actor = inventory.owner; Inventory = inventory;
        InventoryItems = Array.AsReadOnly(added.ToArray()); DroppedItems = Array.AsReadOnly(dropped.ToArray());
    }
}

/// <summary>Main-thread API for skills mods. Subscribers survive destruction of
/// BepInEx's bootstrap host; unsubscribe only when your integration truly ends.</summary>
public static class GrowableApi
{
    public const int ApiVersion = 1;
    public const int MaximumOutputCopies = 100;
    public static event EventHandler<GrowablePlantedEventArgs>? Planted;
    public static event EventHandler<GrowableGrowthEventArgs>? GrowthChanged;
    public static event EventHandler<GrowableHarvestItemEventArgs>? PreparingHarvestItem;
    public static event EventHandler<GrowableHarvestedEventArgs>? Harvested;

    public static bool TryGetPlant(GameObject instance, out GrowableInfo info)
    {
        info = null!;
        if (instance == null) return false;
        if (instance.TryGetComponent<CustomGrowable>(out var custom)) return custom.TryGetApiInfo(out info);
        return NativeGrowableHooks.TryGetInfo(instance, out info);
    }

    internal static bool HasGrowthListeners => GrowthChanged != null;
    internal static bool HasHarvestListeners => PreparingHarvestItem != null || Harvested != null;

    internal static void NotifyPlanted(GameObject plant, GameObject? actor, Item? seed)
    {
        if (Planted == null || SerializationManager.loadingSave || !TryGetPlant(plant, out var info)) return;
        Raise(Planted, new GrowablePlantedEventArgs(info, actor, seed));
    }

    internal static void NotifyGrowth(GrowableInfo? before, GrowableInfo? after, GrowableGrowthReason reason)
    {
        if (GrowthChanged == null || before == null || after == null || SerializationManager.loadingSave) return;
        Raise(GrowthChanged, new GrowableGrowthEventArgs(before, after, reason));
    }

    internal static List<Item> PrepareOutput(GrowableInfo plant, Inventory inventory, Item item)
    {
        if (PreparingHarvestItem == null || SerializationManager.loadingSave) return new List<Item> { item };
        var args = new GrowableHarvestItemEventArgs(plant, inventory, item);
        // Isolate failed handlers and their supported mutations. Later subscribers
        // still run, and one broken skills mod cannot consume a crop with no loot.
        foreach (EventHandler<GrowableHarvestItemEventArgs> handler in PreparingHarvestItem.GetInvocationList())
        {
            int count = args.Count; Quality quality = args.Quality;
            try { handler(null, args); }
            catch (Exception error)
            { args.Count = count; args.Quality = quality; Warn(error); }
            args.Count = Math.Max(0, Math.Min(MaximumOutputCopies, args.Count));
            if (!Enum.IsDefined(typeof(Quality), args.Quality)) args.Quality = quality;
        }
        if (args.SupportsQuality) item.SetQuality(args.Quality);
        var outputs = new List<Item>(args.Count);
        for (int i = 0; i < args.Count; i++) outputs.Add(i == 0 ? item : item.DeepClone());
        return outputs;
    }

    internal static void NotifyHarvested(GrowableInfo plant, Inventory inventory, List<Item> added, List<Item> dropped)
    {
        if (Harvested == null || SerializationManager.loadingSave || added.Count + dropped.Count == 0) return;
        Raise(Harvested, new GrowableHarvestedEventArgs(plant, inventory, added, dropped));
    }

    private static void Raise<T>(EventHandler<T>? handlers, T args) where T : EventArgs
    {
        if (handlers == null) return;
        foreach (EventHandler<T> handler in handlers.GetInvocationList())
            try { handler(null, args); } catch (Exception error) { Warn(error); }
    }

    private static void Warn(Exception error) => Plugin.Log.LogWarning("Growable API subscriber failed: " + error);
}
