#nullable enable
using System;
using System.IO;

namespace SilverpineMods.CustomGrowables;

internal static class GrowableQuality
{
    internal const int UpgradeTiers = 6;

    internal static Quality Normalize(Quality quality) =>
        Enum.IsDefined(typeof(Quality), quality) ? quality : Quality.Normal;

    internal static Quality FromItem(Item item) =>
        item.TryGetItemComponent<IQualityItemComponent>(out var component)
            ? Normalize(component.Quality) : Quality.Normal;

    internal static void ApplyHarvest(Item item, Quality plantQuality, GrowableDefinition definition)
    {
        if (!item.TryGetItemComponent<IQualityItemComponent>(out _)) return;
        Quality quality = Normalize(plantQuality);
        if (definition.qualityBonusEnabled && quality < Quality.Legendary && BonusTotal(definition.qualityBonusChances) > 0)
        {
            int bonus = RollBonus(definition.qualityBonusChances, UnityEngine.Random.value);
            quality = (Quality)Math.Min((int)Quality.Legendary, (int)quality + bonus);
        }
        // Updates all quality-capable components, never adds quality to other items.
        item.SetQuality(quality);
    }

    internal static float BonusChance(float[]? chances, int index) =>
        chances == null ? (index == 0 ? .1f : 0f) : index < chances.Length ? chances[index] : 0f;

    internal static float BonusTotal(float[]? chances)
    {
        float total = 0;
        for (int i = 0; i < UpgradeTiers; i++) total += BonusChance(chances, i);
        return total;
    }

    internal static int RollBonus(float[]? chances, float roll)
    {
        float cumulative = 0;
        for (int i = 0; i < UpgradeTiers; i++)
        {
            float chance = BonusChance(chances, i);
            cumulative += chance;
            if (chance > 0 && (roll < cumulative || (roll == 1f && cumulative >= 1f - .000001f)))
                return i + 1;
        }
        return 0;
    }

    internal static void ValidateBonusChances(float[]? chances)
    {
        if (chances == null) return;
        if (chances.Length != UpgradeTiers)
            throw new InvalidDataException("growable.qualityBonusChances must contain six probabilities, for +1 through +6 tiers.");
        foreach (float chance in chances)
            if (!StageArtIdentity.Finite(chance) || chance < 0 || chance > 1)
                throw new InvalidDataException("growable.qualityBonusChances values must be finite numbers between 0 and 1.");
        if (BonusTotal(chances) > 1f + .000001f)
            throw new InvalidDataException("growable.qualityBonusChances must total 100% or less.");
    }
}
