using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SilverpineMods.CustomGrowables;
using UnityEngine;

internal static class LightingQualityChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Invoke(CustomGrowable plant, string method, params object[] args) =>
        typeof(CustomGrowable).GetMethod(method, Private)!.Invoke(plant, args);
    private static T Read<T>(CustomGrowable plant, string field) =>
        (T)typeof(CustomGrowable).GetField(field, Private)!.GetValue(plant)!;
    private static void SetStage(CustomGrowable plant, int stage)
    {
        typeof(CustomGrowable).GetField("stageIndex", Private)!.SetValue(plant, stage);
        Invoke(plant, "ApplyStage");
    }
    private static Item QualityItem()
    {
        var item = new Item();
        item.Components.Add(new QualityTestComponent());
        return item;
    }

    internal static void Run(Action<bool, string> check, string file)
    {
        void Reject(Action action, string name)
        {
            try { action(); } catch (InvalidDataException) { check(true, name); return; }
            check(false, name);
        }
        var dew = GrowableLightDefinition.MorningDew(); dew.Validate("stage.light");
        var lantern = GrowableLightDefinition.Lantern(); lantern.Validate("stage.light");
        check(dew.PresetName == "Morning Dew" && dew.radius == 1 && dew.intensity == 1 && !dew.flicker &&
            Math.Abs(dew.Color.g - .89451975f) < .00001f && dew.offsetZ == -.55f,
            "Morning Dew preset matches the inspected native warm steady point light");
        // Inspected native assets: oak/our plants use Z=.5; grass and Morning
        // Dew use Z=1; the native light's local -.55 puts it at world Z=.45.
        var geometryLight = new StageLight();
        foreach (float plantDepth in new[] { .5f, 1f })
        {
            var parent = new Transform { position = new Vector3(10, 20, plantDepth), localScale = Vector3.one * 3 };
            geometryLight.Apply(parent, dew);
            Light point = Light.Created.Last();
            check(Math.Abs(point.transform.position.z - .45f) < .00001f &&
                    Math.Abs(1f - point.transform.position.z) < point.range,
                "Morning Dew light reaches the grass plane independently of plant sorting depth " + plantDepth);
            float z = 1f - point.transform.position.z;
            check(Math.Abs(Math.Sqrt(point.range * point.range - z * z) - Math.Sqrt(1f - .55f * .55f)) < .00001f,
                "Morning Dew ground footprint matches native light/terrain separation at plant depth " + plantDepth);
        }
        var movedParent = new Transform { position = new Vector3(30, 40, .5f), localScale = Vector3.one * .25f };
        var customDepth = dew.Copy(); customDepth.offsetX = .25f; customDepth.offsetY = -.5f; customDepth.offsetZ = -.75f;
        geometryLight.Apply(movedParent, customDepth);
        check(Light.Created.Last().transform.position.Equals(new Vector3(30.25f, 39.5f, .25f)) &&
                Light.Created.Last().transform.lossyScale.Equals(Vector3.one),
            "custom light depth is terrain-relative while XY follows the plant without scale distortion");
        geometryLight.Clear();
        check(lantern.PresetName == "Lantern" && lantern.radius == 5 && lantern.intensity == 2 && lantern.flicker && lantern.Color.g == .68f,
            "Lantern preset matches Item Loader's color, radius, intensity, and flicker defaults");
        lantern.extensionData = new Dictionary<string, JToken> { ["future"] = JObject.Parse("{\"x\":1}") };
        var copy = lantern.Copy(); copy.color![0] = .2f; copy.extensionData!["future"]["x"] = 2;
        check(lantern.color![0] == 1 && (int)lantern.extensionData["future"]["x"]! == 1 && copy.PresetName == "Custom",
            "duplicated light colors and extension data are independent of the original stage");
        foreach (var invalid in new[]
        {
            new GrowableLightDefinition { color = new[] { 1f, 0f } },
            new GrowableLightDefinition { color = new[] { 1f, float.NaN, 0f } },
            new GrowableLightDefinition { color = new[] { 1f, 2f, 0f } },
            new GrowableLightDefinition { radius = 0 }, new GrowableLightDefinition { radius = 51 },
            new GrowableLightDefinition { intensity = -1 }, new GrowableLightDefinition { intensity = float.PositiveInfinity },
            new GrowableLightDefinition { intensity = 21 }, new GrowableLightDefinition { offsetZ = -11 },
            new GrowableLightDefinition { offsetX = 101 }, new GrowableLightDefinition { offsetY = float.NaN }
        }) Reject(() => invalid.Validate("stage.light"), "invalid stage light settings are rejected: " + JsonConvert.SerializeObject(invalid));
        var dark = new GrowableLightDefinition { enabled = false, intensity = 0 };
        dark.Validate("stage.light");
        check(JsonConvert.DeserializeObject<GrowableStageDefinition>("{\"sprite\":\"seed\"}")!.light == null,
            "legacy stages remain non-emissive without a light block");
        var art = new GrowableStageDefinition { cacheId = Guid.NewGuid().ToString("N"), model = "plant.glb" };
        string cache = art.CacheSuffix(0); art.light = lantern;
        check(art.CacheSuffix(0) == cache, "stage lighting changes do not invalidate baked GLB art caches");

        var definition = new GrowableDefinition
        {
            clock = "GameTurns", playerPropertyOnly = false, growthSeasons = new[] { "Summer" },
            perennial = true, matureStage = 1, regrowStage = 1, scale = 2,
            stages = new()
            {
                new() { sprite = "seed", duration = 1 }, new() { sprite = "bare", duration = 1 },
                new() { sprite = "ripe", scale = 3, light = new() { offsetX = .25f, offsetY = .5f } }
            },
            harvest = new()
            {
                new() { item = "Apple", minimum = 2, maximum = 2 },
                new() { item = "Seed" }, new() { item = "Stone" }
            }
        };
        definition.Validate(file, "pack:lighting-quality");
        var json = JsonConvert.SerializeObject(definition);
        var roundTripDefinition = JsonConvert.DeserializeObject<GrowableDefinition>(json)!;
        check(roundTripDefinition.stages![2].light!.Matches(definition.stages[2].light!) && !roundTripDefinition.qualityBonusEnabled,
            "per-stage lights round-trip through pack JSON and quality bonuses default off");
        GrowableRegistry.Definitions[definition.QualifiedId] = definition;
        WorldInfoManager.Instance = new() { currentSeason = Season.Summer };
        DungeonGenerationManager.Instance = new(); SerializationManager.loadingSave = false;
        ActionQueue.Instance = new(); UnityEngine.Random.value = 0;
        var template = new CustomGrowable(); definition.Template = template.gameObject;
        template.transform.position = new Vector3(0, 0, .5f);
        definition.stages[0].light = dew;
        int lightsBefore = Light.Created.Count;
        template.Configure(definition.QualifiedId);
        check(Light.Created.Count == lightsBefore, "prefab templates never create live stage lights");
        Invoke(template, "OnDisable"); definition.stages[0].light = null;

        var plant = new CustomGrowable(); plant.transform.position = new Vector3(10, 20, .5f);
        plant.Configure(definition.QualifiedId); plant.SetSeedQuality(Quality.Pristine);
        ActionQueue.Instance.Turn();
        check(Light.Created.Count == lightsBefore, "unlit seedling and regrowth stages allocate no light objects");
        ActionQueue.Instance.Turn();
        Light glow = Light.Created.Last();
        check(Light.Created.Count == lightsBefore + 1 && glow.gameObject.activeInHierarchy && glow.range == 1 &&
            glow.transform.position.Equals(new Vector3(10.25f, 20.5f, .45f)) && glow.transform.lossyScale.Equals(Vector3.one),
            "maturing enables one point light with world-space offsets and scale-independent radius");
        plant.RefreshArt(definition.QualifiedId); plant.RestoreInstance();
        check(Light.Created.Count == lightsBefore + 1, "art refresh and restore reuse an unchanged stage light");
        Invoke(plant, "OnDisable");
        check(glow.gameObject.Destroyed && !glow.gameObject.activeInHierarchy, "disable immediately extinguishes and disposes stage lights without OnDestroy");
        Invoke(plant, "OnEnable");
        glow = Light.Created.Last();
        check(glow.gameObject.activeInHierarchy && Light.Created.Count == lightsBefore + 2, "reenabling restores exactly one current-stage light");
        definition.stages[2].light!.intensity = 3; definition.stages[2].light!.flicker = true;
        plant.RefreshArt(definition.QualifiedId);
        check(!glow.gameObject.activeInHierarchy && Light.Created.Last().intensity == 3 &&
            Light.Created.Last().gameObject.Components.ContainsKey(typeof(LightFlickerer)),
            "changed stage settings replace the light and initialize native flicker with the new intensity");

        using var savedPlant = new MemoryStream();
        plant.Serialize(new BinaryWriter(savedPlant, System.Text.Encoding.UTF8, true)); savedPlant.Position = 0;
        var restored = new CustomGrowable(); restored.Deserialize(new BinaryReader(savedPlant));
        check(Read<Quality>(restored, "seedQuality") == Quality.Pristine && Read<int>(restored, "stageIndex") == 2 &&
            Light.Created.Last().intensity == 3, "save/load restores plant quality and derives the light from the restored growth stage");
        Invoke(plant, "OnDisable");
        GrowableRegistry.ItemFactory = id =>
        {
            if (id == "Stone") return new Item();
            if (id == "Seed") { var seed = new Item(); seed.Components.Add(new ItemComponent_CustomGrowableSeed(definition.QualifiedId)); return seed; }
            var fruit = QualityItem(); fruit.Components.Add(new QualityTestComponent()); return fruit;
        };
        try
        {
            var inventory = new Inventory(); glow = Light.Created.Last();
            Invoke(restored, "HarvestInto", inventory);
            check(inventory.Items.Count == 4 && inventory.Items.SelectMany(i => i.GetItemComponents<IQualityItemComponent>())
                .All(q => q.Quality == Quality.Pristine) && inventory.Items.Last().Components.Count == 0,
                "all quality-capable harvest components and returned seeds inherit the plant quality; other products stay unchanged");
            check(Read<Quality>(restored, "seedQuality") == Quality.Pristine && Read<int>(restored, "stageIndex") == 1 && !glow.gameObject.activeInHierarchy,
                "perennial harvest preserves plant quality and extinguishes the light in an unlit regrowth stage");
            definition.qualityBonusEnabled = true; definition.qualityBonusChances = new[] { .25f, .25f, 0f, 0f, 0f, 0f };
            SetStage(restored, 2);
            UnityEngine.Random.value = .9f;
            UnityEngine.Random.Samples.Enqueue(.1f); UnityEngine.Random.Samples.Enqueue(.3f); UnityEngine.Random.Samples.Enqueue(.9f);
            inventory = new(); Invoke(restored, "HarvestInto", inventory);
            check(GrowableQuality.FromItem(inventory.Items[0]) == Quality.Blessed &&
                GrowableQuality.FromItem(inventory.Items[1]) == Quality.Legendary &&
                GrowableQuality.FromItem(inventory.Items[2]) == Quality.Pristine && UnityEngine.Random.Reads == 3,
                "products roll independently for +1, +2, or unchanged quality; unsupported products consume no quality roll");
            check(Read<Quality>(restored, "seedQuality") == Quality.Pristine, "bonus harvests never upgrade the parent plant");
            definition.qualityBonusEnabled = false;
            SetStage(restored, 2); LootDropper.Dropped.Clear(); inventory = new() { Full = true };
            Invoke(restored, "HarvestInto", inventory);
            check(LootDropper.Dropped.Count == 4 && LootDropper.Dropped.SelectMany(i => i.GetItemComponents<IQualityItemComponent>())
                .All(q => q.Quality == Quality.Pristine), "inventory overflow preserves inherited product quality on dropped loot");
            SetStage(restored, 2); glow = Light.Created.Last(); restored.PrepareForRemoval();
            check(!glow.gameObject.activeInHierarchy, "removing a plant switches its light off immediately");
            Invoke(restored, "OnEnable");
            check(!Light.Created.Last().gameObject.activeInHierarchy, "a plant pending removal cannot resurrect its glow");
        }
        finally { GrowableRegistry.ItemFactory = null; Invoke(restored, "OnDisable"); }

        foreach (Quality quality in Enum.GetValues<Quality>())
        {
            var seed = new ItemComponent_CustomGrowableSeed(definition.QualifiedId, quality);
            using var stream = new MemoryStream(); seed.Serialize(new BinaryWriter(stream, System.Text.Encoding.UTF8, true)); stream.Position = 0;
            var loadedSeed = new ItemComponent_CustomGrowableSeed(); loadedSeed.Deserialize(new BinaryReader(stream));
            check(loadedSeed.Quality == quality, "seed quality survives serialization: " + quality);
        }
        var oldStorage = new VersionSafeStorage(); oldStorage.AddToStorage("growableId", definition.QualifiedId);
        using var oldSave = new MemoryStream(); oldStorage.Serialize(new BinaryWriter(oldSave, System.Text.Encoding.UTF8, true));
        oldSave.Position = 0; var oldSeed = new ItemComponent_CustomGrowableSeed("", Quality.Legendary);
        oldSeed.Deserialize(new BinaryReader(oldSave, System.Text.Encoding.UTF8, true));
        oldSave.Position = 0; var oldPlant = new CustomGrowable(); oldPlant.SetSeedQuality(Quality.Legendary);
        oldPlant.Deserialize(new BinaryReader(oldSave));
        check(oldSeed.Quality == Quality.Normal && Read<Quality>(oldPlant, "seedQuality") == Quality.Normal,
            "pre-quality seeds and plants load as Normal, without stale reused quality");
        oldSeed.Quality = (Quality)999; oldPlant.SetSeedQuality((Quality)(-1));
        check(oldSeed.Quality == Quality.Normal && Read<Quality>(oldPlant, "seedQuality") == Quality.Normal, "invalid quality values normalize to Normal");
        Invoke(oldPlant, "OnDisable");

        var seedItem = new Item(); var seedComponent = new ItemComponent_CustomGrowableSeed(definition.QualifiedId);
        seedItem.Components.Add(seedComponent); seedComponent.item = seedItem;
        seedItem.SetQuality(Quality.Exceptional);
        var source = new Inventory(); source.TryAddItem(seedItem);
        Player.Instance = new(); TileTags.Roofed = false; GrowableRegistry.FailPlant = false;
        seedComponent.OnUse(); Player.Instance.Success!();
        check(GrowableRegistry.LastPlantedQuality == Quality.Exceptional && !source.ContainsItem(seedItem),
            "planting passes the used seed's actual native quality into the new plant");

        CheckChances(check, Reject, definition);
    }

    private static void CheckChances(Action<bool, string> check, Action<Action, string> reject, GrowableDefinition definition)
    {
        foreach (float[] invalid in new[] { new[] { .1f }, new[] { .6f, .5f, 0f, 0f, 0f, 0f },
            new[] { -.1f, 0f, 0f, 0f, 0f, 0f }, new[] { float.NaN, 0f, 0f, 0f, 0f, 0f },
            new[] { float.PositiveInfinity, 0f, 0f, 0f, 0f, 0f } })
            reject(() => GrowableQuality.ValidateBonusChances(invalid), "invalid upgrade probabilities are rejected: " + JsonConvert.SerializeObject(invalid));
        var chances = new[] { .25f, .25f, 0f, 0f, 0f, 0f };
        GrowableQuality.ValidateBonusChances(chances);
        check(GrowableQuality.RollBonus(chances, 0) == 1 && GrowableQuality.RollBonus(chances, .249f) == 1 &&
            GrowableQuality.RollBonus(chances, .25f) == 2 && GrowableQuality.RollBonus(chances, .5f) == 0 &&
            GrowableQuality.RollBonus(chances, 1) == 0, "upgrade probabilities use disjoint intervals with an unchanged remainder");
        definition.qualityBonusEnabled = true;
        for (int tier = 1; tier <= GrowableQuality.UpgradeTiers; tier++)
        {
            definition.qualityBonusChances = new float[6]; definition.qualityBonusChances[tier - 1] = 1;
            foreach (Quality quality in Enum.GetValues<Quality>())
            {
                var item = QualityItem(); UnityEngine.Random.value = 1;
                GrowableQuality.ApplyHarvest(item, quality, definition);
                check(GrowableQuality.FromItem(item) == (Quality)Math.Min(6, (int)quality + tier),
                    $"100% +{tier} tiers from {quality} caps at Legendary, including Unity's inclusive upper random endpoint");
            }
        }
        definition.qualityBonusEnabled = false; UnityEngine.Random.value = 0;
        var plain = QualityItem(); GrowableQuality.ApplyHarvest(plain, Quality.Superior, definition);
        check(GrowableQuality.FromItem(plain) == Quality.Superior && UnityEngine.Random.Reads == 0,
            "disabled bonuses inherit quality without consuming random values");
        definition.qualityBonusEnabled = true; definition.qualityBonusChances = new float[6];
        GrowableQuality.ApplyHarvest(plain, Quality.Normal, definition);
        check(GrowableQuality.FromItem(plain) == Quality.Normal && UnityEngine.Random.Reads == 0,
            "all-zero bonus chances never upgrade even on a zero random sample");
        definition.qualityBonusChances = chances;
        var roundTrip = JsonConvert.DeserializeObject<GrowableDefinition>(JsonConvert.SerializeObject(definition))!;
        check(roundTrip.qualityBonusEnabled && roundTrip.qualityBonusChances!.SequenceEqual(chances),
            "multi-tier bonus toggle and probabilities round-trip through pack JSON");
    }
}
