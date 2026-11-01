#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace SilverpineMods.CustomGrowables;

public sealed class CustomGrowable :
    MonoBehaviour,
    ISerializableMonoBehavior,
    IInteractionHandler,
    INPCVisibleObject,
    INPCInteractable
{
    [SerializeField]
    private string growableId = "";

    private int stageIndex;
    private double stageProgress;
    private double pendingOfflineSeconds;
    private string? pendingOfflineTimestamp;
    private GrowableDefinition? cachedDefinition;
    private bool hasMatured;
    private ActionQueue? turnPublisher;
    private bool beingRemoved;
    private bool harvesting;
    private Quality seedQuality;
    private StageLight? stageLight;

    public string InteractionName => "Harvest";

    internal void SetSeedQuality(Quality quality) => seedQuality = GrowableQuality.Normalize(quality);

    internal void Configure(string qualifiedId)
    {
        growableId = qualifiedId;
        cachedDefinition = null;
        stageIndex = 0;
        stageProgress = 0f;
        seedQuality = Quality.Normal;
        pendingOfflineSeconds = 0;
        pendingOfflineTimestamp = null;
        beingRemoved = false;
        hasMatured = TryGetDefinition(out GrowableDefinition definition) &&
            definition.HasReachedMaturityStage(stageIndex);
        ApplyStage();
    }

    internal void RestoreInstance()
    {
        gameObject.hideFlags = HideFlags.None;
        ApplyStage();
    }

    internal void RefreshArt(string qualifiedId)
    {
        if (growableId.Equals(
                qualifiedId,
                StringComparison.OrdinalIgnoreCase))
            ApplyStage();
    }

    private void Start()
    {
        ApplyStage();
        if (TryGetDefinition(out GrowableDefinition definition) && definition.HasReachedMaturityStage(stageIndex))
            hasMatured = true;
        TrySubscribeToTurns();
    }

    private void FixedUpdate()
    {
        if (beingRemoved || SerializationManager.loadingSave ||
            !TryGetDefinition(out GrowableDefinition definition))
            return;
        // Plant objects deserialize before WorldInfoManager restores the saved season.
        // Credit elapsed time once, only after the destination world is ready.
        if (!ApplyPendingOfflineGrowth(definition)) return;
        if (definition.Clock == GrowableClock.GameTurns)
        {
            TrySubscribeToTurns();
            return;
        }
        if (!definition.IsHarvestReady(stageIndex) && CanGrowNow(definition))
            Advance(definition, Time.fixedUnscaledDeltaTime);
    }

    private void OnEnable() => ApplyStage();

    private void OnDisable()
    {
        UnsubscribeFromTurns();
        stageLight?.Clear();
    }

    private void UnsubscribeFromTurns()
    {
        if (turnPublisher != null)
            turnPublisher.OnPostProcessTurn -= OnPostProcessTurn;
        turnPublisher = null;
    }

    private void TrySubscribeToTurns()
    {
        if (TryGetDefinition(out GrowableDefinition current) && current.IsHarvestReady(stageIndex))
        { UnsubscribeFromTurns(); return; }
        if (turnPublisher == ActionQueue.Instance) return;
        UnsubscribeFromTurns();
        if (!isActiveAndEnabled || ActionQueue.Instance == null ||
            !TryGetDefinition(out GrowableDefinition definition) ||
            definition.Clock != GrowableClock.GameTurns)
            return;
        turnPublisher = ActionQueue.Instance;
        turnPublisher.OnPostProcessTurn += OnPostProcessTurn;
    }

    private void OnPostProcessTurn()
    {
        if (beingRemoved || !isActiveAndEnabled || SerializationManager.loadingSave ||
            !TryGetDefinition(out GrowableDefinition definition) ||
            definition.Clock != GrowableClock.GameTurns ||
            definition.IsHarvestReady(stageIndex) ||
            !CanGrowNow(definition))
            return;
        Advance(definition, 1f);
    }

    private bool CanGrowNow(GrowableDefinition definition)
    {
        if (definition.GrowthSeasonMask == GrowthSeasons.All) return true;
        if (WorldInfoManager.Instance == null || DungeonGenerationManager.Instance == null) return false;
        return GrowthSeasons.Allows(definition.GrowthSeasonMask,
            (int)WorldInfoManager.Instance.GetSeasonFor(gameObject));
    }

    private bool ApplyPendingOfflineGrowth(GrowableDefinition definition)
    {
        if (pendingOfflineSeconds <= 0) return true;
        if (SerializationManager.loadingSave || WorldInfoManager.Instance == null || DungeonGenerationManager.Instance == null)
            return false;
        double elapsed = pendingOfflineSeconds;
        pendingOfflineSeconds = 0; pendingOfflineTimestamp = null;
        if (CanGrowNow(definition)) Advance(definition, elapsed);
        return true;
    }

    private void Advance(GrowableDefinition definition, double amount)
    {
        if (GrowthProgress.Advance(definition, ref stageIndex, ref stageProgress, ref hasMatured, amount))
            ApplyStage();
    }

    private void ApplyStage()
    {
        if (!TryGetDefinition(out GrowableDefinition definition) ||
            definition.stages == null || definition.stages.Count == 0)
            return;
        stageIndex = Mathf.Clamp(stageIndex, 0, definition.stages.Count - 1);
        GrowableStageDefinition stage = definition.stages[stageIndex];
        if (definition.Template != null && gameObject != definition.Template)
        {
            Vector3 position = transform.position;
            position.z = definition.Template.transform.position.z;
            transform.position = position;
        }
        transform.localScale = Vector3.one * definition.scale * stage.scale;
        SpriteRenderer? renderer = GetComponent<SpriteRenderer>();
        if (renderer != null)
            renderer.sprite = stage.RuntimeSprite;
        if (!beingRemoved && isActiveAndEnabled && gameObject != definition.Template && stage.light?.enabled == true)
        {
            stageLight ??= new StageLight();
            stageLight.Apply(transform, stage.light);
        }
        else stageLight?.Clear();
        YSorter? sorter = GetComponent<YSorter>();
        if (renderer != null && sorter != null && gameObject.activeInHierarchy)
        {
            // OnEnable/restore can run before YSorter.Awake caches its renderer.
            // Match native tree sorting directly; keep YSorter for its normal
            // lifecycle without calling into its uninitialized private state.
            renderer.sortingOrder = Mathf.RoundToInt((0f - transform.position.y) * 10f) + sorter.offset;
        }
        if (isActiveAndEnabled) TrySubscribeToTurns();
    }

    public void Serialize(BinaryWriter binaryWriter)
    {
        if (TryGetDefinition(out GrowableDefinition pendingDefinition)) ApplyPendingOfflineGrowth(pendingDefinition);
        var storage = new VersionSafeStorage();
        storage.AddToStorage("growableId", growableId);
        storage.AddToStorage("seedQuality", (int)seedQuality);
        storage.AddToStorage("stageIndex", stageIndex);
        if (TryGetDefinition(out GrowableDefinition stageDefinition) && stageDefinition.stages != null &&
            stageIndex >= 0 && stageIndex < stageDefinition.stages.Count)
            storage.AddToStorage("stageId", stageDefinition.stages[stageIndex].cacheId ?? "");
        // Keep the existing save field/type for backwards compatibility. Rounding here
        // is sub-second for ordinary stages; accumulation in memory remains double.
        storage.AddToStorage("stageProgress", (float)Math.Min(float.MaxValue, stageProgress));
        storage.AddToStorage("hasMatured", hasMatured);
        storage.AddToStorage(
            "utcNow",
            pendingOfflineTimestamp ?? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        storage.Serialize(binaryWriter);
    }

    public void Deserialize(BinaryReader binaryReader)
    {
        var storage = new VersionSafeStorage();
        storage.Deserialize(binaryReader);
        growableId = storage.GetString("growableId", growableId);
        SetSeedQuality((Quality)storage.GetInt("seedQuality", (int)Quality.Normal));
        cachedDefinition = null;
        pendingOfflineSeconds = 0;
        pendingOfflineTimestamp = null;
        beingRemoved = false; harvesting = false;
        stageIndex = storage.GetInt("stageIndex", stageIndex);
        string savedStageId = storage.GetString("stageId", "");
        if (!string.IsNullOrEmpty(savedStageId) && TryGetDefinition(out GrowableDefinition stageDefinition))
        {
            int restoredStage = stageDefinition.stages!.FindIndex(stage => string.Equals(stage.cacheId, savedStageId, StringComparison.OrdinalIgnoreCase));
            if (restoredStage >= 0) stageIndex = restoredStage;
        }
        stageProgress = storage.GetFloat("stageProgress", 0f);
        if (double.IsNaN(stageProgress) || double.IsInfinity(stageProgress) || stageProgress < 0)
            stageProgress = 0;
        if (TryGetDefinition(out GrowableDefinition loadedDefinition))
            stageIndex = Mathf.Clamp(stageIndex, 0, loadedDefinition.stages!.Count - 1);
        bool inferredMaturity = TryGetDefinition(
                out GrowableDefinition maturityDefinition) &&
            maturityDefinition.HasReachedMaturityStage(stageIndex);
        hasMatured = storage.GetBool("hasMatured", inferredMaturity);
        string savedUtc = storage.GetString(
            "utcNow",
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        if (TryGetDefinition(out GrowableDefinition definition) &&
            definition.Clock == GrowableClock.RealTimeHours &&
            !definition.IsHarvestReady(stageIndex) &&
            DateTime.TryParse(
                savedUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime parsed))
        {
            pendingOfflineSeconds = Math.Max(
                0d,
                DateTime.UtcNow.Subtract(parsed.ToUniversalTime()).TotalSeconds);
            if (pendingOfflineSeconds > 0) pendingOfflineTimestamp = savedUtc;
        }
        ApplyStage();
    }

    public string GetNPCVisibleObjectDescription()
    {
        if (!TryGetDefinition(out GrowableDefinition definition))
            return "An unknown plant";
        string quality = seedQuality == Quality.Normal ? "" : $" ({seedQuality} quality)";
        if (definition.IsHarvestReady(stageIndex))
            return definition.perennial
                ? "A harvest-ready " +
                    definition.DisplayName.ToLowerInvariant() + quality
                : "A mature " + definition.DisplayName.ToLowerInvariant() + quality;
        if (hasMatured)
            return "A mature " + definition.DisplayName.ToLowerInvariant() + quality;
        if (stageIndex == 0)
            return "A " + definition.DisplayName.ToLowerInvariant() +
                " seedling" + quality;
        return "A growing " + definition.DisplayName.ToLowerInvariant() + quality;
    }

    public void OnInteract(GameObject sender)
    {
        if (this == null || !isActiveAndEnabled || beingRemoved || harvesting ||
            !TryGetDefinition(out GrowableDefinition definition))
            return;

        if (!definition.IsHarvestReady(stageIndex))
        {
            DialogBox.Instance.DisplayTextNoDialog(
                "It's not ready to harvest yet. Destroy it?",
                new DialogOption("Yes", BeginDestroyInteraction),
                new DialogOption("No", null));
            return;
        }

        Vector2Int position = transform.GetVector2IntPosition();
        AudioPlayer.Instance.PlayLoopAtPosition(
            "sound_loop_shoveling",
            1f,
            position);
        Player.Instance.StartLongInteraction(
            () => HarvestInto(Player.Instance.worldInventory.inventory),
            definition.interactionTurns,
            null,
            () => AudioPlayer.Instance.StopLoop(position));
    }

    private void BeginDestroyInteraction()
    {
        if (this == null || !isActiveAndEnabled || beingRemoved || harvesting)
            return;
        Vector2Int position = transform.GetVector2IntPosition();
        AudioPlayer.Instance.PlayLoopAtPosition(
            "sound_loop_shoveling",
            1f,
            position);
        Player.Instance.StartLongInteraction(
            RemovePlant,
            TryGetDefinition(out GrowableDefinition definition)
                ? definition.interactionTurns
                : 5,
            null,
            () => AudioPlayer.Instance.StopLoop(position));
    }

    private void HarvestInto(Inventory inventory)
    {
        if (this == null || !isActiveAndEnabled || beingRemoved || harvesting || inventory == null ||
            !TryGetDefinition(out GrowableDefinition definition) ||
            !definition.IsHarvestReady(stageIndex))
            return;
        harvesting = true;
        try
        {
            // Resolve every output first. A missing/broken item must not consume the
            // crop or grant earlier outputs again on the next attempt.
            var results = new List<Item>();
            try
            {
                foreach (GrowableHarvestDefinition output in definition.harvest!)
                {
                    if (output.chance <= 0f || (output.chance < 1f && UnityEngine.Random.value >= output.chance)) continue;
                    int count = UnityEngine.Random.Range(output.minimum, output.maximum + 1);
                    for (int i = 0; i < count; i++)
                    {
                        if (!GrowableRegistry.TryCreateItem(output.item!, out Item item))
                            throw new InvalidOperationException("Cannot create harvest item '" + output.item + "'.");
                        GrowableQuality.ApplyHarvest(item, seedQuality, definition);
                        results.Add(item);
                    }
                }
            }
            catch (Exception error)
            {
                Plugin.Log.LogError($"Harvest of '{growableId}' was not consumed: {error}");
                return;
            }

            Vector2Int position = transform.GetVector2IntPosition();
            // Commit readiness before inventory events, which can invoke other mods.
            if (definition.regrowStage >= 0)
            {
                stageIndex = definition.regrowStage;
                stageProgress = 0;
                if (!definition.perennial) hasMatured = definition.HasReachedMaturityStage(stageIndex);
            }
            else PrepareForRemoval();

            var overflow = new List<Item>();
            foreach (Item item in results)
            {
                bool added;
                try { added = inventory.TryAddItem(item); }
                catch (Exception error)
                {
                    // Native TryAddItem can throw from a callback after inserting the item.
                    added = inventory.ContainsItem(item);
                    Plugin.Log.LogWarning("Harvest inventory callback failed: " + error.Message);
                }
                if (!added) overflow.Add(item);
                else if (inventory.owner != null)
                    try { AnimationHelper.Instance.SpawnItemFloatingText(item, inventory.owner.transform); }
                    catch (Exception error) { Plugin.Log.LogWarning("Harvest text failed: " + error.Message); }
            }
            try
            {
                if (overflow.Count > 0) LootDropper.DropLoot(overflow, position);
            }
            finally
            {
                try { AnimationHelper.Instance.SpawnFadeDummy(gameObject); }
                catch (Exception error) { Plugin.Log.LogWarning("Harvest visual failed: " + error.Message); }
                if (beingRemoved) UnityEngine.Object.Destroy(gameObject);
                else ApplyStage();
            }
        }
        finally { harvesting = false; }
    }

    internal void PrepareForRemoval() { beingRemoved = true; UnsubscribeFromTurns(); stageLight?.Clear(); }

    private void RemovePlant()
    {
        if (this == null || !isActiveAndEnabled || beingRemoved || harvesting)
            return;
        PrepareForRemoval();
        try { AnimationHelper.Instance.SpawnFadeDummy(gameObject); }
        finally { UnityEngine.Object.Destroy(gameObject); }
    }

    public string GetNPCFunctionCallQuestion(NeuralNPC neuralNPC)
    {
        string name = TryGetDefinition(out GrowableDefinition definition)
            ? definition.DisplayName.ToLowerInvariant()
            : "plant";
        return "In this excerpt, did " + neuralNPC.GetFinalName() +
            " harvest the " + name + "?";
    }

    public string GetNPCFunctionCallAPIName(NeuralNPC neuralNPC)
    {
        string name = TryGetDefinition(out GrowableDefinition definition)
            ? definition.DisplayName
            : "plant";
        return "harvest_" + name.ToLowerInvariant().Replace(" ", "_");
    }

    public bool IsNPCInteractableActive(
        NeuralNPC neuralNPC,
        string conversation)
    {
        return this != null && isActiveAndEnabled && !beingRemoved && !harvesting && TryGetDefinition(out GrowableDefinition definition) &&
            definition.IsHarvestReady(stageIndex) &&
            conversation.ContainsAnyWordIgnoreCase(
                definition.DisplayName,
                definition.DisplayName + "s");
    }

    public bool IsNPCInteractableAPIActive(NeuralNPC neuralNPC)
    {
        return this != null && isActiveAndEnabled && !beingRemoved && !harvesting && TryGetDefinition(out GrowableDefinition definition) &&
            definition.IsHarvestReady(stageIndex);
    }

    public Task OnNPCInteract(NeuralNPC neuralNPC)
    {
        if (neuralNPC != null && IsNPCInteractableAPIActive(neuralNPC))
        {
            WorldInventory? target = neuralNPC.GetComponent<WorldInventory>();
            if (target != null) HarvestInto(target.inventory);
        }
        return Task.CompletedTask;
    }

    public bool ShouldPathOneWay() => false;

    private bool TryGetDefinition(out GrowableDefinition definition)
    {
        if (cachedDefinition != null)
        {
            definition = cachedDefinition;
            return true;
        }
        if (!GrowableRegistry.TryGet(growableId, out definition)) return false;
        cachedDefinition = definition;
        return true;
    }
}
