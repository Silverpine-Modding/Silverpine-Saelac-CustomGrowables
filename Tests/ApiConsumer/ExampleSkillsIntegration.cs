using System;
using SilverpineMods.CustomGrowables;
using UnityEngine;

namespace ExampleSkillsMod;

// Compile-checked example, not an installed plugin. Your skills system supplies
// actor eligibility, levels, XP storage, and optional growth tracking.
public sealed class ExampleSkillsIntegration : IDisposable
{
    private readonly Func<GameObject, bool> acceptsActor;
    private readonly Func<GameObject, int> getLevel;
    private readonly Action<GameObject, float> awardXp;
    private readonly Action<GrowableGrowthEventArgs>? observeGrowth;

    public ExampleSkillsIntegration(Func<GameObject, bool> acceptsActor,
        Func<GameObject, int> getLevel, Action<GameObject, float> awardXp,
        Action<GrowableGrowthEventArgs>? observeGrowth = null)
    {
        this.acceptsActor = acceptsActor;
        this.getLevel = getLevel;
        this.awardXp = awardXp;
        this.observeGrowth = observeGrowth;
        GrowableApi.Planted += OnPlanted;
        GrowableApi.PreparingHarvestItem += OnPreparingHarvestItem;
        GrowableApi.Harvested += OnHarvested;
        GrowableApi.GrowthChanged += OnGrowth;
    }

    private bool Accepts(GameObject? actor) => actor != null && acceptsActor(actor);

    private void OnPlanted(object? sender, GrowablePlantedEventArgs e)
    {
        if (Accepts(e.Actor)) awardXp(e.Actor!, 1f);
    }

    private void OnPreparingHarvestItem(object? sender, GrowableHarvestItemEventArgs e)
    {
        if (!Accepts(e.Actor)) return;
        int level = getLevel(e.Actor!);
        // Example policy: add one copy at level 10; improve quality at level 20.
        // Respect another mod's deliberate suppression of this output.
        if (level >= 10 && e.Count > 0 && e.Count < GrowableApi.MaximumOutputCopies) e.Count++;
        if (level >= 20 && e.SupportsQuality)
            e.Quality = (Quality)Math.Min((int)Quality.Legendary, (int)e.Quality + 1);
    }

    private void OnHarvested(object? sender, GrowableHarvestedEventArgs e)
    {
        // Once per successful harvest, not per generated copy or modifier call.
        if (Accepts(e.Actor)) awardXp(e.Actor!, 5f);
    }

    private void OnGrowth(object? sender, GrowableGrowthEventArgs e) => observeGrowth?.Invoke(e);

    // Call only when the integration truly ends, not when Silverpine destroys
    // the BepInEx bootstrap host during startup. Retain one instance per session.
    public void Dispose()
    {
        GrowableApi.Planted -= OnPlanted;
        GrowableApi.PreparingHarvestItem -= OnPreparingHarvestItem;
        GrowableApi.Harvested -= OnHarvested;
        GrowableApi.GrowthChanged -= OnGrowth;
    }
}
