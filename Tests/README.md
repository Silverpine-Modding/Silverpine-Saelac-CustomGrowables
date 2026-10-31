# Growables regression checks

Run `dotnet run --project Tests/RegressionChecks.csproj -c Release` from this
repository. The console suite links production definitions, plant and seed
components, timing, seasons, preview snapshots, editor validation,
seed compatibility, cache migration, and Modding Tools shared-art code.
Small game/path stubs allow these tests to run outside Unity.

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
