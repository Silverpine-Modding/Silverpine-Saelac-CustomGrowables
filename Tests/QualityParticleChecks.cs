using System.Reflection;
using SilverpineMods.CustomGrowables;
using UnityEngine;

internal static class QualityParticleChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Invoke(CustomGrowable plant, string method, params object[] args) =>
        typeof(CustomGrowable).GetMethod(method, Private)!.Invoke(plant, args);
    private static GameObject Host(ParticleSystem particles) => particles.transform.parent!.gameObject!;

    internal static void Run(Action<bool, string> check, string file)
    {
        const string resource = "MiscPrefabs/prefab_effect_glitter_rarity";
        GameObject native = Resources.Assets[resource];
        var parent = new Transform { position = new Vector3(10, 20, .5f), localScale = Vector3.one * 6 };
        var effect = new QualityParticles();
        int created = ParticleSystem.Created.Count, loads = Resources.Loads;
        effect.Apply(parent, Quality.Normal, 0, 0);
        effect.Apply(parent, (Quality)999, 0, 0);
        check(ParticleSystem.Created.Count == created && Resources.Loads == loads,
            "Normal/invalid plant quality allocates no effect and loads no particle asset");
        foreach (Quality quality in Enum.GetValues<Quality>().Skip(1))
        {
            effect.Apply(parent, quality, .25f, -.5f);
            ParticleSystem current = ParticleSystem.Created.Last();
            check(current.StartColor.Equals(quality.GetColor()) && current.ColorAtPlay.Equals(quality.GetColor()) &&
                    current.ActiveAtPlay && ParticleSystem.ClonedUnderInactiveParent,
                "native quality color is assigned before playing particles: " + quality);
        }
        var glitter = ParticleSystem.Created.Last();
        check(ParticleSystem.Created.Count == created + 1 && glitter.Stops == 6 && glitter.Plays == 6,
            "quality changes recolor and clear old particles without allocating another emitter");
        var renderer = glitter.gameObject.GetComponent<ParticleSystemRenderer>();
        check(renderer.sortingLayerID == 389308695 &&
                ReferenceEquals(renderer.sharedMaterial, native.GetComponent<ParticleSystemRenderer>().sharedMaterial) &&
                native.GetComponent<ParticleSystem>().StartColor.Equals(new Color(1, 1, 1)),
            "quality effects retain the native sorting layer/material without mutating the resource prefab");
        check(glitter.transform.position.Equals(new Vector3(10.25f, 19.5f, 1.5f)) &&
                glitter.transform.lossyScale.Equals(Vector3.one) && renderer.sortingOrder == -198 &&
                !glitter.gameObject.GetComponent<ParticleSystemYSorter>().enabled,
            "quality glitter uses native depth/Y sorting, tile-relative art offsets and scale-independent size");
        parent.position = new Vector3(-3, -4, .5f); parent.localScale = Vector3.one * .25f;
        effect.Apply(parent, Quality.Legendary, -.5f, .25f);
        check(ParticleSystem.Created.Count == created + 1 && glitter.Plays == 6 && glitter.Stops == 6 &&
                glitter.transform.position.Equals(new Vector3(-3.5f, -3.75f, 1.5f)) &&
                glitter.transform.lossyScale.Equals(Vector3.one) && renderer.sortingOrder == 42,
            "stage position/scale refreshes reuse the emitter without restarting emission or calling an uninitialized sorter");
        effect.Apply(parent, Quality.Legendary, 0, 3);
        check(renderer.sortingOrder == 42 && glitter.transform.position.y == -1,
            "upward stage art offsets cannot sort quality glitter behind its own plant");
        effect.Apply(parent, Quality.Normal, 0, 0);
        check(Host(glitter).Destroyed && !Host(glitter).activeInHierarchy,
            "returning to Normal quality immediately removes glitter");
        effect.Apply(parent, Quality.Quality, 0, 0);
        check(ParticleSystem.Created.Count == created + 2, "a cleared quality effect can be recreated");
        effect.Clear(); effect.Clear();

        var definition = new GrowableDefinition
        {
            clock = "GameTurns", perennial = true, matureStage = 1, regrowStage = 1,
            growthSeasons = new[] { "Spring", "Summer", "Autumn", "Winter" },
            qualityBonusEnabled = true, qualityBonusChances = new[] { 0f, 0, 0, 0, 0, 1 },
            stages = new()
            {
                new() { sprite = "seed", duration = 1 },
                new() { sprite = "bare", duration = 1, offsetX = -.25f, scale = 2 },
                new() { sprite = "ripe", offsetX = .5f, scale = 3 }
            },
            harvest = new() { new() { item = "Apple" } }
        };
        definition.Validate(file, "pack:quality-particles");
        GrowableRegistry.Definitions[definition.QualifiedId] = definition;
        ActionQueue.Instance = new(); SerializationManager.loadingSave = false;
        var template = new CustomGrowable(); definition.Template = template.gameObject;
        template.transform.position = new Vector3(0, 0, .5f);
        template.Configure(definition.QualifiedId); template.SetSeedQuality(Quality.Legendary);
        created = ParticleSystem.Created.Count;
        template.RefreshArt(definition.QualifiedId);
        check(ParticleSystem.Created.Count == created, "quality-bearing templates never spawn live particles");
        Invoke(template, "OnDisable");

        var plant = new CustomGrowable(); plant.isActiveAndEnabled = false;
        plant.Configure(definition.QualifiedId); plant.SetSeedQuality(Quality.Superior);
        plant.RefreshArt(definition.QualifiedId);
        check(ParticleSystem.Created.Count == created, "inactive plant setup does not emit quality particles");
        plant.isActiveAndEnabled = true;
        Invoke(plant, "OnEnable"); Invoke(plant, "Start");
        glitter = ParticleSystem.Created.Last();
        check(ParticleSystem.Created.Count == created + 1 && glitter.StartColor.Equals(Quality.Superior.GetColor()),
            "activation before Start creates exactly one emitter using inherited seed quality");
        ActionQueue.Instance.Turn(); ActionQueue.Instance.Turn();
        check(ParticleSystem.Created.Count == created + 1 && glitter.Plays == 1 &&
                glitter.transform.position.Equals(new Vector3(.5f, 0, 1.5f)),
            "growth retains quality glitter and follows each stage's visual offset");
        using var save = new MemoryStream();
        plant.Serialize(new BinaryWriter(save, System.Text.Encoding.UTF8, true)); save.Position = 0;
        var restored = new CustomGrowable();
        restored.Deserialize(new BinaryReader(save));
        var restoredGlitter = ParticleSystem.Created.Last();
        restored.RestoreInstance(); restored.RefreshArt(definition.QualifiedId);
        check(ParticleSystem.Created.Count == created + 2 && restoredGlitter.Plays == 1 &&
                restoredGlitter.StartColor.Equals(Quality.Superior.GetColor()) &&
                restoredGlitter.transform.position.Equals(new Vector3(.5f, 0, 1.5f)),
            "save/load reconstructs quality glitter once from saved seed quality and stage");
        Invoke(restored, "OnDisable");

        var oldFactory = GrowableRegistry.ItemFactory;
        try
        {
            GrowableRegistry.ItemFactory = _ =>
            {
                var fruit = new Item(); fruit.Components.Add(new QualityTestComponent()); return fruit;
            };
            var inventory = new Inventory();
            Invoke(plant, "HarvestInto", inventory);
            check(inventory.Items.Single().GetItemComponents<IQualityItemComponent>().Single().Quality == Quality.Legendary &&
                    glitter.StartColor.Equals(Quality.Superior.GetColor()) && glitter.Plays == 1 &&
                    glitter.transform.position.Equals(new Vector3(-.25f, 0, 1.5f)),
                "perennial harvest bonuses upgrade products but do not recolor/restart the plant's inherited quality effect");
        }
        finally { GrowableRegistry.ItemFactory = oldFactory; }
        plant.isActiveAndEnabled = false; Invoke(plant, "OnDisable");
        check(Host(glitter).Destroyed && !Host(glitter).activeInHierarchy,
            "OnDisable immediately hides and disposes quality particles without OnDestroy");
        plant.isActiveAndEnabled = true; Invoke(plant, "OnEnable");
        glitter = ParticleSystem.Created.Last();
        check(glitter.StartColor.Equals(Quality.Superior.GetColor()) && Host(glitter).activeInHierarchy,
            "reenabling reconstructs the current quality effect");
        plant.PrepareForRemoval(); plant.RefreshArt(definition.QualifiedId);
        check(Host(glitter).Destroyed && !Host(glitter).activeInHierarchy && ReferenceEquals(glitter, ParticleSystem.Created.Last()),
            "removal clears quality glitter immediately and prevents art refresh from recreating it");

        // A visual-only failure must not break planting/growth. Reset only the
        // static resource cache for these fault-injection cases, then restore it.
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var templateField = typeof(QualityParticles).GetField("template", flags)!;
        var unavailableField = typeof(QualityParticles).GetField("unavailable", flags)!;
        var failed = new QualityParticles();
        UnityEngine.Object.FailParticleClone = true;
        try
        {
            failed.Apply(parent, Quality.Quality, 0, 0);
            check((bool)typeof(QualityParticles).GetField("creationFailed", Private)!.GetValue(failed)! &&
                    typeof(QualityParticles).GetField("root", Private)!.GetValue(failed) == null,
                "particle clone failures clean up the partial host and do not escape into plant gameplay");
        }
        finally { UnityEngine.Object.FailParticleClone = false; }
        created = ParticleSystem.Created.Count;
        failed.Apply(parent, Quality.Quality, 0, 0);
        check(ParticleSystem.Created.Count == created, "failed particle creation is not retried on every stage refresh");
        templateField.SetValue(null, null); unavailableField.SetValue(null, false);
        Resources.Assets.Remove(resource); loads = Resources.Loads;
        try
        {
            var missing = new QualityParticles();
            missing.Apply(parent, Quality.Superior, 0, 0); missing.Apply(parent, Quality.Legendary, 0, 0);
            check(Resources.Loads == loads + 1 && ParticleSystem.Created.Count == created,
                "missing native particle resources safely disable the visual without repeated asset loads");
        }
        finally
        {
            Resources.Assets[resource] = native;
            templateField.SetValue(null, null); unavailableField.SetValue(null, false);
        }
    }
}
