using System.Globalization;
using System.Reflection;
using SilverpineMods.CustomGrowables;

internal static class RuntimeChecks
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Invoke(CustomGrowable plant, string name, params object[] args) =>
        typeof(CustomGrowable).GetMethod(name, Private)!.Invoke(plant, args);
    private static T Read<T>(CustomGrowable plant, string name) =>
        (T)typeof(CustomGrowable).GetField(name, Private)!.GetValue(plant)!;

    internal static void Run(Action<bool, string> check, string file)
    {
        var definition = new GrowableDefinition
        {
            growthSeasons = new[] { "Summer" },
            stages = new()
            {
                new() { sprite = "seedling", duration = 1 },
                new() { sprite = "bare", duration = 1 },
                new() { sprite = "fruit" }
            },
            perennial = true, matureStage = 1, regrowStage = 1,
            harvest = new() { new() { item = "Apple", minimum = 5, maximum = 5 } }
        };
        definition.Validate(file, "pack:seasonal");
        GrowableRegistry.Definitions[definition.QualifiedId] = definition;
        WorldInfoManager.Instance = new() { currentSeason = Season.Summer };
        DungeonGenerationManager.Instance = new();
        SerializationManager.loadingSave = true;

        CustomGrowable Load(float progress = 0, double offlineHours = 2, int stage = 0)
        {
            var storage = new VersionSafeStorage();
            storage.AddToStorage("growableId", definition.QualifiedId);
            storage.AddToStorage("stageIndex", stage);
            storage.AddToStorage("stageId", "");
            storage.AddToStorage("stageProgress", progress);
            storage.AddToStorage("hasMatured", stage >= 1);
            storage.AddToStorage("utcNow", DateTime.UtcNow.AddHours(-offlineHours).ToString("O", CultureInfo.InvariantCulture));
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            storage.Serialize(writer); stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var plant = new CustomGrowable(); plant.Deserialize(reader); return plant;
        }

        var loaded = Load(progress: 10);
        Invoke(loaded, "FixedUpdate");
        check(Read<int>(loaded, "stageIndex") == 0 && Read<double>(loaded, "stageProgress") == 10,
            "deserializing a plant does not advance it using the previous world's season");
        WorldInfoManager.Instance.currentSeason = Season.Winter;
        SerializationManager.loadingSave = false;
        Invoke(loaded, "FixedUpdate");
        check(Read<int>(loaded, "stageIndex") == 0 && Read<double>(loaded, "stageProgress") == 10 &&
            Read<double>(loaded, "pendingOfflineSeconds") == 0, "off-season offline time is discarded once, preserving saved progress");
        WorldInfoManager.Instance.currentSeason = Season.Summer;
        Invoke(loaded, "FixedUpdate");
        check(Read<int>(loaded, "stageIndex") == 0 && Read<double>(loaded, "stageProgress") > 10 &&
            Read<double>(loaded, "stageProgress") < 11, "returning in-season resumes without banking off-season offline time");

        SerializationManager.loadingSave = true;
        loaded = Load();
        SerializationManager.loadingSave = false;
        DungeonGenerationManager.Instance = null;
        Invoke(loaded, "FixedUpdate");
        check(Read<double>(loaded, "pendingOfflineSeconds") > 0 && Read<int>(loaded, "stageIndex") == 0,
            "offline credit waits until local world data exists");
        DungeonGenerationManager.Instance = new();
        Invoke(loaded, "FixedUpdate"); Invoke(loaded, "FixedUpdate");
        check(Read<int>(loaded, "stageIndex") == 2 && Read<double>(loaded, "pendingOfflineSeconds") == 0,
            "in-season offline growth reaches harvest readiness and is applied only once");
        WorldInfoManager.Instance.currentSeason = Season.Winter;
        var inventory = new Inventory();
        Invoke(loaded, "HarvestInto", inventory);
        check(inventory.Items.Count == 5 && Read<int>(loaded, "stageIndex") == 1 && Read<bool>(loaded, "hasMatured"),
            "ripe perennial can yield five apples out of season without losing maturity");
        Invoke(loaded, "FixedUpdate");
        check(Read<double>(loaded, "stageProgress") == 0, "post-harvest regrowth pauses out of season");
        WorldInfoManager.Instance.LocalSeason = Season.Summer;
        Invoke(loaded, "FixedUpdate");
        check(Read<double>(loaded, "stageProgress") > 0, "native local summer biome overrides global winter for growth");
        WorldInfoManager.Instance.currentSeason = Season.Summer;
        WorldInfoManager.Instance.LocalSeason = Season.Winter;
        double previous = Read<double>(loaded, "stageProgress");
        Invoke(loaded, "FixedUpdate");
        check(Read<double>(loaded, "stageProgress") == previous, "native mountain winter pauses a summer-only plant");
        WorldInfoManager.Instance.LocalSeason = null;

        loaded = Load(progress: float.NaN, offlineHours: -1, stage: -99);
        check(Read<int>(loaded, "stageIndex") == 0 && Read<double>(loaded, "stageProgress") == 0 &&
            Read<double>(loaded, "pendingOfflineSeconds") == 0, "invalid saved progress/stage and future timestamps cannot corrupt growth");

        definition.clock = "GameTurns";
        definition.stages![0].duration = 2; definition.stages[1].duration = 3;
        definition.Validate(file, definition.QualifiedId);
        var turns = new ActionQueue(); ActionQueue.Instance = turns;
        var turnPlant = new CustomGrowable(); turnPlant.Configure(definition.QualifiedId);
        Invoke(turnPlant, "OnEnable"); Invoke(turnPlant, "Start"); Invoke(turnPlant, "FixedUpdate");
        check(turns.Subscribers == 1, "enable/start/fixed update subscribe to turns only once");
        turns.Turn();
        check(Read<double>(turnPlant, "stageProgress") == 1, "game-turn plants advance exactly once per processed turn");
        WorldInfoManager.Instance.currentSeason = Season.Winter; turns.Turn();
        check(Read<double>(turnPlant, "stageProgress") == 1, "off-season game turns preserve progress");
        WorldInfoManager.Instance.currentSeason = Season.Summer;
        SerializationManager.loadingSave = true; turns.Turn(); SerializationManager.loadingSave = false;
        check(Read<double>(turnPlant, "stageProgress") == 1, "loading-save turns never advance plants");
        var replacementQueue = new ActionQueue(); ActionQueue.Instance = replacementQueue;
        Invoke(turnPlant, "FixedUpdate");
        check(turns.Subscribers == 0 && replacementQueue.Subscribers == 1, "changing action queues detaches the old publisher");
        Invoke(turnPlant, "OnDisable");
        check(replacementQueue.Subscribers == 0, "disable removes turn subscription without OnDestroy");
        Invoke(turnPlant, "OnEnable"); replacementQueue.Turn();
        check(Read<int>(turnPlant, "stageIndex") == 1 && replacementQueue.Subscribers == 1,
            "reenabling a plant resumes turn-based growth");
        Invoke(turnPlant, "OnDisable");

        int lookups = GrowableRegistry.Lookups;
        for (int i = 0; i < 1000; i++) Invoke(loaded, "FixedUpdate");
        check(GrowableRegistry.Lookups == lookups, "active growth reuses the resolved definition instead of string lookups each tick");

        definition.harvest![0].chance = 0;
        loaded = Load(stage: 2);
        inventory = new(); UnityEngine.Random.value = 0;
        Invoke(loaded, "HarvestInto", inventory);
        check(inventory.Items.Count == 0, "zero-chance outputs never drop even when the random sample is zero");
        Invoke(loaded, "OnDisable");

        // Round-trip through the actual component methods; the old float field remains readable.
        using var save = new MemoryStream();
        using var saveWriter = new BinaryWriter(save, System.Text.Encoding.UTF8, true);
        loaded.Serialize(saveWriter); save.Position = 0;
        using var saveReader = new BinaryReader(save);
        var roundTrip = new CustomGrowable(); roundTrip.Deserialize(saveReader);
        check(Read<int>(roundTrip, "stageIndex") == 1 && Read<bool>(roundTrip, "hasMatured"),
            "component save/load retains perennial maturity and regrowth state");

        definition.harvest[0].chance = 1;
        definition.harvest.Add(new() { item = "Missing fruit" });
        GrowableRegistry.FailItem = "Missing fruit";
        loaded = Load(stage: 2); inventory = new();
        Invoke(loaded, "HarvestInto", inventory);
        check(inventory.Items.Count == 0 && Read<int>(loaded, "stageIndex") == 2 && !Read<bool>(loaded, "harvesting"),
            "failed harvest preparation gives nothing and leaves the plant harvestable");
        GrowableRegistry.FailItem = null; definition.harvest.RemoveAt(1);
        inventory.Added = () => Invoke(loaded, "HarvestInto", inventory);
        Invoke(loaded, "HarvestInto", inventory);
        check(inventory.Items.Count == 5 && Read<int>(loaded, "stageIndex") == 1,
            "inventory callbacks cannot recursively duplicate a harvest");
        loaded = Load(stage: 2); inventory = new() { ThrowAfterAdd = true };
        LootDropper.Dropped.Clear(); Invoke(loaded, "HarvestInto", inventory);
        check(inventory.Items.Count == 5 && LootDropper.Dropped.Count == 0 && Read<int>(loaded, "stageIndex") == 1,
            "inventory exceptions after insertion do not duplicate items on the ground");
        loaded = Load(stage: 2); inventory = new() { ThrowBeforeAdd = true };
        LootDropper.Dropped.Clear(); Invoke(loaded, "HarvestInto", inventory);
        check(inventory.Items.Count == 0 && LootDropper.Dropped.Count == 5,
            "inventory exceptions before insertion route undelivered harvest to ground loot");
        loaded = Load(stage: 2); inventory = new() { Full = true };
        LootDropper.Dropped.Clear(); Invoke(loaded, "HarvestInto", inventory);
        check(LootDropper.Dropped.Count == 5, "full inventories preserve all five apples as ground loot");
        loaded = Load(stage: 2); inventory = new(); AnimationHelper.Instance.ThrowEffects = true;
        Invoke(loaded, "HarvestInto", inventory); AnimationHelper.Instance.ThrowEffects = false;
        check(inventory.Items.Count == 5 && Read<int>(loaded, "stageIndex") == 1,
            "cosmetic failures cannot leave a consumed harvest ready again");
        loaded = Load(stage: 2); loaded.PrepareForRemoval(); inventory = new();
        Invoke(loaded, "HarvestInto", inventory);
        check(inventory.Items.Count == 0 && !loaded.IsNPCInteractableAPIActive(new NeuralNPC()),
            "plants scheduled for replacement cannot still be harvested");
        loaded = Load(stage: 2); loaded.isActiveAndEnabled = false;
        Invoke(loaded, "HarvestInto", inventory);
        check(inventory.Items.Count == 0, "delayed harvest callbacks ignore disabled plants");
        loaded = Load(stage: 2); ActionQueue.Instance = new(); Invoke(loaded, "OnEnable");
        check(ActionQueue.Instance.Subscribers == 0, "ripe plants do not retain unnecessary turn subscriptions");
        Invoke(loaded, "HarvestInto", new Inventory());
        check(ActionQueue.Instance.Subscribers == 1, "harvesting a ripe perennial restarts turn subscription for regrowth");
        Invoke(loaded, "OnDisable");

        Player.Instance = new(); WorldInfoManager.Instance.Owned = true; TileTags.Roofed = false;
        var seed = new Item(); var seedInventory = new Inventory(); seedInventory.TryAddItem(seed);
        var behavior = new ItemComponent_CustomGrowableSeed(definition.QualifiedId) { item = seed };
        behavior.OnUse(); behavior.OnUse();
        check(Player.Instance.Starts == 1, "repeated seed use cannot start overlapping planting interactions");
        seedInventory.Items.Remove(seed); Player.Instance.Success!(); Player.Instance.Finished!();
        check(GrowableRegistry.Planted == 0, "removing a seed while interacting cancels planting without a free plant");
        seedInventory.TryAddItem(seed); behavior.OnUse(); WorldInfoManager.Instance.Owned = false;
        Player.Instance.Success!(); Player.Instance.Finished!();
        check(GrowableRegistry.Planted == 0 && seedInventory.ContainsItem(seed), "planting rechecks property permissions before consuming a seed");
        WorldInfoManager.Instance.Owned = true; behavior.OnUse(); TileTags.Roofed = true;
        Player.Instance.Success!(); Player.Instance.Finished!();
        check(GrowableRegistry.Planted == 0 && seedInventory.ContainsItem(seed), "planting rechecks newly roofed tiles");
        TileTags.Roofed = false; behavior.OnUse(); Player.Instance.transform.position = new(1, 0, 0);
        Player.Instance.Success!(); Player.Instance.Finished!();
        check(GrowableRegistry.Planted == 0, "moving away during planting cannot plant remotely");
        Player.Instance.transform.position = new(); behavior.OnUse(); GrowableRegistry.FailPlant = true;
        Player.Instance.Success!(); Player.Instance.Finished!(); GrowableRegistry.FailPlant = false;
        check(seedInventory.ContainsItem(seed), "failed plant creation preserves the seed and releases the interaction guard");
        behavior.OnUse(); var canceledSuccess = Player.Instance.Success; var canceledFinish = Player.Instance.Finished;
        canceledFinish!(); canceledSuccess!();
        check(GrowableRegistry.Planted == 0 && seedInventory.ContainsItem(seed), "canceled planting cannot be completed by a stale callback");
        behavior.OnUse(); int starts = Player.Instance.Starts; canceledFinish(); behavior.OnUse();
        check(Player.Instance.Starts == starts, "old planting cancellation cannot release a new interaction guard");
        Player.Instance.Finished!();
        behavior.OnUse(); Player.Instance.Success!(); Player.Instance.Success!(); Player.Instance.Finished!();
        check(GrowableRegistry.Planted == 1 && !seedInventory.ContainsItem(seed), "successful planting consumes exactly one seed, even if completion is repeated");

        definition.clock = "RealTimeHours"; definition.Validate(file, definition.QualifiedId);
        loaded = Load(offlineHours: 4);
        using var immediateSave = new MemoryStream();
        using var immediateWriter = new BinaryWriter(immediateSave, System.Text.Encoding.UTF8, true);
        loaded.Serialize(immediateWriter);
        check(Read<int>(loaded, "stageIndex") == 1 && Read<double>(loaded, "stageProgress") >= 7200 && Read<double>(loaded, "pendingOfflineSeconds") == 0,
            "saving before the first fixed update still applies eligible offline growth");
        loaded = Load(offlineHours: 4); DungeonGenerationManager.Instance = null;
        using var deferredSave = new MemoryStream();
        using var deferredWriter = new BinaryWriter(deferredSave, System.Text.Encoding.UTF8, true);
        loaded.Serialize(deferredWriter); deferredSave.Position = 0;
        using var deferredReader = new BinaryReader(deferredSave);
        var deferredPlant = new CustomGrowable(); deferredPlant.Deserialize(deferredReader);
        check(Read<double>(deferredPlant, "pendingOfflineSeconds") >= 4 * 3600,
            "saving while the world is unavailable preserves the original offline timestamp");
        DungeonGenerationManager.Instance = new();
    }
}
