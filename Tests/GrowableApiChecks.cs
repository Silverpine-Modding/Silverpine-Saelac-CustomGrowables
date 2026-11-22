using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SilverpineMods.CustomGrowables;
using UnityEngine;

internal static class GrowableApiChecks
{
    private static void Invoke(object target, string name, params object[] args) =>
        NativeGrowablePatchCode.Method(target.GetType(), name).Invoke(target, args);
    private static Item Product(Quality quality = Quality.Quality)
    { var item = new Item(); item.Components.Add(new QualityTestComponent { Quality = quality }); return item; }
    private sealed class SeedClosure { public ItemComponent_Seeds Seeds = new(); }

    internal static void Run(Action<bool, string> check, string file)
    {
        var planted = new List<GrowablePlantedEventArgs>();
        var grown = new List<GrowableGrowthEventArgs>();
        var harvested = new List<GrowableHarvestedEventArgs>();
        EventHandler<GrowablePlantedEventArgs> planting = (_, e) => planted.Add(e);
        EventHandler<GrowableGrowthEventArgs> growth = (_, e) => grown.Add(e);
        EventHandler<GrowableGrowthEventArgs> brokenGrowth = (_, _) => throw new Exception("Other mod growth failure");
        EventHandler<GrowableHarvestedEventArgs> harvest = (_, e) => harvested.Add(e);
        EventHandler<GrowablePlantedEventArgs> brokenPlanting = (_, _) => throw new Exception("Other mod planting failure");
        EventHandler<GrowableHarvestedEventArgs> brokenHarvest = (_, _) => throw new Exception("Other mod XP failure");
        EventHandler<GrowableHarvestItemEventArgs> bonus = (_, e) => { e.Count = 2; e.Quality = Quality.Pristine; };
        EventHandler<GrowableHarvestItemEventArgs> brokenBonus = (_, e) =>
        { e.Count = 0; e.Quality = Quality.Legendary; throw new Exception("Other mod modifier failure"); };
        GrowableApi.Planted += brokenPlanting; GrowableApi.Planted += planting;
        GrowableApi.GrowthChanged += brokenGrowth; GrowableApi.GrowthChanged += growth;
        GrowableApi.Harvested += brokenHarvest; GrowableApi.Harvested += harvest;
        GrowableApi.PreparingHarvestItem += bonus; GrowableApi.PreparingHarvestItem += brokenBonus;
        var oldFactory = GrowableRegistry.ItemFactory;
        try
        {
            SerializationManager.loadingSave = false; AnimationHelper.Instance.ThrowEffects = false;
            WorldInfoManager.Instance = new(); DungeonGenerationManager.Instance = new();
            ActionQueue.Instance = new(); Player.Instance = new(); TileTags.Roofed = false;
            var definition = new GrowableDefinition
            {
                clock = "GameTurns", growthSeasons = new[] { "Spring", "Summer", "Autumn", "Winter" },
                stages = new() { new() { sprite = "seed", duration = 1 }, new() { sprite = "ripe" } },
                regrowStage = 0, harvest = new() { new() { item = "Apple", minimum = 2, maximum = 2 } }
            };
            definition.Validate(file, "pack:api"); GrowableRegistry.Definitions[definition.QualifiedId] = definition;
            GrowableRegistry.ItemFactory = _ => Product();
            CustomGrowable Plant(bool ripe = false)
            {
                var plant = new GameObject("Custom API plant").AddComponent<CustomGrowable>();
                plant.Configure(definition.QualifiedId); plant.SetSeedQuality(Quality.Superior);
                if (ripe) Invoke(plant, "Advance", definition, 1d, GrowableGrowthReason.Growth);
                return plant;
            }
            var custom = Plant();
            check(GrowableApi.TryGetPlant(custom.gameObject, out var info) && info.IsCustom && info.Id == "pack:api" &&
                info.Quality == Quality.Superior && info.StageCount == 2 && !info.IsHarvestReady,
                "public growable query exposes custom identity, seed quality and stage readiness");
            var inventory = new Inventory(); Invoke(custom, "HarvestInto", inventory);
            check(harvested.Count == 0 && inventory.Items.Count == 0, "immature custom harvest attempts never award XP");
            Invoke(custom, "Advance", definition, 1d, GrowableGrowthReason.Growth);
            check(grown.Count == 1 && grown[0].Before.StageIndex == 0 && grown[0].After.IsHarvestReady,
                "custom growth reports an actual stage transition with before/after snapshots");
            custom.RefreshArt(definition.QualifiedId); custom.RestoreInstance();
            check(grown.Count == 1, "art refresh and restoration do not masquerade as growth");
            int bonusCalls = 0;
            EventHandler<GrowableHarvestItemEventArgs> reentrant = (_, _) =>
            { bonusCalls++; Invoke(custom, "HarvestInto", inventory); };
            GrowableApi.PreparingHarvestItem += reentrant;
            try { Invoke(custom, "HarvestInto", inventory); }
            finally { GrowableApi.PreparingHarvestItem -= reentrant; }
            check(inventory.Items.Count == 4 && inventory.Items.All(i => i.GetItemComponents<IQualityItemComponent>().Single().Quality == Quality.Pristine) &&
                bonusCalls == 2 && inventory.Items.Distinct().Count() == 4,
                "custom harvest modifiers add independent copies and quality once per rolled item despite failed/reentrant subscribers");
            check(harvested.Count == 1 && harvested[0].ItemCount == 4 && harvested[0].Plant.Quality == Quality.Superior &&
                ReferenceEquals(harvested[0].Actor, inventory.owner) && harvested[0].DroppedItems.Count == 0 &&
                grown.Last().Reason == GrowableGrowthReason.Regrowth && grown.Last().After.StageIndex == 0,
                "custom completed harvest supplies actor, original quality, delivered items and a distinct regrowth transition");
            check(harvested[0].InventoryItems is System.Collections.ObjectModel.ReadOnlyCollection<Item>,
                "completed output lists cannot be structurally modified by subscribers");
            custom = Plant(ripe: true); inventory = new Inventory { Full = true }; LootDropper.Dropped.Clear();
            Invoke(custom, "HarvestInto", inventory);
            check(harvested.Last().InventoryItems.Count == 0 && harvested.Last().DroppedItems.Count == 4 && LootDropper.Dropped.Count == 4,
                "custom overflow is reported separately and only after the ground drop succeeds");
            custom = Plant(ripe: true); inventory = new Inventory { ThrowAfterAdd = true };
            Invoke(custom, "HarvestInto", inventory);
            check(harvested.Last().InventoryItems.Count == 4 && harvested.Last().DroppedItems.Count == 0,
                "post-insertion inventory exceptions do not duplicate XP or report fake overflow");
            int beforeEvents = harvested.Count;
            custom = Plant(ripe: true); GrowableRegistry.FailItem = "Apple";
            Invoke(custom, "HarvestInto", new Inventory()); GrowableRegistry.FailItem = null;
            check(harvested.Count == beforeEvents && GrowableApi.TryGetPlant(custom.gameObject, out info) && info.IsHarvestReady,
                "failed output resolution leaves the crop ready and publishes no completed-harvest event");
            using var save = new MemoryStream(); custom.Serialize(new BinaryWriter(save)); save.Position = 0;
            int beforeGrowth = grown.Count;
            var restored = new CustomGrowable(); restored.Deserialize(new BinaryReader(save));
            check(grown.Count == beforeGrowth && harvested.Count == beforeEvents, "loading a ripe save never grants growth or harvest XP");
            custom = Plant(); Invoke(custom, "Advance", definition, 1d, GrowableGrowthReason.OfflineCatchUp);
            check(grown.Last().Reason == GrowableGrowthReason.OfflineCatchUp, "offline custom stage changes are explicitly distinguishable");

            var seed = Product(); var seeds = new Inventory(); seeds.TryAddItem(seed);
            var use = new ItemComponent_CustomGrowableSeed(definition.QualifiedId) { item = seed }; seed.Components.Add(use);
            use.OnUse(); Player.Instance.Finished!();
            check(planted.Count == 0, "cancelled custom planting publishes no planted event");
            use.OnUse(); Player.Instance.Success!(); Player.Instance.Success!();
            check(planted.Count == 1 && ReferenceEquals(planted[0].Actor, Player.Instance.gameObject) &&
                ReferenceEquals(planted[0].Seed, seed) && !seeds.ContainsItem(seed),
                "completed custom planting publishes once after seed consumption despite a failed subscriber");

            var crop = new GameObject("prefab_crop_turnip(Clone)").AddComponent<Crop>(); crop.SetMature(true);
            check(GrowableApi.TryGetPlant(crop.gameObject, out info) && info.Id == "native:prefab_crop_turnip" &&
                info.Kind == GrowableKind.NativeCrop && info.IsHarvestReady && info.DisplayName == "Turnip",
                "native crop query normalizes prefab names and reads actual maturity");
            inventory = new Inventory(); beforeEvents = harvested.Count;
            check(NativeGrowableHooks.Deliver(inventory, Product(), crop) && harvested.Count == beforeEvents + 1 && inventory.Items.Count == 2 &&
                harvested.Last().Actor == inventory.owner && harvested.Last().Plant.Kind == GrowableKind.NativeCrop,
                "native crop harvest uses the same bonus and successful-delivery API as custom plants");
            check(!NativeGrowableHooks.CanHarvest(crop) && !NativeGrowableHooks.Deliver(inventory, Product(), crop) &&
                inventory.Items.Count == 2 && harvested.Count == beforeEvents + 1,
                "native duplicate completion cannot grant another yield or XP event");
            var herb = new GameObject("prefab_herb_morningdew").AddComponent<ResourceNode>();
            herb.resourceNodeType = ResourceNodeType.Herb; herb.quality = Quality.Legendary;
            inventory = new Inventory(); NativeGrowableHooks.Deliver(inventory, Product(Quality.Quality), herb);
            check(harvested.Last().Plant.Kind == GrowableKind.NativeHerb && harvested.Last().Plant.Quality == Quality.Legendary &&
                inventory.Items.All(i => i.GetItemComponents<IQualityItemComponent>().Single().Quality == Quality.Pristine),
                "native herbs expose plant quality separately from the tool-limited output quality that skills can modify");
            var ore = new GameObject("prefab_ore_iron").AddComponent<ResourceNode>(); ore.resourceNodeType = ResourceNodeType.Ore;
            inventory = new Inventory(); beforeEvents = harvested.Count;
            NativeGrowableHooks.Deliver(inventory, Product(), ore);
            check(!GrowableApi.TryGetPlant(ore.gameObject, out _) && harvested.Count == beforeEvents && inventory.Items.Count == 1,
                "ore harvesting is neither modified nor reported as growable harvesting");
            var fullCrop = new GameObject("prefab_crop_turnip").AddComponent<Crop>();
            NativeGrowableHooks.Deliver(new Inventory { Full = true }, Product(), fullCrop);
            check(harvested.Count == beforeEvents, "failed native inventory deliveries do not award XP or invent ground loot");
            var callbackCrop = new GameObject("prefab_crop_turnip").AddComponent<Crop>();
            NativeGrowableHooks.Deliver(new Inventory { ThrowAfterAdd = true }, Product(), callbackCrop);
            check(harvested.Count == beforeEvents + 1 && harvested.Last().ItemCount == 2,
                "native inventory callbacks throwing after insertion still report the items actually received");

            var reentrantCrop = new GameObject("prefab_crop_turnip").AddComponent<Crop>();
            var reentrantInventory = new Inventory(); bool nativeReentryBlocked = false;
            EventHandler<GrowableHarvestItemEventArgs> nativeReentry = (_, _) =>
                nativeReentryBlocked = !NativeGrowableHooks.CanHarvest(reentrantCrop) &&
                    !NativeGrowableHooks.Deliver(reentrantInventory, Product(), reentrantCrop);
            GrowableApi.PreparingHarvestItem += nativeReentry;
            beforeEvents = harvested.Count;
            try { NativeGrowableHooks.Deliver(reentrantInventory, Product(), reentrantCrop); }
            finally { GrowableApi.PreparingHarvestItem -= nativeReentry; }
            check(nativeReentryBlocked && harvested.Count == beforeEvents + 1 && reentrantInventory.Items.Count == 2,
                "native modifier reentry cannot recursively grant output or duplicate completed-harvest events");

            var growingCrop = new GameObject("prefab_crop_turnip").AddComponent<Crop>();
            void MatureNativeCrop()
            {
                object?[] capture = { growingCrop, null };
                NativeGrowablePatchCode.Method(typeof(NativeCropMaturePatch), "Prefix").Invoke(null, capture);
                Invoke(growingCrop, "Mature");
                NativeGrowablePatchCode.Method(typeof(NativeCropMaturePatch), "Postfix").Invoke(null, capture);
            }
            beforeGrowth = grown.Count;
            MatureNativeCrop(); MatureNativeCrop();
            check(grown.Count == beforeGrowth + 1 && !grown.Last().Before.IsHarvestReady && grown.Last().After.IsHarvestReady &&
                    grown.Last().Reason == GrowableGrowthReason.Growth,
                "native maturity hooks publish only the real readiness transition despite duplicate calls and failed subscribers");
            growingCrop.SetMature(false); beforeGrowth = grown.Count; SerializationManager.loadingSave = true;
            try { MatureNativeCrop(); }
            finally { SerializationManager.loadingSave = false; }
            check(grown.Count == beforeGrowth, "native maturity during save restoration does not publish growth activity");

            var noQuality = new Item(); GrowableApi.PrepareOutput(info, new Inventory(), noQuality);
            check(noQuality.Components.Count == 0, "quality modifiers never add unsupported quality components");
            EventHandler<GrowableHarvestItemEventArgs> invalid = (_, e) => { e.Count = int.MaxValue; e.Quality = (Quality)1234; };
            GrowableApi.PreparingHarvestItem += invalid;
            try
            {
                var bounded = GrowableApi.PrepareOutput(info, new Inventory(), Product());
                check(bounded.Count == 100 && bounded.All(i => i.GetItemComponents<IQualityItemComponent>().Single().Quality == Quality.Pristine),
                    "invalid modifier values cannot create unbounded output copies or undefined quality tiers");
            }
            finally { GrowableApi.PreparingHarvestItem -= invalid; }
            EventHandler<GrowableHarvestItemEventArgs> zero = (_, e) => e.Count = -99;
            GrowableApi.PreparingHarvestItem += zero;
            try
            {
                check(GrowableApi.PrepareOutput(info, new Inventory(), Product()).Count == 0, "negative output counts clamp to zero");
                var suppressedCrop = new GameObject("prefab_crop_turnip").AddComponent<Crop>();
                var emptyInventory = new Inventory(); beforeEvents = harvested.Count;
                NativeGrowableHooks.Deliver(emptyInventory, Product(), suppressedCrop);
                check(emptyInventory.Items.Count == 0 && harvested.Count == beforeEvents && !NativeGrowableHooks.CanHarvest(suppressedCrop),
                    "suppressing every native output commits without fake XP or another harvest attempt");
            }
            finally { GrowableApi.PreparingHarvestItem -= zero; }

            var nativeSeed = new Item(); var nativeInventory = new Inventory(); nativeInventory.TryAddItem(nativeSeed);
            var closure = new SeedClosure { Seeds = new ItemComponent_Seeds { item = nativeSeed } };
            var prefab = new GameObject("prefab_crop_turnip"); prefab.AddComponent<Crop>();
            int beforePlanting = planted.Count;
            var scope = NativeGrowableHooks.BeginPlanting(closure);
            NativeGrowableHooks.InstantiateSeed(prefab, new Vector3(2, 3, 1), new Quaternion());
            NativeGrowableHooks.EndPlanting(scope);
            check(planted.Count == beforePlanting, "native planting that fails before seed consumption grants no XP");
            scope = NativeGrowableHooks.BeginPlanting(closure);
            NativeGrowableHooks.InstantiateSeed(prefab, new Vector3(2, 3, 1), new Quaternion());
            nativeSeed.RemoveItemFromInventory(); NativeGrowableHooks.EndPlanting(scope); NativeGrowableHooks.EndPlanting(scope);
            check(planted.Count == beforePlanting + 1 && planted.Last().Plant.Kind == GrowableKind.NativeCrop &&
                planted.Last().Plant.Position.Equals(new Vector2Int { x = 2, y = 3 }) && planted.Last().Seed == nativeSeed,
                "native completion reports the actual instance, tile and consumed seed exactly once");
            NativeGrowableHooks.InstantiateSeed(prefab, new Vector3(), new Quaternion());
            check(planted.Count == beforePlanting + 1, "unscoped spawning/world generation never awards planting XP");
            var outerSeed = new Item(); var innerSeed = new Item();
            nativeInventory.TryAddItem(outerSeed); nativeInventory.TryAddItem(innerSeed);
            var outerScope = NativeGrowableHooks.BeginPlanting(new SeedClosure { Seeds = new ItemComponent_Seeds { item = outerSeed } });
            var outerPlant = NativeGrowableHooks.InstantiateSeed(prefab, new Vector3(4, 5, 1), new Quaternion());
            var innerScope = NativeGrowableHooks.BeginPlanting(new SeedClosure { Seeds = new ItemComponent_Seeds { item = innerSeed } });
            var innerPlant = NativeGrowableHooks.InstantiateSeed(prefab, new Vector3(6, 7, 1), new Quaternion());
            beforePlanting = planted.Count;
            innerSeed.RemoveItemFromInventory(); NativeGrowableHooks.EndPlanting(innerScope);
            outerSeed.RemoveItemFromInventory(); NativeGrowableHooks.EndPlanting(outerScope);
            check(planted.Count == beforePlanting + 2 && planted[^2].Plant.GameObject == innerPlant && planted[^1].Plant.GameObject == outerPlant,
                "nested native planting scopes keep each spawned plant paired with its own consumed seed");
            var timed = new GameObject("prefab_crop_oak").AddComponent<InGameTimeCrop>();
            var tree = NativeGrowableHooks.InstantiateMature(new GameObject("prefab_tree_oak"), new Vector3(), new Quaternion(), timed);
            check(grown.Last().Before.Kind == GrowableKind.NativeTimedCrop && grown.Last().After.GameObject == tree &&
                grown.Last().After.Kind == GrowableKind.NativePlant && !grown.Last().After.IsHarvestReady,
                "timed sapling maturation links the old instance to the replacement without treating tree felling as harvest");
            beforeGrowth = grown.Count; beforeEvents = harvested.Count; beforePlanting = planted.Count;
            SerializationManager.loadingSave = true;
            GrowableApi.NotifyPlanted(tree, Player.Instance.gameObject, null);
            NativeGrowableHooks.InstantiateMature(new GameObject("prefab_tree_oak"), new Vector3(), new Quaternion(), timed);
            GrowableApi.NotifyHarvested(info, new Inventory(), new List<Item> { Product() }, new());
            check(planted.Count == beforePlanting && grown.Count == beforeGrowth && harvested.Count == beforeEvents,
                "save restoration suppresses activity notifications");
            var loadingOutput = GrowableApi.PrepareOutput(info, new Inventory(), Product(Quality.Superior));
            check(loadingOutput.Count == 1 && loadingOutput[0].GetItemComponents<IQualityItemComponent>().Single().Quality == Quality.Superior,
                "save restoration bypasses yield and quality modifiers as well as XP notifications");
            SerializationManager.loadingSave = false;

            CheckPatchShapes(check);
        }
        finally
        {
            GrowableApi.Planted -= brokenPlanting; GrowableApi.Planted -= planting;
            GrowableApi.GrowthChanged -= brokenGrowth; GrowableApi.GrowthChanged -= growth;
            GrowableApi.Harvested -= brokenHarvest; GrowableApi.Harvested -= harvest;
            GrowableApi.PreparingHarvestItem -= bonus; GrowableApi.PreparingHarvestItem -= brokenBonus;
            GrowableRegistry.ItemFactory = oldFactory; GrowableRegistry.FailItem = null; SerializationManager.loadingSave = false;
        }
        check(!GrowableApi.HasGrowthListeners && !GrowableApi.HasHarvestListeners, "API subscriptions can be removed without leaked observers");
        var unobserved = new GameObject("prefab_crop_turnip").AddComponent<Crop>(); var destination = new Inventory();
        NativeGrowableHooks.Deliver(destination, Product(), unobserved);
        check(destination.Items.Count == 1 && destination.Items[0].GetItemComponents<IQualityItemComponent>().Single().Quality == Quality.Quality,
            "without subscribers native harvests keep their original count and quality");
    }

    private static void CheckPatchShapes(Action<bool, string> check)
    {
        var targets = (IEnumerable<MethodBase>)NativeGrowablePatchCode.Method(typeof(NativeGrowableHarvestPatch), "TargetMethods").Invoke(null, null)!;
        foreach (MethodBase target in targets)
        {
            // Execute the production rewrite on explicit instruction shapes.
            // The shipped Unity/Mono Harmony cannot run on this .NET 9 host;
            // BinaryChecks separately validates the real game's call sites.
            var label = new DynamicMethod("BranchMetadataTest", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
            var call = new CodeInstruction(OpCodes.Callvirt, NativeGrowablePatchCode.Method(typeof(Inventory), "TryAddItem"));
            call.labels.Add(label);
            var code = new[] { call };
            var patched = ((IEnumerable<CodeInstruction>)NativeGrowablePatchCode.Method(typeof(NativeGrowableHarvestPatch), "Transpiler")
                .Invoke(null, new object[] { code })!).ToList();
            check(patched.Count(i => i.operand is MethodInfo m && m.Name == "Deliver" && m.DeclaringType == typeof(NativeGrowableHooks)) == 1,
                "native harvest transpiler replaces exactly the intended delivery call: " + target.DeclaringType!.Name);
            check(patched.Count == 2 && patched[0].opcode == OpCodes.Ldarg_0 && patched[0].labels.Contains(label) &&
                    patched[1].labels.Count == 0 && patched[1].opcode == OpCodes.Call,
                "native harvest rewrite supplies the source instance and preserves branch-target stack correctness: " + target.DeclaringType!.Name);
        }
        var instantiate = typeof(UnityEngine.Object).GetMethods().Single(m => m.Name == "Instantiate" && m.IsGenericMethod).MakeGenericMethod(typeof(GameObject));
        var seedCode = ((IEnumerable<CodeInstruction>)NativeGrowablePatchCode.Method(typeof(NativeSeedPlantPatch), "Transpiler")
            .Invoke(null, new object[] { new[] { new CodeInstruction(OpCodes.Call, instantiate),
                new CodeInstruction(OpCodes.Callvirt, NativeGrowablePatchCode.Method(typeof(Item), "RemoveItemFromInventory")) } })!).ToList();
        check(seedCode.Count(i => i.operand is MethodInfo m && m.Name == "InstantiateSeed") == 1 &&
            seedCode.Any(i => i.operand is MethodInfo m && m.Name == "RemoveItemFromInventory"),
            "native planting instrumentation captures the spawned instance and preserves the seed debit");
        var matureCode = ((IEnumerable<CodeInstruction>)NativeGrowablePatchCode.Method(typeof(NativeTimedCropMaturePatch), "Transpiler")
            .Invoke(null, new object[] { new[] { new CodeInstruction(OpCodes.Call, instantiate) } })!).ToList();
        check(matureCode.Count(i => i.operand is MethodInfo m && m.Name == "InstantiateMature") == 1,
            "timed growth instrumentation hooks replacement only, preserving native growth/visibility rules");
        bool rejected = false;
        try { NativeGrowablePatchCode.Replace(new[] { new CodeInstruction(OpCodes.Ret) }, _ => false, "Deliver", true).ToList(); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected, "unexpected native hook shapes fail explicitly rather than silently missing activity");
    }
}
