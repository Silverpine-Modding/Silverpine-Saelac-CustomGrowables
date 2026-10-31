using Newtonsoft.Json;
using Silverpine.ModdingTools;
using SilverpineMods.CustomGrowables;
using SilverpineMods.CustomItemLoader;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message); checks++;
}
void Reject(Action action, string message)
{
    bool rejected = false;
    try { action(); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, message);
}

float MeasureEditorText(string value) => System.Globalization.StringInfo.ParseCombiningCharacters(value).Length * 10f;
Check(EditorTextLayout.FitSingleLine("Search", 60, MeasureEditorText) == "Search", "single-line text is preserved when it exactly fits");
Check(EditorTextLayout.FitSingleLine("Skeleton.Pack:mod_skelebones", 160, MeasureEditorText) == "Skeleton.Pack:m…", "long item IDs use ellipsis instead of wrapping inside a card");
Check(EditorTextLayout.FitSingleLine("A\r\nB\nC\tD", 200, MeasureEditorText) == "A B C D", "embedded line breaks cannot create extra sidebar rows");
Check(EditorTextLayout.FitSingleLine("Long title", 9, MeasureEditorText) == "", "too-small text areas cannot overflow even with an ellipsis");
Check(EditorTextLayout.FitSingleLine("🌱e\u0301XYZ", 30, MeasureEditorText) == "🌱e\u0301…", "ellipsis preserves Unicode surrogate pairs and combining characters");
foreach (int width in new[] { 160, 240, 320, 380 })
{
    string fitted = EditorTextLayout.FitSingleLine(new string('W', 100), width, MeasureEditorText);
    Check(MeasureEditorText(fitted) <= width && !fitted.Contains('\n'), "long item names remain within a single line at width " + width);
}
Check(EditorTextLayout.SidebarWidth >= 420 && EditorTextLayout.ItemRowHeight >= 76, "sidebar reserves room for two separate padded text lines");

var seedTemplate = new Item();
Check(SeedCompatibility.FindConflict(seedTemplate) == null, "component-free items can become seeds");
seedTemplate.Components.Add(new PassiveSeedTestComponent());
seedTemplate.Components.Add(new ItemComponent_CustomGrowableSeed());
Check(SeedCompatibility.FindConflict(seedTemplate) == null, "passive and existing growable components remain compatible");
seedTemplate.Components.Add(new ActiveSeedTestComponent());
Check(SeedCompatibility.FindConflict(seedTemplate) == nameof(ActiveSeedTestComponent), "reject competing use behavior");
seedTemplate.Components.Clear(); seedTemplate.Components.Add(new InheritedSeedTestComponent());
Check(SeedCompatibility.FindConflict(seedTemplate) == nameof(InheritedSeedTestComponent), "reject inherited competing use behavior");
seedTemplate.Components.Clear(); seedTemplate.Components.Add(new ItemComponent_Sticky());
Check(SeedCompatibility.FindConflict(seedTemplate) == nameof(ItemComponent_Sticky), "reject sticky items which native Use always blocks");

var first = new GrowableStageDefinition { cacheId = Guid.NewGuid().ToString("N"), model = "apple.glb", duration = 1 };
var second = new GrowableStageDefinition { cacheId = Guid.NewGuid().ToString("N"), model = "pear.glb", duration = 1 };
string identity = first.CacheSuffix(0);
Check(identity == first.CacheSuffix(3), "moving a stage does not change its cache identity");
Check(first.CacheSuffix(0) != second.CacheSuffix(0), "distinct stages cannot alias a cache slot");
var stages = new List<GrowableStageDefinition> { first, second };
stages.Reverse();
Check(stages[1].CacheSuffix(1) == identity, "reordered cache-only stage still references its own PNG");
stages.RemoveAt(0);
Check(stages[0].CacheSuffix(0) == identity, "removing a preceding stage preserves cached art");
first.zoom = 2;
Check(first.CacheSuffix(0) != identity, "unsaved render settings cannot overwrite the saved stage cache");
first.zoom = 1;
Check(first.CacheSuffix(0) == identity, "discarding render settings restores original cache identity");
first.rotation = new[] { 20f, 135f, 0f };
Check(first.CacheSuffix(0) == identity, "explicit default rotation matches implicit default rotation");
first.model = "changed.glb";
Check(first.CacheSuffix(0) != identity, "changing source selects a separate cache");
var legacy = new GrowableStageDefinition();
Check(legacy.CacheSuffix(7) == "__growable_stage_7", "old packs keep their legacy cache lookup before migration");
Check(StageArtIdentity.ShiftPivot(32, 64, 32, 1, 0) == .5f, "explicit center remains centered regardless of preceding stage");
Check(StageArtIdentity.ShiftPivot(16, 46, 32, 1, 0) == 16f / 46, "Juniper native anchor preserves its planted point");
foreach (float scale in new[] { .1f, 1f, 10f })
{
    float shifted = StageArtIdentity.ShiftPivot(16, 64, 32, scale, .25f);
    Check(Math.Abs((16f - shifted * 64) / 32 * scale - .25f) < .00001f, "offset remains .25 tile at scale " + scale);
}
Reject(() => StageArtIdentity.ValidateOffset(float.NaN, 0), "reject NaN offsets");
Reject(() => StageArtIdentity.ValidateOffset(0, float.PositiveInfinity), "reject infinite offsets");
Reject(() => StageArtIdentity.ValidateOffset(101, 0), "reject out-of-range offsets");

GrowableDefinition Definition() => new()
{
    stages = new() { new() { sprite = "seed", duration = 1 }, new() { sprite = "tree" } },
    harvest = new() { new() { item = "Apple", minimum = 5, maximum = 5 } }
};
string folder = Path.Combine(AppContext.BaseDirectory, "GrowableRegression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    string file = Path.Combine(folder, "pack.json");
    var definition = Definition(); definition.Validate(file, "pack:seed");
    Check(definition.harvest![0].minimum == 5 && definition.IsHarvestReady(1), "five-apple harvest definition remains valid");
    definition = Definition(); definition.clock = "999";
    Reject(() => definition.Validate(file, "pack:seed"), "reject undefined numeric growth clocks");
    definition = Definition(); definition.stages![0].pivotX = float.NaN;
    Reject(() => definition.Validate(file, "pack:seed"), "reject non-finite pivots");
    definition = Definition(); definition.stages![0].cacheId = definition.stages[1].cacheId = Guid.NewGuid().ToString("N");
    Reject(() => definition.Validate(file, "pack:seed"), "reject duplicate stage IDs");
    definition = Definition(); definition.stages!.Insert(1, new() { sprite = "mature", duration = 1 });
    definition.perennial = true; definition.matureStage = 1; definition.regrowStage = 1;
    definition.Validate(file, "pack:seed");
    Check(definition.HasReachedMaturityStage(1) && !definition.IsHarvestReady(1), "perennial regrowth remains mature but not harvest-ready");
    definition.stages[1].offsetX = .125f;
    var restored = JsonConvert.DeserializeObject<GrowableDefinition>(JsonConvert.SerializeObject(definition))!;
    Check(restored.stages![1].offsetX == .125f, "per-stage offset survives JSON round trip");

    Check(GrowthSeasons.Resolve(null, false) == 7, "legacy default grows in spring, summer, and autumn");
    Check(GrowthSeasons.Resolve(null, true) == 15, "legacy winter-enabled packs grow year-round");
    for (int mask = 1; mask <= GrowthSeasons.All; mask++)
    {
        definition = Definition(); definition.growthSeasons = GrowthSeasons.NamesFor(mask);
        definition.Validate(file, "pack:seed");
        Check(definition.GrowthSeasonMask == mask && Enumerable.Range(0, 4).All(season =>
            GrowthSeasons.Allows(mask, season) == ((mask & (1 << season)) != 0)),
            "season combination " + mask + " has exactly the selected seasons");
    }
    definition = Definition(); definition.growthSeasons = new[] { " winter ", "SPRING", "Winter" };
    definition.Validate(file, "pack:seed");
    Check(definition.growthSeasons.SequenceEqual(new[] { "Spring", "Winter" }), "season names normalize case, spaces, duplicates, and order");
    Check(definition.GrowthSeasonMask == 9 && !definition.growsInWinter, "explicit seasons override the legacy winter flag");
    restored = JsonConvert.DeserializeObject<GrowableDefinition>(JsonConvert.SerializeObject(definition))!;
    restored.Validate(file, "pack:seed");
    Check(restored.GrowthSeasonMask == 9, "seasons persist through pack JSON save/load");
    foreach (var invalid in new[] { Array.Empty<string>(), new[] { "Fall" }, new[] { "0" }, new[] { "Spring", "typo" }, new string[] { null! } })
    {
        definition = Definition(); definition.growthSeasons = invalid;
        Reject(() => definition.Validate(file, "pack:seed"), "reject empty, unknown, numeric, or null season selection: " + string.Join(",", invalid));
    }
    Check(!GrowthSeasons.Allows(15, -1) && !GrowthSeasons.Allows(15, 4), "invalid native season cannot alias a selected season");
    Check(GrowthSeasons.Resolve(Array.Empty<string>(), false, allowEmpty: true) == 0, "editor can display an empty selection until validation");

    definition = Definition(); definition.stages![0].duration = 168;
    definition.Validate(file, "pack:seed");
    int stageIndex = 0; double progress = 524288; bool matured = false;
    GrowthProgress.Advance(definition, ref stageIndex, ref progress, ref matured, .02);
    Check(progress > 524288, "week-long real-time stages keep advancing past the float-precision stall");
    Check(!GrowthProgress.Advance(definition, ref stageIndex, ref progress, ref matured, double.NaN) &&
        !GrowthProgress.Advance(definition, ref stageIndex, ref progress, ref matured, double.PositiveInfinity), "invalid timer increments cannot poison growth");
    definition.stages[0].duration = float.MaxValue;
    Check(double.IsFinite(definition.GetStageDuration(0)), "hours-to-seconds conversion cannot overflow a valid stage duration");

    definition = Definition(); definition.clock = "GameTurns";
    definition.stages![0].duration = 2;
    definition.stages.Insert(1, new() { sprite = "bare-tree", duration = 3 });
    definition.perennial = true; definition.matureStage = 1; definition.regrowStage = 1;
    definition.growthSeasons = new[] { "Summer" }; definition.Validate(file, "pack:seed");
    stageIndex = 0; progress = 0; matured = false;
    GrowthProgress.Advance(definition, ref stageIndex, ref progress, ref matured, 3);
    Check(stageIndex == 1 && progress == 1 && matured, "growth carries excess progress across stages and records maturity");
    for (int season = 0; season < 4; season++)
    {
        stageIndex = 1; progress = 1; matured = true;
        if (GrowthSeasons.Allows(definition.GrowthSeasonMask, season))
            GrowthProgress.Advance(definition, ref stageIndex, ref progress, ref matured, 2);
        Check(matured && (season == 1 ? stageIndex == 2 && progress == 0 : stageIndex == 1 && progress == 1),
            "perennial summer regrowth preserves maturity and pauses correctly in season " + season);
    }
    stageIndex = 0; progress = 0; matured = false;
    GrowthProgress.Advance(definition, ref stageIndex, ref progress, ref matured, 1e12);
    Check(stageIndex == 2 && progress == 0 && matured, "large offline interval stops at the final harvest-ready stage");
    Check(!GrowthProgress.Advance(definition, ref stageIndex, ref progress, ref matured, 1) && progress == 0,
        "harvest-ready plants cannot grow or accumulate another harvest automatically");

    var previewStage = new GrowableStageDefinition { model = "apple.glb" };
    var sourceSnapshot = new StagePreviewSource("pack:seed", previewStage);
    previewStage.rotation = new[] { 20f, 135f, 0f };
    Check(sourceSnapshot.Equals(new StagePreviewSource("pack:seed", previewStage)), "preview snapshot matches implicit and explicit default rotation");
    previewStage.rotation[1] = 45;
    Check(!sourceSnapshot.Equals(new StagePreviewSource("pack:seed", previewStage)), "in-place rotation changes invalidate the preview");
    var geometrySnapshot = new StagePreviewGeometry(1, previewStage);
    previewStage.offsetY = .1f;
    Check(!geometrySnapshot.Equals(new StagePreviewGeometry(1, previewStage)), "offset edits invalidate geometry without rerendering source art");
    geometrySnapshot = new StagePreviewGeometry(1, previewStage);
    Check(!geometrySnapshot.Equals(new StagePreviewGeometry(2, previewStage)), "base scale edits invalidate preview placement");
    sourceSnapshot = new StagePreviewSource("pack:seed", previewStage);
    for (int i = 0; i < 10000; i++) sourceSnapshot.Equals(new StagePreviewSource("pack:seed", previewStage));
    long before = GC.GetAllocatedBytesForCurrentThread(); bool equal = true;
    for (int i = 0; i < 100000; i++)
        equal &= sourceSnapshot.Equals(new StagePreviewSource("pack:seed", previewStage)) &&
            geometrySnapshot.Equals(new StagePreviewGeometry(1, previewStage)) && GrowthSeasons.Allows(15, i % 4);
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(equal && allocated == 0, "100,000 cached preview/season comparisons allocate zero managed bytes");
    RuntimeChecks.Run(Check, file);
    foreach (string invalid in new[] { "", "-", "abc", "NaN", "Infinity", "1e100" })
        Check(!EditorValidation.TryFloat(invalid, out _), "numeric editor rejects incomplete/non-finite text: " + invalid);
    Check(EditorValidation.TryFloat("0.00001", out float tiny) && tiny == .00001f &&
        EditorValidation.TryFloat(tiny.ToString("R", System.Globalization.CultureInfo.InvariantCulture), out float precise) && precise == tiny,
        "displaying small numeric values cannot round them to zero and dirty the document");
    Check(!EditorValidation.TryInt("1.5", out _) && !EditorValidation.TryInt("999999999999", out _), "integer controls reject fractions and overflow");
    Check(EditorValidation.SameDocument("{\"scale\":1}", "{ \"scale\": 1 }"), "external-edit guard ignores JSON formatting changes");
    Check(!EditorValidation.SameDocument("{\"scale\":1}", "{\"scale\":2}"), "external-edit guard detects conflicting saved changes");
    Check(EditorValidation.SameDocument(null, "null"), "absent and null growable extensions are equivalent");
    definition = Definition(); definition.stages!.Add(null!);
    Reject(() => EditorValidation.RequireEditable(definition), "malformed stages fail safely before the editor can dereference them");
    definition = Definition(); definition.harvest!.Add(null!);
    Reject(() => EditorValidation.RequireEditable(definition), "malformed harvest entries fail safely before drawing");
    definition = Definition(); string mixedId = "abcdefabcdefabcdefabcdefabcdefab";
    definition.stages![0].cacheId = mixedId; definition.stages[1].cacheId = mixedId.ToUpperInvariant();
    Reject(() => definition.Validate(file, "pack:seed"), "stage identity uniqueness is case-insensitive like save restoration");
    string canonical = StageArtIdentity.Suffix(mixedId, 0, "assets/tree.glb", null, 1, 512);
    Check(canonical == StageArtIdentity.Suffix(mixedId, 0, " assets\\tree.glb ", null, 1, 512), "normalized model spelling does not invalidate stage cache identity");
    Reject(() => StageArtIdentity.ShiftPivot(16, 32, float.MaxValue, .01f, 100), "unrenderable offset geometry is rejected before Unity sprite creation");

    // Shared art is implemented only in the framework; no CIL library or runtime assembly is linked.
    SharedArtLibrary.Root = Path.Combine(folder, "shared", "ArtLibrary");
    byte[] pngA = { 137, 80, 78, 71, 13, 10, 26, 10, 1 };
    byte[] pngB = { 137, 80, 78, 71, 13, 10, 26, 10, 2 };
    File.WriteAllBytes(Path.Combine(folder, "__growable_stage_0.png"), pngA);
    File.WriteAllText(Path.Combine(folder, "__growable_stage_0.key"), "original-key");
    StageCacheMigration.CopyGlbCache(folder, "pack", "item", "__growable_stage_0", identity);
    Check(File.ReadAllBytes(Path.Combine(folder, identity + ".png")).SequenceEqual(pngA) &&
        File.ReadAllText(Path.Combine(folder, identity + ".key")) == "original-key", "legacy migration preserves both cached image and render key");
    Check(File.Exists(Path.Combine(folder, "__growable_stage_0.png")), "cache migration leaves the saved pack's old assets intact for Discard");
    StageCacheMigration.CopyGlbCache(folder, "pack", "item", identity, "duplicate-stage");
    Check(File.ReadAllBytes(Path.Combine(folder, "duplicate-stage.png")).SequenceEqual(pngA), "duplicating a cache-only stage keeps usable art");
    File.WriteAllText(Path.Combine(folder, "interrupted.key"), "original-key");
    StageCacheMigration.CopyGlbCache(folder, "pack", "item", identity, "interrupted");
    Check(File.ReadAllBytes(Path.Combine(folder, "interrupted.png")).SequenceEqual(pngA), "cache migration recovers after an interrupted key-only copy");
    File.WriteAllBytes(Path.Combine(folder, "missing-key.png"), pngA);
    StageCacheMigration.CopyGlbCache(folder, "pack", "item", identity, "missing-key");
    Check(File.ReadAllText(Path.Combine(folder, "missing-key.key")) == "original-key", "matching cached PNG can recover its missing key");
    File.WriteAllBytes(Path.Combine(folder, "collision.png"), pngB);
    bool collision = false;
    try { StageCacheMigration.CopyGlbCache(folder, "pack", "item", identity, "collision"); } catch (IOException) { collision = true; }
    Check(collision && File.ReadAllBytes(Path.Combine(folder, "collision.png")).SequenceEqual(pngB), "cache collision refuses to overwrite unrelated art");
    Check(!Directory.GetFiles(folder, "*.tmp-*").Any(), "cache migration leaves no staging files after completion");
    var entry = SharedArtApi.Publish("Apple tree", "apple.glb", pngA);
    var same = SharedArtApi.Publish("Renamed", "other.glb", pngA);
    Check(entry.Id == same.Id && same.OriginalName == "Apple tree", "framework library deduplicates art and retains original name");
    var other = SharedArtApi.Publish("Pear tree", "pear.glb", pngB);
    Check(SharedArtApi.GetEntries().Count == 2, "framework library works without Custom Item Loader");
    string pack = Path.Combine(folder, "pack");
    string imageA = SharedArtApi.CopyViewToPack(entry.Id, pack);
    string imageB = SharedArtApi.CopyViewToPack(other.Id, pack);
    Check(imageA != imageB && File.ReadAllBytes(Path.Combine(pack, imageA)).SequenceEqual(pngA), "library imports never replace existing pack art");
    string sourceA = Path.Combine(folder, "a"), sourceB = Path.Combine(folder, "b");
    Directory.CreateDirectory(sourceA); Directory.CreateDirectory(sourceB);
    File.WriteAllBytes(Path.Combine(sourceA, "plant.png"), pngA); File.WriteAllBytes(Path.Combine(sourceB, "plant.png"), pngB);
    Check(SharedArtApi.ImportImage(Path.Combine(sourceA, "plant.png"), pack) == imageA &&
        SharedArtApi.ImportImage(Path.Combine(sourceB, "plant.png"), pack) == imageB, "same-basename image imports are collision-safe and deduplicate");
    Reject(() => SharedArtLibrary.Resolve("../../outside.png"), "shared-art paths cannot escape the library");
    var directional = SharedArtLibrary.Publish("Four views", "model.glb", new[] { pngA, pngB, pngA, pngB });
    Check(File.ReadAllBytes(SharedArtApi.GetViewPath(directional.id, 1)).SequenceEqual(pngB), "growables may choose one view from a four-view item asset");
}
finally
{
    // Only this freshly created, uniquely named test directory is removed.
    Directory.Delete(folder, true);
}
if (args.Length > 0) BinaryChecks.Run(Check, Path.GetFullPath(args[0]));
Console.WriteLine($"{checks} regression checks passed.");
