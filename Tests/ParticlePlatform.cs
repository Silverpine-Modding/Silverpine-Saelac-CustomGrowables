using UnityEngine;

namespace UnityEngine
{
    public static class Resources
    {
        public static readonly Dictionary<string, GameObject> Assets = new();
        public static int Loads;
        static Resources()
        {
            var template = new GameObject("prefab_effect_glitter_rarity");
            template.transform.localPosition = new Vector3(0, 1, 1);
            template.AddComponent<ParticleSystem>();
            template.AddComponent<ParticleSystemRenderer>().sortingLayerID = 389308695;
            Assets["MiscPrefabs/prefab_effect_glitter_rarity"] = template;
        }
        public static T? Load<T>(string path) where T : class
        {
            Loads++;
            return Assets.TryGetValue(path, out var value) ? value as T : null;
        }
    }
    public enum ParticleSystemStopBehavior { StopEmittingAndClear }
    public sealed class ParticleSystem
    {
        public static readonly List<ParticleSystem> Created = new();
        public static bool ClonedUnderInactiveParent;
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public Color StartColor = new(1, 1, 1), ColorAtPlay;
        public bool ActiveAtPlay;
        public int Stops, Plays;
        public MainModule main => new(this);
        public void Stop(bool withChildren, ParticleSystemStopBehavior behavior) => Stops++;
        public void Play(bool withChildren)
        {
            Plays++; ColorAtPlay = StartColor;
            ActiveAtPlay = transform.parent!.gameObject!.activeInHierarchy;
        }
        public struct MainModule
        {
            private readonly ParticleSystem system;
            public MainModule(ParticleSystem system) => this.system = system;
            public Color startColor { get => system.StartColor; set => system.StartColor = value; }
        }
    }
    public sealed class ParticleSystemRenderer
    {
        public int sortingOrder, sortingLayerID;
        public object sharedMaterial = new();
    }
}

// Mirrors the installed game's QualityExtensions; production calls the native API.
public static class QualityExtensions
{
    public static Color GetColor(this Quality quality) => quality switch
    {
        Quality.Quality => new(0, 1, 0),
        Quality.Superior => new(0, .75f, 1),
        Quality.Exceptional => new(1, 0, 1),
        Quality.Pristine => new(1, 1, 0),
        Quality.Legendary => new(1, 0, 0),
        _ => new(1, 1, 1)
    };
}

public class ParticleSystemYSorter { public bool enabled = true; }
