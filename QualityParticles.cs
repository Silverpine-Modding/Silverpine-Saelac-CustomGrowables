#nullable enable
using System;
using UnityEngine;

namespace SilverpineMods.CustomGrowables;

// Native quality glitter, not a light source. Derived entirely from saved seed
// quality; this helper adds no serializable behaviour or OnDestroy callback.
internal sealed class QualityParticles
{
    private const string ResourcePath = "MiscPrefabs/prefab_effect_glitter_rarity";
    // The world prefab's ParticleSystemYSorter uses offset 2 and its plant
    // instances sit one Z unit behind their parent. Keep its material/layer,
    // rotation, emission and fade settings rather than rebuilding the effect.
    private const int SortingOffset = 2;
    private static GameObject? template;
    private static bool unavailable;
    private GameObject? root;
    private ParticleSystem? particles;
    private ParticleSystemRenderer? renderer;
    private Quality appliedQuality;
    private bool creationFailed;

    internal void Apply(Transform parent, Quality quality, float offsetX, float offsetY)
    {
        quality = GrowableQuality.Normalize(quality);
        if (quality == Quality.Normal) { Clear(); return; }
        if (creationFailed) return;
        try
        {
            bool created = root == null;
            if (created)
            {
                if (unavailable) return;
                if (template == null)
                {
                    template = Resources.Load<GameObject>(ResourcePath);
                    if (template == null || template.GetComponent<ParticleSystem>() == null ||
                        template.GetComponent<ParticleSystemRenderer>() == null)
                    {
                        unavailable = true;
                        Plugin.Log.LogWarning("Native plant quality particles are unavailable; plants will still work without them.");
                        return;
                    }
                }
                // Instantiate under an inactive host so playOnAwake cannot emit
                // white particles before the saved quality and position are set.
                root = new GameObject("CustomGrowableQualityParticles");
                root.SetActive(false);
                root.transform.SetParent(parent, false);
                GameObject effect = UnityEngine.Object.Instantiate(template, root.transform, false);
                effect.transform.localPosition = new Vector3(0f, 0f, 1f);
                // Sprite offsets do not move the plant's sorting anchor. Do not
                // let the prefab's Start re-sort from the offset emitter instead.
                ParticleSystemYSorter? sorter = effect.GetComponent<ParticleSystemYSorter>();
                if (sorter != null) sorter.enabled = false;
                particles = effect.GetComponent<ParticleSystem>();
                renderer = effect.GetComponent<ParticleSystemRenderer>();
            }

            // Native-sized glitter follows the planted tile and art offsets,
            // not the stage's sprite scale (which may be huge for a GLB/tree).
            root!.transform.position = new Vector3(parent.position.x + offsetX,
                parent.position.y + offsetY, parent.position.z);
            Vector3 scale = parent.lossyScale;
            root.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            // Use the planted tile's Y just like the sprite; shifting stage art
            // upward must not push its glitter behind that same plant. Direct
            // assignment also avoids a native cached-renderer call before Start.
            renderer!.sortingOrder = Mathf.RoundToInt(-parent.position.y * 10f) + SortingOffset;
            if (created || appliedQuality != quality)
            {
                particles!.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule main = particles.main;
                main.startColor = quality.GetColor();
                appliedQuality = quality;
                root.SetActive(true);
                particles.Play(true);
            }
        }
        catch (Exception error)
        {
            Clear();
            creationFailed = true;
            Plugin.Log.LogWarning("Could not create plant quality particles; plant gameplay is unaffected: " + error.Message);
        }
    }

    internal void Clear()
    {
        if (root != null)
        {
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
        }
        root = null;
        particles = null;
        renderer = null;
    }
}
