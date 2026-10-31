#nullable enable

using Newtonsoft.Json;
using Silverpine.ModdingTools;
using SilverpineMods.CustomItemLoader;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SilverpineMods.CustomGrowables;

/// <summary>
/// A growable-specific editor that deliberately leaves item creation and pack
/// ownership to Custom Item Loader. It reads and writes only the add-on-owned
/// growable extension through CustomItemApi.
/// </summary>
internal sealed partial class GrowableEditorUI : ModToolBehaviour
{
    private const float DesignWidth = 1920f;
    private const float DesignHeight = 1080f;
    private const float TerrainTileScreenSize = 96f;
    private const string ExtensionName = "growable";

    private static readonly string[] Clocks =
        { nameof(GrowableClock.RealTimeHours), nameof(GrowableClock.GameTurns) };
    private static readonly string[] HarvestModes =
        { "Remove after harvest", "Regrowing crop", "Perennial plant" };

    private static GrowableEditorUI? instance;

    private readonly List<CustomItemInfo> items = new();
    private readonly Dictionary<string, string> seedConflicts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> growableIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> numberText = new();
    private readonly HashSet<string> invalidNumbers = new();
    private string? loadedExtensionJson;
    private string loadError = "";
    private readonly List<PickerOption> pickerOptions = new();
    private CustomItemInfo? selectedItem;
    private GrowableDefinition? definition;
    private Vector2 itemScroll;
    private Vector2 detailsScroll;
    private Vector2 pickerScroll;
    private string search = "";
    private string status = "";
    private string pickerTitle = "";
    private string pickerCurrent = "";
    private string pickerSearch = "";
    private Action<string>? pickerCallback;
    private string pickerActionLabel = "";
    private Action? pickerAction;
    private bool open;
    private bool dirty;
    private bool filePickerOpen;
    private bool pickerOpen;
    private bool renderingGlbPreview;
    private PickerOption[]? filteredPickerOptions;
    private string? filteredPickerSearch;
    private int pickerPage;
    private const int PickerPageSize = 64;
    private int glbPreviewStage = -1;
    private Sprite? glbPreview;
    private Sprite? grassTerrainSprite;
    private bool grassTerrainLookupAttempted;

    private sealed class PickerOption
    {
        internal string Value = "";
        internal string Label = "";
        internal string SearchText = "";
    }

    internal static void Open(ModToolSession session)
    {
        if (instance == null)
        {
            GameObject root = new("Custom Growables Editor IMGUI");
            instance = root.AddComponent<GrowableEditorUI>();
        }
        else
        {
            instance.gameObject.SetActive(true);
        }

        instance.AttachSession(session);
        session.EscapeHandler = instance.HandleEscape;
        instance.open = true;
        instance.grassTerrainSprite = null;
        instance.grassTerrainLookupAttempted = false;
        instance.RefreshItems();
    }

    private void Update()
    {
        if (filePickerOpen &&
            (GenericListUI.Instance == null ||
             !GenericListUI.Instance.gameObject.activeInHierarchy))
            filePickerOpen = false;

        if (open && Input.GetKeyDown(KeyCode.Escape)) HandleEscape();
    }

    private void OnGUI()
    {
        if (!open || filePickerOpen)
            return;
        using ModGuiScope scope = ModGui.BeginScaled(DesignWidth, DesignHeight);
        GUISkin previousSkin = GUI.skin;
        EnsureEditorSkin();
        GUI.skin = editorSkin!;
        try { DrawEditorShell(); DrawEditorTooltip(); }
        finally { GUI.skin = previousSkin; }
    }

    private void DrawEditorShell()
    {
        GUI.enabled = true;
        GUI.color = Color.white;
        GUI.backgroundColor = Color.white;
        GUI.depth = -1000;

        Color old = GUI.color;
        GUI.color = new Color(0.045f, 0.065f, 0.075f, 0.99f);
        GUI.DrawTexture(
            new Rect(0, 0, DesignWidth, DesignHeight),
            Texture2D.whiteTexture);
        GUI.color = old;

        GUILayout.BeginArea(
            new Rect(12, 10, DesignWidth - 24, DesignHeight - 20));
        if (closeConfirmation) { DrawCloseConfirmation(); GUILayout.EndArea(); return; }
        if (libraryOpen) { DrawArtLibrary(); GUILayout.EndArea(); return; }
        if (pickerOpen)
        {
            DrawPickerScreen();
            GUILayout.EndArea();
            return;
        }
        DrawHeader();
        GUILayout.BeginHorizontal();
        DrawItemPanel();
        DrawDetailsPanel();
        DrawPersistentPreview();
        GUILayout.EndHorizontal();
        if (!string.IsNullOrWhiteSpace(status))
            GUILayout.Label(status, GUI.skin.box);
        GUILayout.EndArea();
    }

    private void DrawHeader()
    {
        GUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label("Growables Editor", editorFieldLabel!, GUILayout.Width(220));
        DrawSingleLineLabel(selectedItem == null
            ? "Choose an item registered by Custom Item Loader."
            : selectedItem.QualifiedId + (dirty ? "  • unsaved" : ""));

        GUI.enabled = !dirty;
        if (DrawActionButton("Refresh CIL", 110))
        { RefreshItems(); GUIUtility.ExitGUI(); }
        GUI.enabled = selectedItem != null && dirty;
        if (DrawActionButton("Discard", 90))
        { LoadSelected(); GUIUtility.ExitGUI(); }
        GUI.enabled = selectedItem != null && dirty &&
            !(definition != null && SelectedItemHasUseConflict());
        if (DrawActionButton("Save Pack", 110))
            SaveSelected();
        GUI.enabled = true;
        if (DrawActionButton("Close", 90))
        { RequestClose(); GUIUtility.ExitGUI(); }
        GUILayout.EndHorizontal();
    }

    private void DrawItemPanel()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(SidebarWidth));
        GUILayout.Label("CURRENT PACK", editorMuted!);
        if (DrawSingleLineChoice((string.IsNullOrEmpty(packFilter) ? "All packs" : packFilter) + "  ▼"))
            OpenPackPicker();
        GUILayout.Label("Create seeds in Item Loader.", editorMuted!);
        GUILayout.Space(8);
        GUILayout.Label("Search items", editorFieldLabel!);
        GUILayout.BeginHorizontal();
        search = GUILayout.TextField(search ?? "", GUILayout.MinHeight(44));
        if (DrawActionButton("×", 32))
            search = "";
        GUILayout.EndHorizontal();

        itemScroll = GUILayout.BeginScrollView(itemScroll);
        foreach (CustomItemInfo item in items)
        {
            if (!MatchesSearch(item) || (!string.IsNullOrEmpty(packFilter) && item.PackId != packFilter))
                continue;
            bool selected = ReferenceEquals(selectedItem, item) ||
                selectedItem?.QualifiedId.Equals(
                    item.QualifiedId,
                    StringComparison.OrdinalIgnoreCase) == true;
            string marker = growableIds.Contains(item.QualifiedId) ? "● " : "○ ";
            string inheritedSeed = seedConflicts.TryGetValue(item.QualifiedId, out string conflict)
                ? "! " : "";
            string title = marker + inheritedSeed + item.DisplayName;
            string tooltip = item.DisplayName + "\n" + item.QualifiedId +
                (conflict == null ? "" : "\n" + SeedCompatibility.Explain(conflict));
            Color old = GUI.backgroundColor;
            if (selected)
                GUI.backgroundColor = new Color(0.48f, 0.75f, 0.46f);
            GUI.enabled = !dirty || selected;
            if (DrawItemCard(title, item.QualifiedId, tooltip))
            { SelectItem(item); GUIUtility.ExitGUI(); }
            GUI.enabled = true;
            GUI.backgroundColor = old;
        }
        if (items.Count == 0)
            GUILayout.Label("No registered Custom Item Loader items were found.");
        GUILayout.EndScrollView();
        GUILayout.Label("● Growable   ○ Item   ! Conflict", editorMuted!);
        GUILayout.EndVertical();
    }

    private void DrawDetailsPanel()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
        if (selectedItem == null)
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("Select a Custom Item Loader item.");
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            return;
        }

        if (!string.IsNullOrEmpty(loadError))
        {
            GUILayout.Label(loadError, GUI.skin.box);
            GUILayout.Label("The saved data is untouched. Repair the JSON, then reload, or select another item.");
            if (GUILayout.Button("Reload growable data")) { LoadSelected(); GUIUtility.ExitGUI(); }
            GUILayout.EndVertical();
            return;
        }
        if (definition == null)
        {
            bool incompatible = SelectedItemHasUseConflict();
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                selectedItem.DisplayName + " does not have growable behavior.");
            if (incompatible)
            {
                GUILayout.Label(
                    SeedCompatibility.Explain(seedConflicts[selectedItem.QualifiedId]),
                    GUI.skin.box);
            }
            else
            {
                GUILayout.Label(
                    "The item itself remains fully owned by Custom Item Loader.");
            }
            GUI.enabled = !incompatible;
            if (GUILayout.Button("Add Growable Behavior", GUILayout.MinHeight(44)))
            { AddDefinition(); GUIUtility.ExitGUI(); }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            return;
        }

        detailsScroll = GUILayout.BeginScrollView(detailsScroll);
        if (SelectedItemHasUseConflict())
            GUILayout.Label(
                SeedCompatibility.Explain(seedConflicts[selectedItem.QualifiedId]) +
                " You can still remove Growable Behavior below.",
                GUI.skin.box);
        int nextTab = GUILayout.Toolbar(editorTab, new[] { "Lifecycle", "Stages & Art", "Harvest", "Planting" }, editorAction!, GUILayout.MinHeight(44));
        if (nextTab != editorTab) { CancelOffsetDrag(); editorTab = nextTab; }
        GUILayout.Space(12);
        switch (editorTab)
        {
            case 0: DrawLifecycle(); break;
            case 1: DrawStages(); break;
            case 2: DrawHarvest(); break;
            case 3: DrawPlantingRules(); break;
        }
        GUILayout.Space(12);
        if (GUILayout.Button("Remove Growable Behavior", GUILayout.MinHeight(44)))
            removeConfirmation = true;
        if (removeConfirmation)
        {
            GUILayout.Label("Remove this item's growable behavior when you save?");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Remove"))
            {
                CancelOffsetDrag(); ClearGlbPreview(); definition = null; MarkDirty();
                ClearNumberInputs(); removeConfirmation = false;
                status = "Growable behavior will be removed when the pack is saved.";
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("Cancel")) removeConfirmation = false;
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void DrawLifecycle()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("Lifecycle");
        DrawText(
            "Plant name",
            definition!.visibleName ?? "",
            value => Set(ref definition.visibleName, value),
            allowLineBreaks: true);
        DrawChoice(
            "Growth clock",
            definition.clock ?? Clocks[0],
            Clocks,
            value =>
            {
                if (definition.clock == value)
                    return;
                definition.clock = value;
                if (Enum.TryParse(value, true, out GrowableClock parsed))
                    definition.Clock = parsed;
                MarkDirty();
            });

        int mode = GetHarvestMode();
        DrawChoice(
            "After harvest",
            HarvestModes[mode],
            HarvestModes,
            value => SetHarvestMode(Array.IndexOf(HarvestModes, value)));

        if (mode != 0)
            DrawStageIndex(
                "Return to stage",
                definition.regrowStage,
                Math.Max(0, (definition.stages?.Count ?? 2) - 2),
                value =>
                {
                    definition.regrowStage = value;
                    if (definition.perennial &&
                        definition.matureStage > value)
                        definition.matureStage = value;
                    MarkDirty();
                });
        if (mode == 2)
        {
            DrawStageIndex(
                "Mature from stage",
                definition.matureStage < 0
                    ? definition.regrowStage
                    : definition.matureStage,
                Math.Max(0, (definition.stages?.Count ?? 1) - 1),
                value =>
                {
                    definition.matureStage = value;
                    if (definition.regrowStage < value)
                        definition.regrowStage = Math.Min(
                            value,
                            Math.Max(0, definition.stages!.Count - 2));
                    MarkDirty();
                });
            GUILayout.Label(
                "A perennial becomes mature once, survives harvest, and " +
                "cycles only through mature stages while its harvest renews.",
                GUI.skin.box);
        }

        DrawInt(
            "Interaction turns",
            "interactionTurns",
            definition.interactionTurns,
            value => Set(ref definition.interactionTurns, value));
        DrawFloat(
            "Base world scale",
            "scale",
            definition.scale,
            value => Set(ref definition.scale, value));
        GUILayout.EndVertical();
    }

    private void DrawPlantingRules()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("Planting and environment");
        DrawToggle(
            "Player property only",
            definition!.playerPropertyOnly,
            value => Set(ref definition.playerPropertyOnly, value));
        DrawToggle(
            "Allow beneath roofs",
            definition.allowRoofed,
            value => Set(ref definition.allowRoofed, value));
        DrawGrowthSeasons();
        DrawToggle(
            "Blocks movement",
            definition.blocksMovement,
            value => Set(ref definition.blocksMovement, value));
        DrawToggle(
            "Replace tile plants",
            definition.replaceExistingPlants,
            value => Set(ref definition.replaceExistingPlants, value));
        GUILayout.EndVertical();
    }

    private void DrawGrowthSeasons()
    {
        GUILayout.Label("Growth seasons");
        int mask;
        try { mask = GrowthSeasons.Resolve(definition!.growthSeasons, definition.growsInWinter, allowEmpty: true); }
        catch (InvalidDataException error)
        {
            GUILayout.Label(error.Message);
            if (GUILayout.Button("Reset to all seasons"))
            {
                definition!.growthSeasons = GrowthSeasons.NamesFor(GrowthSeasons.All);
                definition.growsInWinter = true;
                MarkDirty();
            }
            return;
        }
        int original = mask;
        for (int row = 0; row < 2; row++)
        {
            GUILayout.BeginHorizontal();
            for (int i = row * 2; i < row * 2 + 2; i++)
            {
                bool selected = GUILayout.Toggle(GrowthSeasons.Allows(mask, i), GrowthSeasons.Names[i], GUI.skin.button);
                mask = selected ? mask | (1 << i) : mask & ~(1 << i);
            }
            GUILayout.EndHorizontal();
        }
        if (GUILayout.Button("All seasons")) mask = GrowthSeasons.All;
        if (mask != original)
        {
            definition!.growthSeasons = GrowthSeasons.NamesFor(mask);
            definition.growsInWinter = GrowthSeasons.Allows(mask, 3);
            MarkDirty();
        }
        if (mask == 0) GUILayout.Label("Choose at least one season before saving.");
        GUILayout.Label("Growth and regrowth pause outside these seasons without losing progress. " +
            "Planting and harvesting ripe plants are still allowed. Uses the local biome's season.", editorMuted!);
    }

    private void DrawStages()
    {
        definition!.stages ??= new List<GrowableStageDefinition>();
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("Growth and harvest-ready stages");
        GUILayout.Label(
            "The final stage is harvest-ready. Duration belongs to the " +
            "stage it is shown on and controls advancement to the next one.");

        DrawSelectedStageChoice();
        if (definition.stages.Count > 0) DrawStage(selectedStage);

        if (GUILayout.Button("Add Stage"))
        { AddStage(); GUIUtility.ExitGUI(); }
        GUILayout.EndVertical();
    }

    private bool DrawStage(int index)
    {
        GrowableStageDefinition stage = definition!.stages![index];
        bool finalStage = index == definition.stages.Count - 1;
        int action = 0;

        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Label(finalStage
            ? $"Stage {index}: harvest-ready"
            : $"Stage {index}");
        GUI.enabled = index > 0;
        if (DrawActionButton("↑", 36))
            action = -1;
        GUI.enabled = index < definition.stages.Count - 1;
        if (DrawActionButton("↓", 36))
            action = 1;
        GUI.enabled = definition.stages.Count > 2;
        if (DrawActionButton("Remove", 78))
            action = 2;
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        string artMode = stage.image == null
            ? stage.model == null
                ? "Base-game sprite"
                : "GLB model"
            : "Custom image";
        DrawChoice(
            "Art source",
            artMode,
            new[] { "Base-game sprite", "Custom image", "GLB model" },
            value =>
            {
                if (value == "Custom image")
                {
                    stage.sprite = null;
                    stage.model = null;
                    stage.image ??= "assets/plant.png";
                }
                else if (value == "GLB model")
                {
                    stage.sprite = null;
                    stage.image = null;
                    stage.model ??= "assets/plant.glb";
                    stage.rotation ??= new[] { 20f, 135f, 0f };
                }
                else
                {
                    stage.image = null;
                    stage.model = null;
                    stage.sprite ??= "sprite_nature_turnip_mature";
                }
                stage.artAssetId = null; stage.artOriginalName = null;
                ClearNumberInputs();
                MarkDirty();
            });

        if (artMode == "Base-game sprite")
        {
            DrawText("Sprite asset", stage.sprite ?? "", sprite =>
            {
                stage.sprite = sprite;
                stage.artAssetId = null; stage.artOriginalName = null;
                ApplyBaseGameSpriteSettings(stage);
                MarkDirty();
            });
            GUILayout.BeginHorizontal();
            if (DrawActionButton("Choose… ▼", 110))
                OpenArtworkPicker(stage);
            GUILayout.EndHorizontal();
        }
        else if (artMode == "Custom image")
        {
            DrawText("Image", stage.image ?? "", image =>
            {
                stage.image = image;
                stage.artAssetId = null; stage.artOriginalName = null;
                MarkDirty();
            });
            GUILayout.BeginHorizontal();
            if (DrawActionButton("Choose… ▼", 110))
                OpenArtworkPicker(stage);
            if (DrawActionButton("Import…", 90))
                BrowseArtwork(stage);
            GUILayout.EndHorizontal();
            DrawFloat(
                "Pixels per unit",
                $"stage.{index}.ppu",
                stage.pixelsPerUnit,
                value =>
                {
                    stage.pixelsPerUnit = value;
                    MarkDirty();
                });
            GUILayout.BeginHorizontal();
            DrawFieldLabel("Pivot X / Y");
            DrawFloatInline(
                $"stage.{index}.pivotX",
                stage.pivotX,
                value =>
                {
                    stage.pivotX = value;
                    MarkDirty();
                });
            DrawFloatInline(
                $"stage.{index}.pivotY",
                stage.pivotY,
                value =>
                {
                    stage.pivotY = value;
                    MarkDirty();
                });
            GUILayout.EndHorizontal();
        }
        else
        {
            DrawText("GLB model", stage.model ?? "", model =>
            {
                stage.model = model;
                stage.artAssetId = null; stage.artOriginalName = null;
                MarkDirty();
            });
            GUILayout.BeginHorizontal();
            if (DrawActionButton("Choose… ▼", 110))
                OpenArtworkPicker(stage);
            if (DrawActionButton("Import…", 90))
                BrowseArtwork(stage);
            GUILayout.EndHorizontal();

            stage.rotation ??= new[] { 20f, 135f, 0f };
            if (stage.rotation.Length != 3)
                stage.rotation = new[] { 20f, 135f, 0f };
            GUILayout.BeginHorizontal();
            DrawFieldLabel("Rotation X / Y / Z");
            for (int axis = 0; axis < 3; axis++)
            {
                int captured = axis;
                DrawFloatInline(
                    $"stage.{index}.rotation.{axis}",
                    stage.rotation[axis],
                    value =>
                    {
                        stage.rotation[captured] = value;
                        MarkDirty();
                    });
            }
            GUILayout.EndHorizontal();
            DrawFloat(
                "Zoom",
                $"stage.{index}.zoom",
                stage.zoom,
                value =>
                {
                    stage.zoom = value;
                    MarkDirty();
                });
            DrawInt(
                "Render resolution",
                $"stage.{index}.resolution",
                stage.resolution,
                value =>
                {
                    stage.resolution = value;
                    MarkDirty();
                });
            DrawFloat(
                "Pixels per unit",
                $"stage.{index}.ppu",
                stage.pixelsPerUnit,
                value =>
                {
                    stage.pixelsPerUnit = value;
                    MarkDirty();
                });
            GUILayout.BeginHorizontal();
            DrawFieldLabel("Pivot X / Y");
            DrawFloatInline(
                $"stage.{index}.pivotX",
                stage.pivotX,
                value =>
                {
                    stage.pivotX = value;
                    MarkDirty();
                });
            DrawFloatInline(
                $"stage.{index}.pivotY",
                stage.pivotY,
                value =>
                {
                    stage.pivotY = value;
                    MarkDirty();
                });
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "The GLB is rendered once from this camera rotation. " +
                "Growables do not generate directional variants. Source " +
                "models are kept in CIL's shared authoring library; rendered " +
                "stage sprites are stored in this pack's .cache folder.",
                GUI.skin.box);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load Cached Preview"))
                LoadCachedGlbPreview(stage, index);
            GUI.enabled = !renderingGlbPreview;
            if (GUILayout.Button(
                    renderingGlbPreview
                        ? "Rendering GLB preview…"
                        : "Render GLB preview"))
                RenderGlbPreview(stage, index);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        DrawFloat(
            "Stage scale multiplier",
            $"stage.{index}.scale",
            stage.scale,
            value =>
            {
                stage.scale = value;
                MarkDirty();
            });
        if (GUILayout.Button("Choose from shared art library…")) OpenArtLibrary(stage);
        if (string.IsNullOrWhiteSpace(stage.sprite))
        {
            DrawChoice("Anchor mode", string.IsNullOrWhiteSpace(stage.anchorSprite) ? "Custom pivot" : "Match native sprite",
                new[] { "Custom pivot", "Match native sprite" }, value =>
                {
                    if (value == "Custom pivot") { stage.anchorSprite = null; MarkDirty(); }
                    else OpenAnchorPicker(stage);
                });
            if (!string.IsNullOrWhiteSpace(stage.anchorSprite) && DrawSingleLineChoice("Anchor: " + stage.anchorSprite + "  ▼"))
                OpenAnchorPicker(stage);
        }

        if (!finalStage)
            DrawFloat(
                definition.Clock == GrowableClock.GameTurns
                    ? "Duration (turns)"
                    : "Duration (hours)",
                $"stage.{index}.duration",
                stage.duration,
                value =>
                {
                    stage.duration = value;
                    MarkDirty();
                });
        DrawStageLight(stage, index);
        GUILayout.EndVertical();

        if (action == -1)
            MoveStage(index, index - 1);
        else if (action == 1)
            MoveStage(index, index + 1);
        else if (action == 2)
            RemoveStage(index);
        if (action != 0) GUIUtility.ExitGUI();
        return action != 0;
    }

    private void DrawHarvest()
    {
        definition!.harvest ??= new List<GrowableHarvestDefinition>();
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("Harvest results");
        GUILayout.Label(
            "Use a CIL qualified ID (pack:item) or type a base-game item name.");
        GUILayout.Label("Products that support quality inherit the planted seed's quality.", editorMuted!);
        DrawToggle("Chance for higher quality", definition.qualityBonusEnabled,
            value => { Set(ref definition.qualityBonusEnabled, value); GUIUtility.ExitGUI(); });
        if (definition.qualityBonusEnabled)
        {
            for (int tier = 0; tier < GrowableQuality.UpgradeTiers; tier++)
            {
                int captured = tier;
                DrawFloat($"+{tier + 1} tier chance (%)", "harvest.qualityBonus." + tier,
                    GrowableQuality.BonusChance(definition.qualityBonusChances, tier) * 100f, value =>
                    {
                        if (definition.qualityBonusChances == null || definition.qualityBonusChances.Length != GrowableQuality.UpgradeTiers)
                            definition.qualityBonusChances = Enumerable.Range(0, GrowableQuality.UpgradeTiers)
                                .Select(index => GrowableQuality.BonusChance(definition.qualityBonusChances, index)).ToArray();
                        Set(ref definition.qualityBonusChances[captured], value / 100f);
                    });
            }
            float total = GrowableQuality.BonusTotal(definition.qualityBonusChances) * 100f;
            GUILayout.Label(total <= 100.0001f ? $"Unchanged quality: {Math.Max(0, 100f - total):0.##}%" : "Total chance must not exceed 100%.", editorMuted!);
            GUILayout.Label("One roll per quality-capable product. Results cap at Legendary; the plant's quality stays unchanged. " +
                "Returned seeds can also improve.", editorMuted!);
        }

        for (int index = 0; index < definition.harvest.Count; index++)
        {
            GrowableHarvestDefinition output = definition.harvest[index];
            bool remove = false;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Result " + (index + 1));
            GUI.enabled = definition.harvest.Count > 1;
            if (DrawActionButton("Remove", 78))
                remove = true;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            DrawText(
                "Item ID / name",
                output.item ?? "",
                value =>
                {
                    output.item = value;
                    MarkDirty();
                });
            GUILayout.BeginHorizontal();
            DrawFieldLabel("Choose item");
            if (DrawSingleLineChoice(GetHarvestItemLabel(output) + "  ▼"))
                OpenHarvestItemPicker(output);
            GUILayout.EndHorizontal();
            DrawInt(
                "Minimum",
                $"harvest.{index}.minimum",
                output.minimum,
                value =>
                {
                    output.minimum = value;
                    MarkDirty();
                });
            DrawInt(
                "Maximum",
                $"harvest.{index}.maximum",
                output.maximum,
                value =>
                {
                    output.maximum = value;
                    MarkDirty();
                });
            DrawFloat(
                "Chance (0 to 1)",
                $"harvest.{index}.chance",
                output.chance,
                value =>
                {
                    output.chance = value;
                    MarkDirty();
                });
            GUILayout.EndVertical();
            if (remove)
            {
                if (invalidNumbers.Count != 0)
                {
                    status = "Correct invalid numeric input before removing a harvest result.";
                    GUIUtility.ExitGUI();
                    return;
                }
                definition.harvest.RemoveAt(index);
                ClearNumberInputs();
                MarkDirty();
                GUIUtility.ExitGUI();
            }
        }

        if (GUILayout.Button("Add Harvest Result"))
        {
            definition.harvest.Add(new GrowableHarvestDefinition
            {
                item = selectedItem?.QualifiedId,
                minimum = 1,
                maximum = 1,
                chance = 1f
            });
            MarkDirty();
            GUIUtility.ExitGUI();
        }
        GUILayout.EndVertical();
    }

    private void AddDefinition()
    {
        if (selectedItem == null || SelectedItemHasUseConflict())
        {
            status = selectedItem == null ? "Select an item first." :
                SeedCompatibility.Explain(seedConflicts[selectedItem.QualifiedId]);
            return;
        }
        definition = new GrowableDefinition
        {
            visibleName = selectedItem!.DisplayName,
            stages = new List<GrowableStageDefinition>
            {
                new()
                {
                    sprite = "sprite_nature_seeds",
                    duration = 24f
                },
                new()
                {
                    sprite = "sprite_nature_turnip_mature"
                }
            },
            harvest = new List<GrowableHarvestDefinition>
            {
                new()
                {
                    item = selectedItem.QualifiedId,
                    minimum = 1,
                    maximum = 1,
                    chance = 1f
                }
            }
        };
        definition.Clock = GrowableClock.RealTimeHours;
        dirty = true;
        detailsScroll = Vector2.zero;
        status =
            "Growable behavior added. Choose the actual harvest item and art.";
    }

    private int GetHarvestMode()
    {
        if (definition!.perennial)
            return 2;
        return definition.regrowStage >= 0 ? 1 : 0;
    }

    private void SetHarvestMode(int mode)
    {
        if (!TryPrepareStageEdit()) return;
        mode = Mathf.Clamp(mode, 0, HarvestModes.Length - 1);
        definition!.perennial = mode == 2;
        if (mode == 0)
        {
            definition.regrowStage = -1;
            definition.matureStage = -1;
        }
        else if (mode == 1)
        {
            definition.regrowStage = Math.Max(0, definition.regrowStage);
            definition.matureStage = -1;
        }
        else
        {
            try { EnsurePerennialStages(); }
            catch (Exception error)
            {
                definition.perennial = false;
                MarkDirty(); status = "Cannot prepare perennial stages: " + error.Message; return;
            }
            int mature = Math.Max(0, definition.stages!.Count - 2);
            definition.matureStage = definition.matureStage < 0
                ? mature
                : Math.Min(definition.matureStage, mature);
            definition.regrowStage = Math.Max(
                definition.matureStage,
                definition.regrowStage < 0 ? mature : definition.regrowStage);
            definition.regrowStage = Math.Min(
                definition.regrowStage,
                definition.stages.Count - 2);
        }
        ClearNumberInputs();
        MarkDirty();
    }

    private void EnsurePerennialStages()
    {
        definition!.stages ??= new List<GrowableStageDefinition>();
        while (definition.stages.Count < 2)
            definition.stages.Add(NewDefaultStage(
                definition.stages.Count == 0
                    ? "sprite_nature_seeds"
                    : "sprite_nature_turnip_mature",
                definition.stages.Count == 0 ? 24f : 0f));
        if (definition.stages.Count >= 3)
            return;

        GrowableStageDefinition ready = definition.stages[1];
        definition.stages.Insert(1, CopyStageArt(ready, 24f));
    }

    private void AddStage()
    {
        if (!TryPrepareStageEdit()) return;
        definition!.stages ??= new List<GrowableStageDefinition>();
        if (definition.stages.Count == 0)
        {
            definition.stages.Add(NewDefaultStage(
                "sprite_nature_seeds",
                24f));
            definition.stages.Add(NewDefaultStage(
                "sprite_nature_turnip_mature",
                0f));
        }
        else
        {
            GrowableStageDefinition previousFinal =
                definition.stages[definition.stages.Count - 1];
            GrowableStageDefinition copy;
            try { copy = CopyStageArt(previousFinal, 0f); }
            catch (Exception error) { status = "Cannot duplicate stage art: " + error.Message; return; }
            if (previousFinal.duration <= 0f)
                previousFinal.duration = 24f;
            definition.stages.Add(copy);
        }
        NormalizeStageIndices();
        selectedStage = definition.stages.Count - 1;
        ClearGlbPreview();
        ClearNumberInputs();
        MarkDirty();
    }

    private void MoveStage(int from, int to)
    {
        if (definition?.stages == null ||
            from < 0 || from >= definition.stages.Count ||
            to < 0 || to >= definition.stages.Count)
            return;
        if (!TryPrepareStageEdit()) return;
        CancelOffsetDrag(); ClearGlbPreview();
        GrowableStageDefinition stage = definition.stages[from];
        definition.stages.RemoveAt(from);
        definition.stages.Insert(to, stage);
        selectedStage = to;
        definition.regrowStage = RemapMovedIndex(
            definition.regrowStage,
            from,
            to);
        definition.matureStage = RemapMovedIndex(
            definition.matureStage,
            from,
            to);
        NormalizeStageIndices();
        ClearNumberInputs();
        MarkDirty();
    }

    private void RemoveStage(int index)
    {
        if (definition?.stages == null || definition.stages.Count <= 2)
            return;
        if (!TryPrepareStageEdit()) return;
        CancelOffsetDrag(); ClearGlbPreview();
        definition.stages.RemoveAt(index);
        selectedStage = Math.Min(index, definition.stages.Count - 1);
        if (definition.regrowStage > index)
            definition.regrowStage--;
        if (definition.matureStage > index)
            definition.matureStage--;
        NormalizeStageIndices();
        ClearNumberInputs();
        MarkDirty();
    }

    private void NormalizeStageIndices()
    {
        if (definition?.stages == null || definition.stages.Count < 2)
            return;
        int lastNonReady = definition.stages.Count - 2;
        // A former final stage had no timer. Once moved earlier it needs one.
        for (int i = 0; i <= lastNonReady; i++)
            if (definition.stages[i].duration <= 0 || !StageArtIdentity.Finite(definition.stages[i].duration))
                definition.stages[i].duration = 24f;
        if (definition.regrowStage >= 0)
            definition.regrowStage = Mathf.Clamp(
                definition.regrowStage,
                0,
                lastNonReady);
        if (definition.matureStage >= 0)
            definition.matureStage = Mathf.Clamp(
                definition.matureStage,
                0,
                definition.stages.Count - 1);
        if (definition.perennial &&
            definition.matureStage > definition.regrowStage)
            definition.matureStage = definition.regrowStage;
    }

    private string GetHarvestItemLabel(GrowableHarvestDefinition output)
    {
        CustomItemInfo? item = items.FirstOrDefault(value =>
            value.QualifiedId.Equals(
                output.item,
                StringComparison.OrdinalIgnoreCase));
        if (item != null)
            return "CIL • " + item.DisplayName;
        Item? baseItem = ItemLibrary.Items.FirstOrDefault(value =>
            value != null && string.Equals(
                value.name,
                output.item,
                StringComparison.OrdinalIgnoreCase));
        return baseItem == null
            ? "Pick a base-game or CIL item"
            : "Base game • " + baseItem.name;
    }

    private void OpenHarvestItemPicker(GrowableHarvestDefinition output)
    {
        var options = new List<PickerOption>();
        foreach (CustomItemInfo item in items)
            options.Add(new PickerOption
            {
                Value = item.QualifiedId,
                Label = "CIL • " + item.DisplayName + "\n" + item.QualifiedId,
                SearchText = item.DisplayName + " " + item.QualifiedId + " " +
                    item.Category
            });

        var customTemplates = new HashSet<Item>(
            items.Select(value => value.Template));
        foreach (Item item in ItemLibrary.Items
                     .Where(value => value != null &&
                         !customTemplates.Contains(value) &&
                         !string.IsNullOrWhiteSpace(value.name))
                     .GroupBy(value => value.name, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(value => value.name, StringComparer.OrdinalIgnoreCase))
            options.Add(new PickerOption
            {
                Value = item.name,
                Label = "Base game • " + item.name,
                SearchText = item.name + " " + item.itemCategory
            });

        OpenPicker(
            "Choose Harvest Item",
            output.item ?? "",
            options,
            value =>
            {
                output.item = value;
                MarkDirty();
            });
    }

    private void OpenArtworkPicker(GrowableStageDefinition stage)
    {
        var options = new List<PickerOption>();
        foreach (string name in GrowableRegistry.GetBaseGameSpriteNames())
            options.Add(new PickerOption
            {
                Value = "sprite|" + name,
                Label = "Base game • " + name,
                SearchText = "base game sprite " + name
            });

        string packFolder = selectedItem == null
            ? ""
            : Path.GetDirectoryName(selectedItem.SourceJsonPath) ?? "";
        if (Directory.Exists(packFolder))
            foreach (string path in Directory.GetFiles(
                         packFolder,
                         "*.*",
                         SearchOption.AllDirectories)
                     .Where(value => HasExtension(
                         value,
                         ".png",
                         ".jpg",
                         ".jpeg"))
                     .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                string relative = MakeRelativePath(packFolder, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
                options.Add(new PickerOption
                {
                    Value = "image|" + relative,
                    Label = "Pack image • " + relative,
                    SearchText = "custom pack image sprite " + relative
                });
            }

        foreach (string modelReference in
                 CustomItemApi.GetStoredGlbModelReferences())
            options.Add(new PickerOption
            {
                Value = "model|" + modelReference,
                Label = "Shared CIL GLB • " + modelReference,
                SearchText = "shared cil glb model 3d sprite " + modelReference
            });

        string current = !string.IsNullOrWhiteSpace(stage.image)
            ? "image|" + stage.image
            : !string.IsNullOrWhiteSpace(stage.model)
                ? "model|" + stage.model
                : "sprite|" + (stage.sprite ?? "");
        OpenPicker(
            "Choose Stage Artwork",
            current,
            options,
            value =>
            {
                if (value.StartsWith("image|", StringComparison.Ordinal))
                {
                    stage.image = value.Substring("image|".Length);
                    stage.sprite = null;
                    stage.model = null;
                }
                else if (value.StartsWith("model|", StringComparison.Ordinal))
                {
                    stage.model = value.Substring("model|".Length);
                    stage.sprite = null;
                    stage.image = null;
                    stage.rotation ??= new[] { 20f, 135f, 0f };
                }
                else if (value.StartsWith("sprite|", StringComparison.Ordinal))
                {
                    stage.sprite = value.Substring("sprite|".Length);
                    stage.image = null;
                    stage.model = null;
                    ApplyBaseGameSpriteSettings(stage);
                }
                stage.artAssetId = null; stage.artOriginalName = null;
                MarkDirty();
                ClearNumberInputs();
            },
            "Import Image / GLB…",
            () => BrowseArtwork(stage));
    }

    private void RefreshItems()
    {
        string previous = selectedItem?.QualifiedId ?? "";
        fittedText.Clear();
        items.Clear();
        items.AddRange(CustomItemApi.GetRegisteredItems()
            .OrderBy(value => value.PackId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase));
        growableIds.Clear();
        seedConflicts.Clear();
        foreach (CustomItemInfo item in items)
        {
            string? conflict = SeedCompatibility.FindConflict(item.Template);
            if (conflict != null) seedConflicts[item.QualifiedId] = conflict;
            try
            {
                if (!string.IsNullOrWhiteSpace(
                        CustomItemApi.GetItemExtensionJson(
                            item.QualifiedId,
                            ExtensionName)))
                    growableIds.Add(item.QualifiedId);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning(
                    $"Could not inspect growable data for " +
                    $"'{item.QualifiedId}': {exception.Message}");
            }
        }

        CustomItemInfo? restored = items.FirstOrDefault(value =>
            value.QualifiedId.Equals(previous, StringComparison.OrdinalIgnoreCase));
        selectedItem = restored ?? items.FirstOrDefault();
        LoadSelected();
        status = items.Count == 0
            ? "No enabled Custom Item Loader items are registered."
            : $"Loaded {items.Count} Custom Item Loader item" +
                (items.Count == 1 ? "." : "s.");
    }

    private void SelectItem(CustomItemInfo item)
    {
        if (dirty || selectedItem?.QualifiedId.Equals(
                item.QualifiedId,
                StringComparison.OrdinalIgnoreCase) == true)
            return;
        selectedItem = item;
        LoadSelected();
    }

    private void LoadSelected()
    {
        CancelOffsetDrag(); previewGeneration++; renderingGlbPreview = false; removeConfirmation = false; selectedStage = 0;
        ClearGlbPreview();
        definition = null;
        loadError = ""; loadedExtensionJson = null;
        dirty = false;
        ClearNumberInputs();
        detailsScroll = Vector2.zero;
        if (selectedItem == null)
            return;

        try
        {
            string? json = CustomItemApi.GetItemExtensionJson(
                selectedItem.QualifiedId,
                ExtensionName);
            loadedExtensionJson = json;
            if (!string.IsNullOrWhiteSpace(json))
            {
                definition = JsonConvert.DeserializeObject<GrowableDefinition>(json!);
                if (definition != null) EditorValidation.RequireEditable(definition);
                if (definition != null &&
                    Enum.TryParse(
                        definition.clock,
                        ignoreCase: true,
                        out GrowableClock parsed))
                    definition.Clock = parsed;
                if (definition?.stages != null)
                {
                    foreach (GrowableStageDefinition stage in definition.stages)
                        if (!string.IsNullOrWhiteSpace(stage.sprite))
                            ApplyBaseGameSpriteSettings(stage);
                    EnsureStageCacheIdentities();
                }
            }
            // Loading (also used by Discard) establishes a clean editing session.
            // Legacy cache-ID migration is preparation, not an unsaved user edit.
            dirty = false;
            status = "Loaded " + selectedItem.QualifiedId;
        }
        catch (Exception exception)
        {
            definition = null; dirty = false;
            loadError = "Could not load growable data: " + exception.Message;
            status = loadError;
            Plugin.Log.LogError(
                $"Growables editor could not load '{selectedItem.QualifiedId}': " +
                exception);
        }
    }

    private bool SaveSelected()
    {
        if (selectedItem == null)
            return false;
        try
        {
            if (!string.IsNullOrEmpty(loadError)) throw new InvalidDataException(loadError);
            if (invalidNumbers.Count != 0)
                throw new InvalidDataException("Correct invalid numeric input before saving: " + string.Join(", ", invalidNumbers));
            string? currentJson = CustomItemApi.GetItemExtensionJson(selectedItem.QualifiedId, ExtensionName);
            if (!EditorValidation.SameDocument(loadedExtensionJson, currentJson))
                throw new InvalidDataException("This growable was changed outside this editor. Discard/reload before saving to avoid overwriting those changes.");
            string? json = null;
            if (definition != null)
            {
                string? conflict = SeedCompatibility.FindConflict(selectedItem.Template);
                if (conflict != null)
                    throw new InvalidDataException(SeedCompatibility.Explain(conflict));
                NormalizeModelSources();
                EnsureStageCacheIdentities();
                string candidate = JsonConvert.SerializeObject(
                    definition,
                    Formatting.None,
                    new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore
                    });
                GrowableDefinition validation =
                    JsonConvert.DeserializeObject<GrowableDefinition>(candidate) ??
                    throw new InvalidDataException(
                        "The growable definition could not be validated.");
                validation.Validate(
                    selectedItem.SourceJsonPath,
                    selectedItem.QualifiedId);
                GrowableRegistry.ValidateReferences(validation);
                json = candidate;
            }

            CustomItemApi.SetItemExtensionJson(
                Plugin.PluginGuid,
                selectedItem.QualifiedId,
                ExtensionName,
                json);
            loadedExtensionJson = json;
            if (definition == null)
                growableIds.Remove(selectedItem.QualifiedId);
            else
                growableIds.Add(selectedItem.QualifiedId);
            dirty = false;
            status =
                "Saved through Custom Item Loader. Restart Silverpine to " +
                "apply registration changes.";
            return true;
        }
        catch (Exception exception)
        {
            status = "Save failed: " + exception.Message;
            Plugin.Log.LogError(
                $"Growables editor could not save '{selectedItem.QualifiedId}': " +
                exception);
            return false;
        }
    }

    private async void RenderGlbPreview(
        GrowableStageDefinition stage,
        int stageIndex)
    {
        if (selectedItem == null || string.IsNullOrWhiteSpace(stage.model))
        {
            status = "Choose or import a GLB model first.";
            return;
        }

        string selectedId = selectedItem.QualifiedId;
        int generation = ++previewGeneration;
        GrowableDefinition? selectedDefinition = definition;
        renderingGlbPreview = true;
        status = "Rendering one-direction GLB preview…";
        try
        {
            EnsureStageCacheIdentities();
            string folder = Path.GetFullPath(
                Path.GetDirectoryName(selectedItem.SourceJsonPath)!);
            NormalizeModelSource(stage, folder);
            string suffix = stage.CacheSuffix(stageIndex);
            Sprite rendered = await CustomItemApi.RenderAndCacheGlbSpriteAsync(
                folder,
                selectedItem.PackId,
                selectedItem.ItemId,
                stage.model!,
                stage.ModelRotation,
                stage.zoom,
                stage.resolution,
                "custom_growable_editor_preview",
                suffix);
            if (!open || selectedItem == null ||
                generation != previewGeneration || !ReferenceEquals(definition, selectedDefinition) ||
                definition?.stages?.Contains(stage) != true || stage.CacheSuffix(stageIndex) != suffix ||
                !selectedItem.QualifiedId.Equals(
                    selectedId,
                    StringComparison.OrdinalIgnoreCase))
            {
                DestroySpriteAndTexture(rendered);
                return;
            }

            ClearGlbPreview();
            glbPreview = rendered;
            previewOwnsTexture = true;
            glbPreviewStage = definition!.stages!.IndexOf(stage);
            previewSourceKey = StagePreviewKey(stage);
            status =
                "Rendered one-direction GLB preview and updated its CIL " +
                "distribution cache.";
        }
        catch (Exception exception)
        {
            if (open && generation == previewGeneration) status = "GLB preview failed: " + exception.Message;
            Plugin.Log.LogError(
                "Growables editor GLB preview failed: " + exception);
        }
        finally
        {
            if (generation == previewGeneration) renderingGlbPreview = false;
        }
    }

    private void LoadCachedGlbPreview(
        GrowableStageDefinition stage,
        int stageIndex)
    {
        if (selectedItem == null || string.IsNullOrWhiteSpace(stage.model))
        {
            status = "Choose or import a GLB model first.";
            return;
        }
        try
        {
            string folder = Path.GetFullPath(
                Path.GetDirectoryName(selectedItem.SourceJsonPath)!);
            if (!CustomItemApi.TryLoadCachedGlbSprite(
                    folder,
                    selectedItem.PackId,
                    selectedItem.ItemId,
                    stage.model!,
                    stage.ModelRotation,
                    stage.zoom,
                    stage.resolution,
                    "custom_growable_editor_cached_preview",
                    stage.CacheSuffix(stageIndex),
                    out Sprite cached))
            {
                status =
                    "No valid cached sprite exists for this model and its " +
                    "current render settings.";
                return;
            }
            ClearGlbPreview();
            glbPreview = cached;
            previewOwnsTexture = true;
            glbPreviewStage = stageIndex;
            previewSourceKey = StagePreviewKey(stage);
            status = "Loaded the CIL cached stage sprite.";
        }
        catch (Exception exception)
        {
            status = "Cached preview failed: " + exception.Message;
            Plugin.Log.LogError(
                "Growables editor cached GLB preview failed: " + exception);
        }
    }

    private void NormalizeModelSources()
    {
        if (selectedItem == null || definition?.stages == null)
            return;
        string folder = Path.GetFullPath(
            Path.GetDirectoryName(selectedItem.SourceJsonPath)!);
        foreach (GrowableStageDefinition stage in definition.stages)
            if (!string.IsNullOrWhiteSpace(stage.model))
                NormalizeModelSource(stage, folder, requireSource: false);
    }

    private void NormalizeModelSource(
        GrowableStageDefinition stage,
        string packFolder,
        bool requireSource = true)
    {
        if (string.IsNullOrWhiteSpace(stage.model) ||
            CustomItemApi.IsGlbModelSourceStored(stage.model!))
            return;
        string sourcePath = CustomItemApi.ResolveGlbModelSourcePath(
            packFolder,
            stage.model!,
            requireExists: requireSource);
        if (!File.Exists(sourcePath))
            return;
        string storedReference = CustomItemApi.ImportGlbModelSource(sourcePath);
        if (stage.model.Equals(
                storedReference,
                StringComparison.OrdinalIgnoreCase))
            return;
        int index = definition!.stages!.IndexOf(stage);
        string oldSuffix = stage.CacheSuffix(index);
        string newSuffix = StageArtIdentity.Suffix(stage.cacheId, index, storedReference, stage.rotation, stage.zoom, stage.resolution);
        StageCacheMigration.CopyGlbCache(packFolder, selectedItem!.PackId, selectedItem.ItemId, oldSuffix, newSuffix);
        stage.model = storedReference;
        MarkDirty();
    }


    private void DrawGrassTerrainTile(Rect rect)
    {
        Color old = GUI.color;
        GUI.color = new Color(0.04f, 0.055f, 0.045f, 0.9f);
        GUI.DrawTexture(
            new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f),
            Texture2D.whiteTexture);
        GUI.color = Color.white;

        Sprite? grass = GetGrassTerrainSprite();
        if (grass != null)
        {
            DrawSpriteTexture(grass, rect);
            GUI.color = old;
            return;
        }

        GUI.color = new Color(0.22f, 0.39f, 0.18f, 1f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = old;
    }

    private Sprite? GetGrassTerrainSprite()
    {
        if (grassTerrainSprite != null)
            return grassTerrainSprite;
        if (grassTerrainLookupAttempted)
            return null;

        grassTerrainLookupAttempted = true;
        foreach (SpriteRenderer renderer in
                 Resources.FindObjectsOfTypeAll<SpriteRenderer>())
        {
            if (renderer == null || renderer.sprite == null)
                continue;
            string objectName = renderer.gameObject.name;
            if (!objectName.Equals(
                    "prefab_tile_grass",
                    StringComparison.OrdinalIgnoreCase) &&
                !objectName.StartsWith(
                    "prefab_tile_grass (",
                    StringComparison.OrdinalIgnoreCase))
                continue;
            grassTerrainSprite = renderer.sprite;
            Plugin.Log.LogInfo(
                $"Growables editor is using terrain sprite " +
                $"'{grassTerrainSprite.name}' as its one-tile size reference.");
            return grassTerrainSprite;
        }

        foreach (Sprite candidate in Resources.FindObjectsOfTypeAll<Sprite>())
            if (candidate.name.Equals(
                    "grass",
                    StringComparison.OrdinalIgnoreCase))
            {
                grassTerrainSprite = candidate;
                return grassTerrainSprite;
            }

        Plugin.Log.LogWarning(
            "Growables editor could not locate prefab_tile_grass; using a " +
            "built-in one-tile grass reference.");
        return null;
    }

    private static void DrawSpriteTexture(Sprite sprite, Rect destination)
    {
        Rect source = sprite.textureRect;
        Texture texture = sprite.texture;
        Rect coordinates = new(
            source.x / texture.width,
            source.y / texture.height,
            source.width / texture.width,
            source.height / texture.height);
        GUI.DrawTextureWithTexCoords(
            destination,
            texture,
            coordinates,
            alphaBlend: true);
    }

    private void BrowseArtwork(GrowableStageDefinition stage)
    {
        if (selectedItem == null)
            return;
        if (invalidNumbers.Count != 0) { status = "Correct invalid numeric input before importing artwork."; return; }
        filePickerOpen = true;
        CustomItemInfo owner = selectedItem;
        GrowableDefinition? ownerDefinition = definition;
        try
        {
            FilePickerUI.GetFileSelection(
                path => HasExtension(
                    path,
                    ".png",
                    ".jpg",
                    ".jpeg",
                    ".glb"),
                selected =>
                {
                    filePickerOpen = false;
                    if (selected == "Exit")
                        return;
                    if (!open || !ReferenceEquals(selectedItem, owner) || !ReferenceEquals(definition, ownerDefinition) ||
                        definition?.stages?.Contains(stage) != true) return;
                    try
                    {
                        string folder = Path.GetDirectoryName(
                            selectedItem.SourceJsonPath)!;
                        if (HasExtension(selected, ".glb"))
                        {
                            stage.model =
                                CustomItemApi.ImportGlbModelSource(selected);
                            stage.image = null;
                            stage.sprite = null;
                            stage.rotation ??= new[] { 20f, 135f, 0f };
                            status =
                                "Imported shared CIL authoring model as " +
                                stage.model;
                        }
                        else
                        {
                            stage.image = SharedArtApi.ImportImage(selected, folder);
                            stage.model = null;
                            stage.sprite = null;
                            status = "Imported growable art as " + stage.image;
                        }
                        stage.artAssetId = null;
                        stage.artOriginalName = Path.GetFileNameWithoutExtension(selected);
                        ClearNumberInputs();
                        MarkDirty();
                    }
                    catch (Exception exception)
                    {
                        status = "Artwork import failed: " + exception.Message;
                        Plugin.Log.LogError(
                            "Growables editor artwork import failed: " + exception);
                    }
                });

            if (GenericListUI.Instance == null ||
                !GenericListUI.Instance.gameObject.activeInHierarchy)
                filePickerOpen = false;
        }
        catch
        {
            filePickerOpen = false;
            throw;
        }
    }

    private bool MatchesSearch(CustomItemInfo item)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;
        return item.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            item.QualifiedId.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private bool SelectedItemHasUseConflict() =>
        selectedItem != null && seedConflicts.ContainsKey(selectedItem.QualifiedId);

    private void DrawText(
        string label,
        string value,
        Action<string> assign,
        bool allowLineBreaks = false)
    {
        GUILayout.BeginVertical();
        GUILayout.Label(label, editorFieldLabel!);
        string next = DrawWrappedTextInput(value ?? "", allowLineBreaks);
        if (next != value)
            assign(next);
        GUILayout.EndVertical();
    }

    private void DrawToggle(
        string label,
        bool value,
        Action<bool> assign)
    {
        GUILayout.BeginHorizontal();
        DrawFieldLabel(label);
        bool next = GUILayout.Toggle(value, value ? "Enabled" : "Disabled");
        if (next != value)
            assign(next);
        GUILayout.EndHorizontal();
    }

    private void DrawInt(
        string label,
        string key,
        int value,
        Action<int> assign)
    {
        GUILayout.BeginHorizontal();
        DrawFieldLabel(label);
        string previous = NumberText(
            key,
            value.ToString(CultureInfo.InvariantCulture));
        string next = GUILayout.TextField(previous);
        numberText[key] = next;
        if (next != previous) MarkDirty();
        if (EditorValidation.TryInt(next, out int parsed))
        {
            invalidNumbers.Remove(key);
            if (parsed != value) assign(parsed);
        }
        else { invalidNumbers.Add(key); GUILayout.Label("Invalid number"); }
        GUILayout.EndHorizontal();
    }

    private void DrawFloat(
        string label,
        string key,
        float value,
        Action<float> assign)
    {
        GUILayout.BeginHorizontal();
        DrawFieldLabel(label);
        DrawFloatInline(key, value, assign);
        GUILayout.EndHorizontal();
    }

    private void DrawFloatInline(
        string key,
        float value,
        Action<float> assign)
    {
        string previous = NumberText(key, value.ToString("R", CultureInfo.InvariantCulture));
        string next = GUILayout.TextField(previous,
            GUILayout.MinWidth(60));
        numberText[key] = next;
        if (next != previous) MarkDirty();
        if (EditorValidation.TryFloat(next, out float parsed) &&
            (!key.StartsWith("offset.", StringComparison.Ordinal) || Math.Abs(parsed) <= 100))
        {
            invalidNumbers.Remove(key);
            if (parsed != value) assign(parsed);
        }
        else { invalidNumbers.Add(key); GUILayout.Label("Invalid number"); }
    }

    private void DrawChoice(
        string label,
        string current,
        IReadOnlyList<string> values,
        Action<string> assign)
    {
        GUILayout.BeginHorizontal();
        DrawFieldLabel(label);
        int index = IndexOf(values, current);
        if (DrawSingleLineChoice(values[index] + "  ▼"))
            OpenPicker(
                "Choose " + label,
                values[index],
                values.Select(value => new PickerOption
                {
                    Value = value,
                    Label = value,
                    SearchText = value
                }),
                assign);
        GUILayout.EndHorizontal();
    }

    private void DrawStageIndex(
        string label,
        int current,
        int maximum,
        Action<int> assign)
    {
        maximum = Math.Max(0, maximum);
        current = Mathf.Clamp(current, 0, maximum);
        GUILayout.BeginHorizontal();
        DrawFieldLabel(label);
        if (DrawSingleLineChoice(GetStagePickerLabel(current) + "  ▼"))
        {
            var options = new List<PickerOption>();
            for (int index = 0; index <= maximum; index++)
            {
                string value = index.ToString(CultureInfo.InvariantCulture);
                string stageLabel = GetStagePickerLabel(index);
                options.Add(new PickerOption
                {
                    Value = value,
                    Label = stageLabel,
                    SearchText = stageLabel
                });
            }
            OpenPicker(
                "Choose " + label,
                current.ToString(CultureInfo.InvariantCulture),
                options,
                value =>
                {
                    if (int.TryParse(
                            value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int parsed))
                        assign(parsed);
                });
        }
        GUILayout.EndHorizontal();
    }

    private string GetStagePickerLabel(int index)
    {
        string suffix = "";
        if (definition?.stages != null &&
            index >= 0 && index < definition.stages.Count)
        {
            GrowableStageDefinition stage = definition.stages[index];
            string art = !string.IsNullOrWhiteSpace(stage.artOriginalName) ? stage.artOriginalName! : !string.IsNullOrWhiteSpace(stage.sprite)
                ? stage.sprite!
                : !string.IsNullOrWhiteSpace(stage.image)
                    ? Path.GetFileName(stage.image)
                    : Path.GetFileName(stage.model ?? "GLB model");
            suffix = " — " + art;
            if (index == definition.stages.Count - 1)
                suffix += " (harvest-ready)";
        }
        return "Stage " + index + suffix;
    }

    private void OpenPicker(
        string title,
        string current,
        IEnumerable<PickerOption> options,
        Action<string> callback,
        string? actionLabel = null,
        Action? action = null)
    {
        if (invalidNumbers.Count != 0)
        { status = "Correct invalid numeric input before changing selection: " + string.Join(", ", invalidNumbers); return; }
        pickerOptions.Clear();
        pickerOptions.AddRange(options);
        pickerTitle = title;
        pickerCurrent = current ?? "";
        pickerSearch = "";
        pickerScroll = Vector2.zero;
        pickerCallback = callback;
        pickerActionLabel = actionLabel ?? "";
        pickerAction = action;
        pickerOpen = true;
        filteredPickerOptions = null; pickerPage = 0;
        GUIUtility.ExitGUI();
    }

    private void DrawPickerScreen()
    {
        GUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label(pickerTitle, GUILayout.Width(410));
        GUILayout.Label(pickerOptions.Count + " choices");
        if (!string.IsNullOrWhiteSpace(pickerActionLabel) &&
            DrawActionButton(pickerActionLabel, 190))
        {
            Action? action = pickerAction;
            ClosePicker();
            action?.Invoke();
            GUIUtility.ExitGUI();
        }
        if (DrawActionButton("Cancel", 110))
        {
            ClosePicker();
            GUIUtility.ExitGUI();
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label(
            "Search by name, ID, category, or asset name. Escape returns to " +
            "the Growables Editor without changing the value.");
        GUILayout.Label("Search", editorFieldLabel!);
        GUILayout.BeginHorizontal();
        pickerSearch = GUILayout.TextField(
            pickerSearch ?? "",
            GUILayout.MinHeight(44));
        if (DrawActionButton("Clear", 80))
            pickerSearch = "";
        GUILayout.EndHorizontal();

        if (filteredPickerOptions == null || filteredPickerSearch != pickerSearch)
        {
            filteredPickerOptions = pickerOptions.Where(PickerMatches).ToArray();
            filteredPickerSearch = pickerSearch; pickerPage = 0;
        }
        int pages = Math.Max(1, (filteredPickerOptions.Length + PickerPageSize - 1) / PickerPageSize);
        GUILayout.BeginHorizontal();
        if (DrawActionButton("Previous", 110)) { pickerPage = (pickerPage + pages - 1) % pages; pickerScroll = Vector2.zero; GUIUtility.ExitGUI(); }
        GUILayout.Label($"Page {pickerPage + 1} / {pages} · {filteredPickerOptions.Length} matches");
        if (DrawActionButton("Next", 110)) { pickerPage = (pickerPage + 1) % pages; pickerScroll = Vector2.zero; GUIUtility.ExitGUI(); }
        GUILayout.EndHorizontal();
        pickerScroll = GUILayout.BeginScrollView(pickerScroll, GUI.skin.box);
        int shown = 0;
        for (int i = pickerPage * PickerPageSize; i < Math.Min(filteredPickerOptions.Length, (pickerPage + 1) * PickerPageSize); i++)
        {
            PickerOption option = filteredPickerOptions[i];
            shown++;
            Color old = GUI.backgroundColor;
            if (option.Value.Equals(
                    pickerCurrent,
                    StringComparison.OrdinalIgnoreCase))
                GUI.backgroundColor = new Color(0.48f, 0.75f, 0.46f);
            if (GUILayout.Button(option.Label, GUILayout.MinHeight(42)))
            {
                Action<string>? callback = pickerCallback;
                string value = option.Value;
                ClosePicker();
                callback?.Invoke(value);
                GUI.backgroundColor = old;
                GUIUtility.ExitGUI();
            }
            GUI.backgroundColor = old;
        }
        if (shown == 0)
            GUILayout.Label("No matching choices.");
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private bool PickerMatches(PickerOption option)
    {
        if (string.IsNullOrWhiteSpace(pickerSearch))
            return true;
        return option.Label.Contains(
                pickerSearch,
                StringComparison.OrdinalIgnoreCase) ||
            option.Value.Contains(
                pickerSearch,
                StringComparison.OrdinalIgnoreCase) ||
            option.SearchText.Contains(
                pickerSearch,
                StringComparison.OrdinalIgnoreCase);
    }

    private void ClosePicker()
    {
        pickerOpen = false;
        pickerCallback = null;
        pickerAction = null;
        pickerOptions.Clear();
        filteredPickerOptions = null; filteredPickerSearch = null; pickerPage = 0;
        pickerTitle = "";
        pickerCurrent = "";
        pickerSearch = "";
        pickerActionLabel = "";
        pickerScroll = Vector2.zero;
    }

    private string NumberText(string key, string fallback)
    {
        if (!numberText.TryGetValue(key, out string value))
        {
            value = fallback;
            numberText.Add(key, value);
        }
        return value;
    }

    private void ClearNumberInputs() { numberText.Clear(); invalidNumbers.Clear(); }

    private void Set(ref string? field, string value)
    {
        if (field == value)
            return;
        field = value;
        MarkDirty();
    }

    private void Set(ref int field, int value)
    {
        if (field == value)
            return;
        field = value;
        MarkDirty();
    }

    private void Set(ref float field, float value)
    {
        if (Mathf.Approximately(field, value))
            return;
        field = value;
        MarkDirty();
    }

    private void Set(ref bool field, bool value)
    {
        if (field == value)
            return;
        field = value;
        MarkDirty();
    }

    private new void MarkDirty() => dirty = true;

    private void Close()
    {
        CancelOffsetDrag(); previewGeneration++; renderingGlbPreview = false;
        ClearLibraryThumbnails(); libraryOpen = false; closeConfirmation = false;
        ClearGlbPreview();
        filePickerOpen = false;
        ClosePicker();
        open = false;
        dirty = false;
        ClearNumberInputs();
        items.Clear();
        growableIds.Clear();
        selectedItem = null;
        definition = null;
        gameObject.SetActive(false);
        ReleaseSession();
    }

    private static GrowableStageDefinition NewDefaultStage(
        string sprite,
        float duration) => new()
    {
        cacheId = Guid.NewGuid().ToString("N"),
        sprite = sprite,
        duration = duration,
        pixelsPerUnit = 32f,
        pivotX = 0.5f,
        pivotY = 0.5f
    };

    private GrowableStageDefinition CopyStageArt(
        GrowableStageDefinition source,
        float duration)
    {
        var copy = new GrowableStageDefinition
        {
        cacheId = Guid.NewGuid().ToString("N"),
        anchorSprite = source.anchorSprite,
        offsetX = source.offsetX, offsetY = source.offsetY,
        artAssetId = source.artAssetId, artOriginalName = source.artOriginalName,
        sprite = source.sprite,
        image = source.image,
        model = source.model,
        rotation = source.rotation?.ToArray(),
        zoom = source.zoom,
        resolution = source.resolution,
        scale = source.scale,
        duration = duration,
        pixelsPerUnit = source.pixelsPerUnit,
        pivotX = source.pivotX,
        pivotY = source.pivotY,
        light = source.light?.Copy()
        };
        if (source.extensionData != null)
            copy.extensionData = source.extensionData.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone());
        if (!string.IsNullOrWhiteSpace(source.model) && selectedItem != null)
        {
            int index = definition!.stages!.IndexOf(source);
            StageCacheMigration.CopyGlbCache(Path.GetDirectoryName(selectedItem.SourceJsonPath)!, selectedItem.PackId,
                selectedItem.ItemId, source.CacheSuffix(index), copy.CacheSuffix(index));
        }
        return copy;
    }

    private static void ApplyBaseGameSpriteSettings(
        GrowableStageDefinition stage)
    {
        if (string.IsNullOrWhiteSpace(stage.sprite) ||
            !GrowableRegistry.TryGetBaseGameSpriteSettings(
                stage.sprite,
                out float pixelsPerUnit,
                out Vector2 pivot))
            return;
        stage.pixelsPerUnit = pixelsPerUnit;
        stage.pivotX = pivot.x;
        stage.pivotY = pivot.y;
    }

    private void ClearGlbPreview()
    {
        if (previewVisual != null) Destroy(previewVisual);
        previewVisual = null; previewGeometryKey = null; previewSourceKey = null;
        if (glbPreview != null)
        {
            if (previewOwnsTexture) DestroySpriteAndTexture(glbPreview);
        }
        glbPreview = null;
        glbPreviewStage = -1;
    }

    private static void DestroySpriteAndTexture(Sprite sprite)
    {
        Texture2D texture = sprite.texture;
        Destroy(sprite);
        if (texture != null)
            Destroy(texture);
    }

    private static int RemapMovedIndex(int value, int from, int to)
    {
        if (value < 0)
            return value;
        if (value == from)
            return to;
        if (from < to && value > from && value <= to)
            return value - 1;
        if (to < from && value >= to && value < from)
            return value + 1;
        return value;
    }

    private static int IndexOf(
        IReadOnlyList<string> values,
        string current)
    {
        for (int index = 0; index < values.Count; index++)
            if (values[index].Equals(
                    current,
                    StringComparison.OrdinalIgnoreCase))
                return index;
        return 0;
    }

    private static string MakeRelativePath(string folder, string path)
    {
        Uri root = new(
            Path.GetFullPath(folder).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar);
        Uri target = new(Path.GetFullPath(path));
        return Uri.UnescapeDataString(root.MakeRelativeUri(target).ToString());
    }

    private static bool HasExtension(
        string path,
        params string[] extensions) =>
        extensions.Any(extension =>
            path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
}
