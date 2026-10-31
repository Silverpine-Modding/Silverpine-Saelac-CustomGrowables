#nullable enable
using UnityEngine;

namespace SilverpineMods.CustomGrowables;

// Transient child only: no new serializable component and no OnDestroy dependency.
// The saved growth stage and current pack settings are the source of truth.
internal sealed class StageLight
{
    private Light? light;
    private GrowableLightDefinition? applied;

    internal void Apply(Transform parent, GrowableLightDefinition settings)
    {
        if (!settings.enabled) { Clear(); return; }
        if (light == null || applied == null || !applied.Matches(settings))
        {
            Clear();
            var child = new GameObject("CustomGrowableStageLight");
            child.SetActive(false);
            child.transform.SetParent(parent, false);
            try
            {
                light = child.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = settings.Color;
                light.range = settings.radius;
                light.intensity = settings.intensity;
                light.shadows = LightShadows.None;
                if (settings.flicker) child.AddComponent<LightFlickerer>();
                applied = settings.Copy();
                Position(parent, settings);
                child.SetActive(true);
            }
            catch
            {
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
                light = null; applied = null;
                throw;
            }
        }
        else Position(parent, settings);
    }

    private void Position(Transform parent, GrowableLightDefinition settings)
    {
        // Offsets and radius are in world units, independent of plant/stage scale.
        light!.transform.position = parent.position + new Vector3(settings.offsetX, settings.offsetY, settings.offsetZ);
        Vector3 scale = parent.lossyScale;
        light.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
    }

    internal void Clear()
    {
        if (light != null)
        {
            // Destroy is deferred; deactivate now to avoid a one-frame double glow.
            light.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(light.gameObject);
        }
        light = null; applied = null;
    }
}
