#nullable enable
using System;

namespace SilverpineMods.CustomGrowables;

internal static class GrowthProgress
{
    // Doubles keep small fixed-update increments working on multi-day growth stages.
    // Work is bounded by the stage count, even after a very long offline interval.
    internal static bool Advance(GrowableDefinition definition, ref int stageIndex,
        ref double progress, ref bool matured, double amount)
    {
        if (amount <= 0 || double.IsNaN(amount) || double.IsInfinity(amount) ||
            definition.IsHarvestReady(stageIndex)) return false;
        progress += amount;
        bool changed = false;
        while (!definition.IsHarvestReady(stageIndex))
        {
            double duration = definition.GetStageDuration(stageIndex);
            if (progress < duration) break;
            progress -= duration;
            stageIndex++;
            if (definition.HasReachedMaturityStage(stageIndex)) matured = true;
            changed = true;
        }
        if (definition.IsHarvestReady(stageIndex)) progress = 0;
        return changed;
    }
}
