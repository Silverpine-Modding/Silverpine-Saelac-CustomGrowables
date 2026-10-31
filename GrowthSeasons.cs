#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace SilverpineMods.CustomGrowables;

internal static class GrowthSeasons
{
    // Matches the game's Season enum, including the local biome overrides.
    internal const int All = 15;
    internal const int WithoutWinter = 7;
    internal static readonly string[] Names = { "Spring", "Summer", "Autumn", "Winter" };

    internal static int Resolve(string[]? seasons, bool legacyWinter, bool allowEmpty = false)
    {
        if (seasons == null) return legacyWinter ? All : WithoutWinter;
        int mask = 0;
        foreach (string? name in seasons)
        {
            int index = -1;
            string? trimmed = name?.Trim();
            for (int i = 0; i < Names.Length; i++)
                if (Names[i].Equals(trimmed, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
            if (index < 0)
                throw new InvalidDataException("growable.growthSeasons accepts only Spring, Summer, Autumn, and Winter.");
            mask |= 1 << index;
        }
        if (mask == 0 && !allowEmpty)
            throw new InvalidDataException("Select at least one growth season.");
        return mask;
    }

    internal static bool Allows(int mask, int season) =>
        season >= 0 && season < Names.Length && (mask & (1 << season)) != 0;

    internal static string[] NamesFor(int mask)
    {
        var names = new List<string>(4);
        for (int i = 0; i < Names.Length; i++)
            if (Allows(mask, i)) names.Add(Names[i]);
        return names.ToArray();
    }
}
