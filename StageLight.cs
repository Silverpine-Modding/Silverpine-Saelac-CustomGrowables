#nullable enable
using UnityEngine;

namespace SilverpineMods.CustomGrowables;

// Transient child only: no new serializable component and no OnDestroy dependency.
// The saved growth stage and current pack settings are the source of truth.
internal sealed class StageLight
{
    // Native grass and Morning Dew sit at Z=1; tree-style plant rendering is
    // at Z=.5. Light depth must use the terrain plane, not that sorting depth.
    private const float TerrainPlaneZ = 1f;
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
        // XY follows the planted tile; Z is relative to the native terrain
        // plane. Adding -.55 to tree Z=.5 would leave a range-1 light more
        // than one unit from grass (Z=1), eliminating its ground glow.
        Vector3 position = parent.position;
        light!.transform.position = new Vector3(position.x + settings.offsetX,
            position.y + settings.offsetY, TerrainPlaneZ + settings.offsetZ);
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
