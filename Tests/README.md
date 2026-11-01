# Growables regression checks

Run `dotnet run --project Tests/RegressionChecks.csproj -c Release` from this
repository. The console suite links production definitions, plant and seed
components, timing, seasons, preview snapshots, editor validation,
seed compatibility, cache migration, and Modding Tools shared-art code.
Small game/path stubs allow these tests to run outside Unity.

Version 1.8.3 adds binary guards for content-measured picker rows, wrapping,
left alignment, literal labels, absence of forced height constraints, and
scroll reset when filtering. The old picker fails the routing guard. These
checks inspect compiled layout calls; they do not render Unity UI. In-game,
open Choose Harvest Item with enough items to scroll, verify both the name
and qualified ID are fully visible, then test long/multiline names, artwork
paths, selecting an item, Cancel/Escape, page navigation, and searching or
clearing search after scrolling to the bottom at 1280x720 and 1920x1080.

It also tests native quality glitter: all six above-Normal colors, no Normal
effect, inactive/template exclusion, color-before-emission, native material
reuse, scale-independent sizing, per-stage art offsets with planted-tile
sorting, no restart on unchanged refresh, growth/regrowth, save/load,
disable/reenable, removal and missing/failed assets. Harvest-quality bonuses
must not recolor the plant. Binary guards verify the native world prefab path,
native quality-color API, direct sorting and non-serialized lifecycle wiring.
The world prefab/resource key, native sorting offset (2), layer, relative Z
(1), and native quality palette were inspected in Silverpine 1.7.3's assets
and code. These are headless lifecycle/geometry checks, not rendered Unity
particles. In-game, compare each seed quality against a natural quality plant,
including an upward-offset stage, a large GLB, harvest/regrowth, save/reload,
and removal. Normal plants must stay particle-free; stage lights must remain
independent. The lifecycle guard protects persistent bootstrap registrations,
not a blanket ban on object-local `OnDestroy`. The 1.8.3 suite passes 337 checks,
including compiled-plugin and installed-dependency checks.

Version 1.8.2 adds light geometry regressions using the inspected native
asset depths: oak/custom plants Z=0.5, grass/Morning Dew Z=1, and Morning
Dew's light Z=0.45. They reproduce the old radius-1 light not reaching grass,
verify the native ground footprint at both parent depths, and check custom
depth/XY offsets with scaled plants. These checks validate geometry, not
rendered pixels; compare mature planted Morning Dew with the native plant at
night after installing the fix. Existing test light hosts now use the actual
custom-plant depth instead of a Z=1 stand-in. The 1.8.2 suite passes 302 checks,
including the installed-binary checks.

Version 1.8.1 replaces the no-op YSorter stub with an Awake-dependent renderer
cache matching the native failure. The new activation-order test reproduces
the reported `OnEnable → ApplyStage → SetSortingOrder` null reference against
the old code. It checks both Awake orders, inactive setup, Start, growth,
save restoration/art refresh before Awake, reactivation, and native rounding
at positive/negative Y positions. Binary checks also inspect the installed
game's YSorter initialization and ensure stage updates do not call it early.
This is explicit lifecycle simulation, not Unity lifecycle execution.
The 1.8.1 suite, including installed-binary checks, passes 297 checks. For a
live confirmation, plant immediately after loading the game, grow through a
stage change, save/reload, and verify there are no YSorter exceptions and
tree/wall layering remains unchanged.

Version 1.8.0 adds production stage-light and harvest-quality checks: presets,
validation, independent stage copies, scale-independent positioning, lifecycle
cleanup, growth/regrowth switching, quality persistence, legacy saves, native
seed planting quality, unsupported outputs, inventory overflow, independent
upgrade rolls, all +1 through +6-tier/quality combinations, Legendary caps,
and probability bounds. Binary checks verify native shared-material use,
unchanged serializable component layout, editor wiring, and quality integration.
The prior Discard/clean-load regression remains covered.

To additionally check the compiled plugin against installed dependency APIs,
run these commands from the game directory (build the release first):

```powershell
dotnet build 'Plugin Development/CustomGrowables/CustomGrowables.csproj' -c Release --no-restore --nologo -m:1
dotnet build 'Plugin Development/CustomGrowables/Tests/RegressionChecks.csproj' -c Release --no-restore --nologo -m:1
dotnet 'Plugin Development/CustomGrowables/Tests/bin/Release/net9.0/RegressionChecks.dll' '.'
```

The 1.7.1 audit run passes 143 checks including these optional binary checks.
Version 1.7.2 adds two binary layout guards (145 total) for text-measured
buttons and field labels. These inspect sizing calls, not rendered pixels.
Version 1.7.3 adds text-fitting and sidebar/tooltip wiring checks (157 total):
exact fits, long IDs, narrow widths, embedded newlines, and Unicode boundaries.
Text-fitting tests use deterministic sample font metrics, not Unity rendering.
Version 1.7.4 adds multiline measurement, reference normalization, name
persistence, and compiled input-wiring checks (173 total). The text-box height is calculated
by Unity's width-dependent layout; the headless suite does not render it.
Package restore must already have completed before using `--no-restore`.

Checks cover legacy cache preservation, stage reordering/removal, cache-only
duplication, render-setting isolation, explicit center/native anchors,
scale-independent offsets, finite-value validation, perennial definitions,
five-item yields, JSON persistence and framework-only shared-art reuse/imports.
They also cover all 15 season combinations, native local-season overrides,
offline/quick-save timing, subscription cleanup, failed/reentrant harvests,
inventory exceptions before/after insertion, full-inventory overflow, planting
cancellation/revalidation, competing item use behaviors, malformed editor
data, numeric round trips, external-edit conflicts, and interrupted cache copies.
Each run creates and cleans up a unique directory under the test output folder.

Live-game checklist (requires Silverpine):

1. Compare a native Juniper stage with its custom berried image using Match
   native sprite; verify no jump and native tree/wall layering.
2. Drag each stage over the grass preview, nudge with arrows and reset. Plant,
   advance and reload; confirm offsets match without moving collision/interaction.
3. Render a GLB, change selection/settings or discard during rendering, and
   verify stale results cannot replace the active preview or saved art.
4. Open a legacy cache-only pack, reorder/remove/duplicate stages, save and
   restart with no source GLBs; verify each stage retains its artwork.
5. Close/Escape with unsaved changes, during a drag, and in a picker; test
   Save/Discard/Cancel, including an invalid reference that must fail Save.
6. Select art published by Item Loader in Growables, then publish stage art
   and reuse it in Item Loader. Verify copied packs work without shared files.
7. Plant seasonal annuals and perennials, harvest five apples with full/empty
   inventories, change season, save immediately after load, and restart.
   Verify regrowth pauses while ripe harvests remain available year-round.
8. Attempt to attach behavior to native seeds, edible fruit, potions, and
   sticky items; check that the UI explains the conflict. A component-free
   seed should plant normally and consume exactly one item, including after
   canceling/restarting its interaction.
9. Enter incomplete/non-finite numeric text, load malformed JSON, or change
   the growable externally while editing. Verify Save fails visibly and does
   not overwrite the previous saved definition. Correct/reload and save again.

These manual checks have not been executed as part of the 1.7.1 source audit.
Mocks do not verify Unity lifecycle dispatch, actual UI event ordering,
native rendering/physics, or arbitrary third-party callback failures.

For 1.7.3, also check the sidebar with `Skeleton.Pack:mod_skelebones` and very
long names/IDs at 1280×720 and 1920×1080. Each card must have only two visible
lines with no clipped third line, Search must not split, and hovering should
reveal full text. Confirm selection, search, scrolling, and dropdowns still work.
Live rendering of this update has not been verified by the headless suite.

For 1.7.4, enter and delete a long plant name with explicit line breaks,
including several empty lines at the end. The box should grow and shrink to
show the whole value. Paste long sprite names, image/GLB paths, and harvest
references; verify they wrap without widening the panel or overlapping the
picker/import buttons. Save/reload and check that plant-name line breaks are
preserved but reference values contain no CR/LF characters. Repeat at 1280×720
and 1920×1080, scroll through a tall field, then close/reopen the editor.

For 1.8.0, compare native and custom plants at noon/night and near a lantern.
Enable Morning Dew on only the final stage; grow, harvest, and reload to check
that its light appears/disappears correctly. Test custom colors, offsets, size
changes, and flicker. Verify the Stages & Art controls remain readable while
scrolling at both resolutions; the artwork preview intentionally stays unlit.
Plant quality seeds, save/reload, and check quality stars on compatible outputs
and returned seeds. Test zero, partial, and 100% multi-tier bonus distributions,
including caps at Legendary and a full inventory. Confirm a sum over 100%
cannot be saved. These live rendering/UI checks have not been performed by
the headless suite.
