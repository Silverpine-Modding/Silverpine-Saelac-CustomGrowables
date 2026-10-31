#nullable enable
using Silverpine.ModdingTools;
using SilverpineMods.CustomItemLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SilverpineMods.CustomGrowables;

internal sealed partial class GrowableEditorUI
{
    private int editorTab, selectedStage, previewGeneration, libraryPage;
    private bool closeConfirmation, removeConfirmation, libraryOpen, previewOwnsTexture;
    private string packFilter = "", previewMessage = "", librarySearch = "";
    private StagePreviewSource? previewSourceKey;
    private StagePreviewGeometry? previewGeometryKey;
    private Vector2 previewScroll, libraryScroll;
    private GUISkin? editorSkin;
    private GUIStyle? editorHeading, editorMuted, editorAction, editorFieldLabel;
    private const float SidebarWidth = EditorTextLayout.SidebarWidth;
    private const float PreviewPanelWidth = 460f;
    private readonly List<Texture2D> skinTextures = new();
    private Sprite? previewVisual;
    private GrowableStageDefinition? draggedStage, libraryStage;
    private Vector2 dragMouse, dragStart;
    private bool dragWasDirty;
    private float dragTileSize;
    private int offsetControl;
    private int lastEscapeFrame = -1;
    private readonly List<SharedArtInfo> libraryEntries = new();
    private SharedArtInfo[]? filteredLibraryEntries;
    private string? filteredLibrarySearch;
    private readonly Dictionary<string, Sprite> libraryThumbnails = new();
    private readonly HashSet<string> failedThumbnails = new();
    private readonly Dictionary<string, int> libraryViews = new();

    private void EnsureEditorSkin()
    {
        if (editorSkin != null) return;
        editorSkin = Instantiate(GUI.skin);
        Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, color); texture.Apply();
            skinTextures.Add(texture); return texture;
        }
        Texture2D panel = Solid(new Color(.098f, .129f, .145f));
        Texture2D input = Solid(new Color(.063f, .086f, .098f));
        Texture2D button = Solid(new Color(.153f, .196f, .22f));
        Texture2D selected = Solid(new Color(.188f, .251f, .224f));
        Texture2D hover = Solid(new Color(.204f, .259f, .286f));
        foreach (GUIStyle style in new[] { editorSkin.label, editorSkin.button, editorSkin.box,
                     editorSkin.textArea, editorSkin.textField, editorSkin.toggle })
        {
            style.fontSize = 20; style.wordWrap = true;
            style.fixedHeight = 0f; style.fixedWidth = 0f;
            foreach (GUIStyleState state in new[] { style.normal, style.hover, style.active, style.focused,
                         style.onNormal, style.onHover, style.onActive, style.onFocused })
                state.textColor = new Color(.90f, .92f, .90f);
        }
        editorSkin.box.normal.background = panel;
        editorSkin.box.border = new RectOffset();
        editorSkin.box.padding = new RectOffset(16, 16, 16, 16);
        editorSkin.label.padding = new RectOffset(2, 2, 6, 6);
        editorSkin.button.normal.background = button;
        editorSkin.button.hover.background = hover;
        editorSkin.button.active.background = selected;
        editorSkin.button.onNormal.background = selected;
        editorSkin.button.onHover.background = selected;
        editorSkin.button.onActive.background = selected;
        editorSkin.button.onFocused.background = selected;
        editorSkin.button.padding = new RectOffset(12, 12, 10, 10);
        editorSkin.button.border = new RectOffset();
        foreach (GUIStyle style in new[] { editorSkin.textArea, editorSkin.textField })
        {
            style.normal.background = input; style.focused.background = input;
            style.hover.background = input; style.active.background = input;
            style.padding = new RectOffset(10, 10, 9, 9); style.border = new RectOffset();
        }
        editorSkin.textField.wordWrap = false;
        editorAction = new GUIStyle(editorSkin.button) { wordWrap = false, fixedHeight = 0f };
        editorFieldLabel = new GUIStyle(editorSkin.label) { wordWrap = false };
        editorHeading = new GUIStyle(editorSkin.label) { fontSize = 27 };
        editorMuted = new GUIStyle(editorSkin.label) { fontSize = 18 };
        editorMuted.normal.textColor = new Color(.65f, .69f, .68f);
        EnsureTextStyles();
    }

    private bool DrawActionButton(string text, float minimumWidth = 0f) =>
        GUILayout.Button(text, editorAction!,
            GUILayout.Width(Mathf.Max(minimumWidth, editorAction!.CalcSize(new GUIContent(text)).x + 12f)),
            GUILayout.MinHeight(44f));

    private void DrawFieldLabel(string text) =>
        GUILayout.Label(text, editorFieldLabel!,
            GUILayout.Width(Mathf.Max(210f, editorFieldLabel!.CalcSize(new GUIContent(text)).x + 6f)));

    private void OpenPackPicker() => OpenPicker("Choose pack", packFilter,
        new[] { new PickerOption { Value = "", Label = "All packs" } }.Concat(items.Select(i => i.PackId)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id).Select(id => new PickerOption
            { Value = id, Label = id, SearchText = id })), value => packFilter = value);

    private void DrawSelectedStageChoice()
    {
        if (definition?.stages == null || definition.stages.Count == 0) return;
        selectedStage = Mathf.Clamp(selectedStage, 0, definition.stages.Count - 1);
        if (DrawSingleLineChoice(GetStagePickerLabel(selectedStage) + "  ▼"))
            OpenPicker("Preview / edit stage", selectedStage.ToString(),
                Enumerable.Range(0, definition.stages.Count).Select(i => new PickerOption
                { Value = i.ToString(), Label = GetStagePickerLabel(i), SearchText = GetStagePickerLabel(i) }),
                value => { CancelOffsetDrag(); selectedStage = int.Parse(value); ClearGlbPreview(); ClearNumberInputs(); });
    }

    private void OpenAnchorPicker(GrowableStageDefinition stage) => OpenPicker("Match native planted anchor",
        stage.anchorSprite ?? "", GrowableRegistry.GetBaseGameSpriteNames().Select(name => new PickerOption
        { Value = name, Label = name, SearchText = name }), value => { stage.anchorSprite = value; MarkDirty(); });

    private void RequestClose()
    {
        CancelOffsetDrag();
        if (dirty) closeConfirmation = true;
        else Close();
    }

    private bool HandleEscape()
    {
        if (!open) return false;
        // The host and the editor may see the same key in either Update order.
        if (lastEscapeFrame == Time.frameCount) return true;
        lastEscapeFrame = Time.frameCount;
        if (filePickerOpen) return true;
        if (draggedStage != null) CancelOffsetDrag();
        else if (closeConfirmation) closeConfirmation = false;
        else if (libraryOpen) { libraryOpen = false; ClearLibraryThumbnails(); }
        else if (pickerOpen) ClosePicker();
        else RequestClose();
        return true;
    }

    private void DrawCloseConfirmation()
    {
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("Unsaved growable changes", editorHeading!);
        GUILayout.Label("Save your changes before closing?");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save and close")) { if (SaveSelected()) { Close(); GUIUtility.ExitGUI(); } }
        if (GUILayout.Button("Discard and close")) { Close(); GUIUtility.ExitGUI(); }
        if (GUILayout.Button("Cancel")) { closeConfirmation = false; GUIUtility.ExitGUI(); }
        GUILayout.EndHorizontal();
        GUILayout.Label(status, editorMuted!);
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
    }

    // Migrate before any reordering. Files are copied, never renamed, so Discard preserves the saved pack.
    private bool TryPrepareStageEdit()
    {
        if (invalidNumbers.Count != 0) { status = "Correct invalid numeric input before changing stages."; return false; }
        try { EnsureStageCacheIdentities(); CancelOffsetDrag(); return true; }
        catch (Exception error) { status = "Cannot edit stages: " + error.Message; return false; }
    }

    private void EnsureStageCacheIdentities()
    {
        if (definition?.stages == null || selectedItem == null) return;
        string folder = Path.GetDirectoryName(selectedItem.SourceJsonPath)!;
        for (int i = 0; i < definition.stages.Count; i++)
        {
            GrowableStageDefinition stage = definition.stages[i];
            if (stage == null) throw new InvalidDataException("Stages cannot contain null entries.");
            if (!string.IsNullOrEmpty(stage.cacheId)) continue;
            string old = stage.CacheSuffix(i);
            string id = Guid.NewGuid().ToString("N");
            string next = StageArtIdentity.Suffix(id, i, stage.model, stage.rotation, stage.zoom, stage.resolution);
            if (!string.IsNullOrWhiteSpace(stage.model))
                StageCacheMigration.CopyGlbCache(folder, selectedItem.PackId, selectedItem.ItemId, old, next);
            stage.cacheId = id;
            MarkDirty();
        }
    }

    private StagePreviewSource StagePreviewKey(GrowableStageDefinition stage) => new(selectedItem?.QualifiedId, stage);

    private void EnsureStagePreview(GrowableStageDefinition stage)
    {
        StagePreviewSource key = StagePreviewKey(stage);
        if (previewSourceKey.HasValue && previewSourceKey.Value.Equals(key) && glbPreviewStage == selectedStage) return;
        ClearGlbPreview(); previewSourceKey = key; glbPreviewStage = selectedStage;
        previewMessage = "";
        try
        {
            if (!string.IsNullOrWhiteSpace(stage.sprite))
            {
                glbPreview = GrowableRegistry.ResolveBaseGameSprite(stage.sprite!);
                previewOwnsTexture = false;
            }
            else if (!string.IsNullOrWhiteSpace(stage.image))
            {
                string folder = Path.GetDirectoryName(selectedItem!.SourceJsonPath)!;
                string path = ResolvePreviewImage(folder, stage.image!);
                glbPreview = ReadImage(path); previewOwnsTexture = true;
            }
            else if (!string.IsNullOrWhiteSpace(stage.model))
            {
                if (CustomItemApi.TryLoadCachedGlbSprite(Path.GetDirectoryName(selectedItem!.SourceJsonPath)!,
                    selectedItem.PackId, selectedItem.ItemId, stage.model!, stage.ModelRotation, stage.zoom,
                    stage.resolution, "growable_stage_preview", stage.CacheSuffix(selectedStage), out Sprite cached))
                { glbPreview = cached; previewOwnsTexture = true; }
                else previewMessage = "No cached view for these settings. Render this stage's GLB preview.";
            }
            else previewMessage = "Choose stage artwork to preview.";
        }
        catch (Exception error) { previewMessage = "Preview unavailable: " + error.Message; }
    }

    private static string ResolvePreviewImage(string folder, string relative)
    {
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(folder, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Image leaves its pack folder.");
        return path;
    }

    private static Sprite ReadImage(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(path), false)) throw new InvalidDataException("Image cannot be decoded.");
            texture.filterMode = FilterMode.Point;
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 32);
        }
        catch { Destroy(texture); throw; }
    }

    private void DrawPersistentPreview()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(PreviewPanelWidth));
        previewScroll = GUILayout.BeginScrollView(previewScroll);
        GUILayout.Label("Stage preview", editorHeading!);
        if (definition?.stages == null || definition.stages.Count == 0 || selectedItem == null)
            GUILayout.Label("Attach growable behavior to preview its stages.", editorMuted!);
        else
        {
            DrawSelectedStageChoice();
            GrowableStageDefinition stage = definition.stages[selectedStage];
            EnsureStagePreview(stage);
            if (glbPreview != null)
            {
                var geometry = new StagePreviewGeometry(definition.scale, stage);
                try
                {
                    if (!StageArtIdentity.Finite(definition.scale) || definition.scale <= 0 ||
                        !StageArtIdentity.Finite(stage.scale) || stage.scale <= 0 ||
                        !StageArtIdentity.Finite(stage.pixelsPerUnit) || stage.pixelsPerUnit <= 0 ||
                        !StageArtIdentity.Finite(stage.pivotX) || !StageArtIdentity.Finite(stage.pivotY))
                        throw new InvalidDataException("Enter finite, positive scale and PPU values, and finite pivots.");
                    StageArtIdentity.ValidateOffset(stage.offsetX, stage.offsetY);
                    if (previewVisual == null || !previewGeometryKey.HasValue || !previewGeometryKey.Value.Equals(geometry))
                    {
                        if (previewVisual != null) Destroy(previewVisual);
                        previewVisual = GrowableRegistry.CreateStageVisual(glbPreview, stage, definition.scale);
                        previewGeometryKey = geometry;
                    }
                    DrawWorldPlacement(previewVisual, stage);
                    GUILayout.Label($"Terrain: 1 × 1 tile\nPlant: {previewVisual.rect.width / previewVisual.pixelsPerUnit * definition.scale * stage.scale:0.###} × " +
                        $"{previewVisual.rect.height / previewVisual.pixelsPerUnit * definition.scale * stage.scale:0.###} tiles", editorMuted!);
                }
                catch (Exception error) { GUILayout.Label(error.Message); }
            }
            else GUILayout.Label(previewMessage, GUI.skin.box);
            GUILayout.Label("Placement offset · tiles", editorHeading!);
            GUILayout.BeginHorizontal();
            GUILayout.Label("X", editorFieldLabel!, GUILayout.Width(28));
            DrawFloatInline("offset.x", stage.offsetX, value => SetOffset(stage, value, stage.offsetY, false));
            GUILayout.Space(14);
            GUILayout.Label("Y", editorFieldLabel!, GUILayout.Width(28));
            DrawFloatInline("offset.y", stage.offsetY, value => SetOffset(stage, stage.offsetX, value, false));
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Reset stage offset")) { CancelOffsetDrag(); SetOffset(stage, 0, 0); }
            GUILayout.Label("Drag to position · X right / Y up\nArrows: 0.01 tile · Shift: 0.1 · Esc: cancel", editorMuted!);
            if (GUILayout.Button("Refresh preview")) { CancelOffsetDrag(); ClearGlbPreview(); }
            if (!string.IsNullOrWhiteSpace(stage.model))
            {
                GUI.enabled = !renderingGlbPreview;
                if (GUILayout.Button(renderingGlbPreview ? "Rendering…" : "Render one-direction GLB")) RenderGlbPreview(stage, selectedStage);
                GUI.enabled = true;
            }
            if (GUILayout.Button("Shared art library…")) OpenArtLibrary(stage);
            GUI.enabled = glbPreview != null && string.IsNullOrWhiteSpace(stage.sprite);
            if (GUILayout.Button("Add stage art to library")) PublishStageArt(stage);
            GUI.enabled = true;
        }
        GUILayout.EndScrollView(); GUILayout.EndVertical();
    }

    private void DrawWorldPlacement(Sprite sprite, GrowableStageDefinition stage)
    {
        Rect rect = GUILayoutUtility.GetRect(260, 340, 340, 340, GUILayout.ExpandWidth(true));
        float scale = definition!.scale * stage.scale;
        float width = sprite.rect.width / sprite.pixelsPerUnit * scale;
        float height = sprite.rect.height / sprite.pixelsPerUnit * scale;
        float tile = TerrainTileScreenSize * Mathf.Min(1, rect.width * .86f / (Mathf.Max(1, width) * TerrainTileScreenSize),
            rect.height * .72f / (Mathf.Max(1, height) * TerrainTileScreenSize));
        Vector2 origin = new(rect.center.x, rect.center.y + rect.height * .12f);
        Rect art = new(origin.x - sprite.pivot.x / sprite.pixelsPerUnit * scale * tile,
            origin.y - (sprite.rect.height - sprite.pivot.y) / sprite.pixelsPerUnit * scale * tile, width * tile, height * tile);
        GUI.BeginGroup(rect);
        DrawGrassTerrainTile(new Rect(origin.x - rect.x - tile / 2, origin.y - rect.y - tile / 2, tile, tile));
        DrawSpriteTexture(sprite, new Rect(art.x - rect.x, art.y - rect.y, art.width, art.height));
        Color color = GUI.color; GUI.color = new Color(1, .3f, .3f);
        GUI.DrawTexture(new Rect(origin.x - rect.x - 5, origin.y - rect.y - 5, 10, 10), Texture2D.whiteTexture);
        GUI.color = color; GUI.EndGroup();
        int control = GUIUtility.GetControlID("growable-offset".GetHashCode(), FocusType.Keyboard, rect);
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            GUIUtility.keyboardControl = control;
            if (art.Contains(e.mousePosition))
            {
                draggedStage = stage; dragStart = new Vector2(stage.offsetX, stage.offsetY); dragMouse = e.mousePosition;
                dragTileSize = tile; dragWasDirty = dirty; offsetControl = control; GUIUtility.hotControl = control;
            }
            e.Use();
        }
        if (draggedStage != null && GUIUtility.hotControl == control)
        {
            if (e.type == EventType.MouseDrag)
            {
                Vector2 delta = (e.mousePosition - dragMouse) / dragTileSize;
                SetOffset(draggedStage, dragStart.x + delta.x, dragStart.y - delta.y); e.Use();
            }
            if (e.type == EventType.MouseUp && e.button == 0) { draggedStage = null; GUIUtility.hotControl = 0; e.Use(); }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { CancelOffsetDrag(); e.Use(); }
        }
        if (GUIUtility.keyboardControl == control && e.type == EventType.KeyDown)
        {
            Vector2 delta = e.keyCode switch { KeyCode.LeftArrow => Vector2.left, KeyCode.RightArrow => Vector2.right,
                KeyCode.UpArrow => Vector2.up, KeyCode.DownArrow => Vector2.down, _ => Vector2.zero };
            float step = e.shift ? .1f : .01f;
            if (delta != Vector2.zero) { SetOffset(stage, stage.offsetX + delta.x * step, stage.offsetY + delta.y * step); e.Use(); }
        }
    }

    private void SetOffset(GrowableStageDefinition stage, float x, float y, bool clearNumbers = true)
    {
        if (!StageArtIdentity.Finite(x) || !StageArtIdentity.Finite(y)) { status = "Offsets must be finite."; return; }
        stage.offsetX = (float)Math.Round(Mathf.Clamp(x, -100, 100), 3);
        stage.offsetY = (float)Math.Round(Mathf.Clamp(y, -100, 100), 3);
        if (clearNumbers)
        {
            numberText.Remove("offset.x"); numberText.Remove("offset.y");
            invalidNumbers.Remove("offset.x"); invalidNumbers.Remove("offset.y");
        }
        MarkDirty();
    }

    private void CancelOffsetDrag()
    {
        if (draggedStage == null) return;
        SetOffset(draggedStage, dragStart.x, dragStart.y); dirty = dragWasDirty; draggedStage = null;
        if (GUIUtility.hotControl == offsetControl) GUIUtility.hotControl = 0;
    }

    protected override void OnFrameworkSessionClosed()
    {
        open = false;
        base.OnFrameworkSessionClosed();
    }

    private void OpenArtLibrary(GrowableStageDefinition stage)
    {
        if (invalidNumbers.Count != 0) { status = "Correct invalid numeric input before changing artwork."; return; }
        CancelOffsetDrag(); libraryStage = stage; libraryOpen = true; libraryPage = 0; ReloadArtLibrary();
        GUIUtility.ExitGUI();
    }
    private void ReloadArtLibrary()
    {
        ClearLibraryThumbnails(); libraryEntries.Clear(); filteredLibraryEntries = null;
        try { libraryEntries.AddRange(SharedArtApi.GetEntries()); }
        catch (Exception error) { status = "Cannot read shared art: " + error.Message; }
    }
    private void DrawArtLibrary()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.BeginHorizontal(); GUILayout.Label("Shared asset library", editorHeading!);
        if (DrawActionButton("Back to editor", 210)) { libraryOpen = false; ClearLibraryThumbnails(); GUIUtility.ExitGUI(); }
        GUILayout.EndHorizontal();
        GUILayout.Label("BepInEx/shared/ArtLibrary · same library as Item Loader · choose one view for this stage", editorMuted!);
        string searchText = GUILayout.TextField(librarySearch);
        if (searchText != librarySearch) { librarySearch = searchText; libraryPage = 0; ClearLibraryThumbnails(); }
        if (DrawActionButton("Refresh library", 230)) ReloadArtLibrary();
        if (filteredLibraryEntries == null || filteredLibrarySearch != librarySearch)
        {
            filteredLibraryEntries = libraryEntries.Where(e => (e.OriginalName + " " + e.SourceFileName).IndexOf(librarySearch,
                StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            filteredLibrarySearch = librarySearch;
        }
        var entries = filteredLibraryEntries;
        int pages = Math.Max(1, (entries.Length + 7) / 8); libraryPage = Mathf.Clamp(libraryPage, 0, pages - 1);
        libraryScroll = GUILayout.BeginScrollView(libraryScroll);
        if (entries.Length == 0) GUILayout.Label("No cached art found. Add stage art here, or index existing art in Item Loader.");
        for (int row = libraryPage * 8; row < Math.Min(entries.Length, libraryPage * 8 + 8); row += 2)
        {
            GUILayout.BeginHorizontal();
            for (int i = row; i < Math.Min(entries.Length, row + 2); i++) DrawArtCard(entries[i]);
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView(); GUILayout.BeginHorizontal();
        if (DrawActionButton("◀", 55)) { libraryPage = (libraryPage + pages - 1) % pages; ClearLibraryThumbnails(); }
        GUILayout.Label($"Page {libraryPage + 1} / {pages} · {entries.Length} assets");
        if (DrawActionButton("▶", 55)) { libraryPage = (libraryPage + 1) % pages; ClearLibraryThumbnails(); }
        GUILayout.EndHorizontal(); GUILayout.Label(status, editorMuted!); GUILayout.EndVertical();
    }
    private void DrawArtCard(SharedArtInfo entry)
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width((DesignWidth - 100) / 2));
        GUILayout.Label(entry.OriginalName, editorHeading!);
        GUILayout.Label(entry.SourceFileName, editorMuted!);
        libraryViews.TryGetValue(entry.Id, out int view);
        if (entry.ViewCount == 4)
        {
            int next = GUILayout.Toolbar(view, new[] { "Front", "Right", "Back", "Left" });
            if (next != view) { libraryViews[entry.Id] = next; view = next; ClearLibraryThumbnails(); }
        }
        Rect rect = GUILayoutUtility.GetRect(180, 180, GUILayout.ExpandWidth(true));
        string key = entry.Id + ":" + view;
        try
        {
            if (failedThumbnails.Contains(key)) GUI.Label(rect, "Preview unavailable. Refresh after repairing the image.");
            else
            {
                if (!libraryThumbnails.TryGetValue(key, out Sprite image))
                    libraryThumbnails[key] = image = ReadImage(SharedArtApi.GetViewPath(entry.Id, view));
                float ratio = Mathf.Min(rect.width / image.rect.width, rect.height / image.rect.height);
                DrawSpriteTexture(image, new Rect(rect.center.x - image.rect.width * ratio / 2,
                    rect.center.y - image.rect.height * ratio / 2, image.rect.width * ratio, image.rect.height * ratio));
            }
        }
        catch (Exception error) { failedThumbnails.Add(key); GUI.Label(rect, error.Message); }
        GUI.enabled = !failedThumbnails.Contains(key) && libraryStage != null;
        if (GUILayout.Button("Use this view for stage")) { UseLibraryEntry(entry, view); GUIUtility.ExitGUI(); }
        GUI.enabled = true; GUILayout.EndVertical();
    }
    private void UseLibraryEntry(SharedArtInfo entry, int view)
    {
        if (selectedItem == null || libraryStage == null || definition?.stages?.Contains(libraryStage) != true) return;
        try
        {
            libraryStage.image = SharedArtApi.CopyViewToPack(entry.Id, Path.GetDirectoryName(selectedItem.SourceJsonPath)!, view);
            libraryStage.sprite = null; libraryStage.model = null;
            libraryStage.artAssetId = entry.Id; libraryStage.artOriginalName = entry.OriginalName;
            MarkDirty(); ClearGlbPreview(); ClearNumberInputs(); libraryOpen = false; ClearLibraryThumbnails(); editorTab = 1;
            status = "Cached art copied into this pack. No source model or shared library is needed by recipients.";
        }
        catch (Exception error) { status = "Cannot use cached art: " + error.Message; }
    }
    private void PublishStageArt(GrowableStageDefinition stage)
    {
        if (glbPreview == null || !string.IsNullOrWhiteSpace(stage.sprite)) return;
        try
        {
            string source = stage.model ?? stage.image ?? "stage.png";
            string original = stage.artOriginalName ?? (!string.IsNullOrWhiteSpace(stage.model)
                ? SharedArtLibrary.OriginalModelName(CustomItemApi.ResolveGlbModelSourcePath(
                    Path.GetDirectoryName(selectedItem!.SourceJsonPath)!, stage.model!, false))
                : Path.GetFileNameWithoutExtension(source));
            SharedArtInfo entry = SharedArtApi.Publish(original,
                Path.GetFileName(source), glbPreview.texture.EncodeToPNG());
            stage.artAssetId = entry.Id; stage.artOriginalName = entry.OriginalName; MarkDirty();
            status = "Stage art added to the same shared library used by Item Loader.";
        }
        catch (Exception error) { status = "Cannot publish stage art: " + error.Message; }
    }
    private void ClearLibraryThumbnails()
    {
        foreach (Sprite sprite in libraryThumbnails.Values) if (sprite != null) DestroySpriteAndTexture(sprite);
        libraryThumbnails.Clear(); failedThumbnails.Clear();
    }

    protected override void OnDisable()
    {
        open = false; filePickerOpen = false; libraryOpen = false; libraryStage = null;
        ClosePicker();
        CancelOffsetDrag(); previewGeneration++; renderingGlbPreview = false;
        ClearGlbPreview(); ClearLibraryThumbnails();
        fittedText.Clear();
        foreach (Texture2D texture in skinTextures) if (texture != null) Destroy(texture);
        skinTextures.Clear(); if (editorSkin != null) Destroy(editorSkin); editorSkin = null;
        base.OnDisable();
    }
}
