#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace SilverpineMods.CustomGrowables;

public sealed class ItemComponent_CustomGrowableSeed : ItemComponent
{
    private string growableId = "";
    private bool planting;

    public ItemComponent_CustomGrowableSeed()
    {
    }

    internal ItemComponent_CustomGrowableSeed(string growableId)
    {
        this.growableId = growableId;
    }

    public override void Serialize(BinaryWriter binaryWriter)
    {
        var storage = new VersionSafeStorage();
        storage.AddToStorage("growableId", growableId);
        storage.Serialize(binaryWriter);
    }

    public override void Deserialize(BinaryReader binaryReader)
    {
        var storage = new VersionSafeStorage();
        storage.Deserialize(binaryReader);
        growableId = storage.GetString("growableId", growableId);
    }

    public override string GetDescriptionSuffix()
    {
        if (!GrowableRegistry.TryGet(
                growableId,
                out GrowableDefinition definition))
            return "";

        string description =
            $"Grows into {definition.DisplayName.ToLowerInvariant()} in " +
            $"<color=\"yellow\">{definition.GetDurationDescription()}</color>.";
        if (definition.playerPropertyOnly)
            description += "\nCan only be planted on your own property.";
        description += definition.GrowthSeasonMask == GrowthSeasons.All
            ? "\nGrows in all seasons."
            : "\nGrows during: " + string.Join(", ", GrowthSeasons.NamesFor(definition.GrowthSeasonMask)) +
                ". Growth and regrowth pause in other seasons.";
        return description;
    }

    public override void OnUse()
    {
        Player player = Player.Instance;
        Item seed = item;
        Inventory? source = seed?.inventory;
        if (planting || player == null || WorldInfoManager.Instance == null ||
            seed == null || source == null || !source.ContainsItem(seed)) return;
        if (!GrowableRegistry.TryGet(
                growableId,
                out GrowableDefinition definition))
        {
            UpperNotificationUI.Instance.OneOff(
                "This growable definition is unavailable.");
            return;
        }

        Vector2Int position = player.transform.GetVector2IntPosition();
        bool propertyAllowed = !definition.playerPropertyOnly ||
            WorldInfoManager.Instance.IsOnPlayerOwnedPlot(position);
        bool roofAllowed = definition.allowRoofed ||
            !TileTags.HasTileTag(position, TileTag.Roofed);
        if (!propertyAllowed || !roofAllowed)
        {
            UpperNotificationUI.Instance.OneOff("You can't do that here.");
            return;
        }

        AudioPlayer.Instance.PlayLoopAtPosition(
            "sound_loop_shoveling",
            1f,
            position);
        planting = true;
        bool completed = false;
        bool completing = false;
        void Finish()
        {
            if (completed) return;
            completed = true;
            planting = false;
            if (AudioPlayer.Instance != null) AudioPlayer.Instance.StopLoop(position);
        }
        try { player.StartLongInteraction(
            () =>
            {
                if (completed || completing) return;
                completing = true;
                try
                {
                    if (player == null || player != Player.Instance || !source.ContainsItem(seed) || seed.inventory != source ||
                        player.transform.GetVector2IntPosition() != position || WorldInfoManager.Instance == null) return;
                    if ((definition.playerPropertyOnly && !WorldInfoManager.Instance.IsOnPlayerOwnedPlot(position)) ||
                        (!definition.allowRoofed && TileTags.HasTileTag(position, TileTag.Roofed))) return;
                    GrowableRegistry.Plant(definition, position);
                    seed.RemoveItemFromInventory();
                }
                catch (Exception error) { Plugin.Log.LogError("Planting failed: " + error); }
                finally { Finish(); }
            },
            definition.interactionTurns,
            null,
            Finish); }
        catch
        {
            Finish();
            throw;
        }
    }
}
