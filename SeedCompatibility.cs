#nullable enable

using System;

namespace SilverpineMods.CustomGrowables;

internal static class SeedCompatibility
{
    internal static string? FindConflict(Item template)
    {
        // Item.Use invokes every component, while Sticky prevents Use entirely.
        // Check inherited overrides too; ignore our already-attached seed component.
        foreach (ItemComponent component in template.GetItemComponents<ItemComponent>())
        {
            if (component is ItemComponent_CustomGrowableSeed) continue;
            Type type = component.GetType();
            if (component is ItemComponent_Sticky ||
                type.GetMethod(nameof(ItemComponent.OnUse), Type.EmptyTypes)?.DeclaringType != typeof(ItemComponent))
                return type.Name;
        }
        return null;
    }

    internal static string Explain(string component) =>
        "Growable behavior conflicts with " + component + ". " +
        "Use a component-free clone or a separate seed item without another use behavior.";
}
