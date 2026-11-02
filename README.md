# Custom Growables

A BepInEx add-on for Silverpine 1.7.3 and Custom Item Loader 3.0.0. It turns
ordinary Custom Item Loader items into plantable, persistent growables by
reading an optional `growable` block from the same item definition.

Custom Growables supports:

- Any number of visible growth stages.
- Base-game sprites, pack-relative PNG/JPG/JPEG art, or one-direction GLB
  renders at every stage.
- Per-stage size multipliers layered on the plant's base world scale.
- Native diffuse world lighting and optional per-stage colored point lights.
- Seed quality carried into the plant and all quality-capable harvest items.
- Native quality-colored glitter on above-Normal plants, through all stages.
- Optional, independently configurable +1 through +6-tier harvest-quality chances.
- Real-world-hour or processed-game-turn growth.
- Save/load persistence and offline real-time progress.
- Per-plant Spring / Summer / Autumn / Winter growth seasons, including regrowth.
- Player-property and roof planting restrictions.
- Multiple harvest outputs with quantity ranges and chances.
- Base-game or Custom Item Loader harvest items.
- Optional crop regrowth or persistent perennial harvest cycles.
- Separate saved maturity and harvest-readiness state for trees and bushes.
- Native tree-style render layering and Y-sorting so tall growables draw over
  terrain and wall tiles from their planted tile instead of falling beneath
  the tile layer.
- A dedicated in-game Growables Editor backed by CIL's item and pack APIs.
- Native-style player and NPC harvesting interactions.
- Save-safe assembly-qualified serialization for the add-on's custom seed
  component.

## Shared-library deletion (1.8.4)

Shared-art cards now offer **Delete from library**, with confirmation and Escape
to cancel. Deletion removes the **whole asset**, not just the currently selected
view, from every mod's shared catalog. Pack copies, source GLBs and image files
used by other entries are retained. Existing growable stage artwork copied into
packs is unchanged; direct shared-library consumers may lose access.

The framework keeps a recovery copy under `BepInEx/shared/ArtLibrary/trash` and
refuses deletion when records are unsafe or unreadable. Recovery is described in
the Modding Tools README. Copies are not automatically purged, so removing an
entry is not a disk-space cleanup operation. Explicit republishing can add it again.

Item Loader, Enemy Loader and Growables all use the same deletion API. Open
browsers refresh entries and thumbnails after any of them changes the library,
using a cheap in-memory revision token rather than repeated disk scans.

## Requirements

- BepInEx for Silverpine 1.7.3
- Silverpine Modding Tools 1.13.1 or newer
- Custom Item Loader 3.0.0 or newer (including its add-on GLB-cache API)

Install `CustomGrowables.dll` in:

```text
BepInEx/plugins/CustomGrowables/
```

Keep item packs and custom images in Custom Item Loader's normal folder:

```text
BepInEx/config/CustomItemLoader/
```

Source GLBs are authoring assets shared with Custom Item Loader and are kept
outside distributable packs in:

```text
BepInEx/shared/ArtLibrary/models/
```

## In-game Growables Editor

Version 1.8.3 fixes clipped text in the harvest-item picker and other choice
screens. Rows are left-aligned and grow to fit the full label, including the
separate item-ID line and wrapped long names/paths. The scroll view can no
longer squeeze them into single-line buttons. Changing or clearing search
returns to the top of the results; selection and Cancel behavior are unchanged.
Plants above Normal quality also display the base game's quality glitter,
colored from their inherited seed quality. The effect persists through growth,
regrowth and save/load, and follows per-stage artwork offsets. No pack edits
or save migration are required.

Version 1.8.2 fixes missing ground glow for small stage lights. Light depth
now uses the native terrain plane rather than the tree-style sprite depth.
The Morning Dew preset reaches the grass with its original radius/intensity,
without changing plant sorting or requiring pack edits. The depth control is
labeled terrain-relative; the existing `offsetZ` field is retained.

Version 1.8.1 fixes a planting/restore activation-order exception in
`YSorter.SetSortingOrder`. Stage updates apply native Y-based sorting directly
to the existing renderer, without depending on whether `YSorter.Awake` has
already run. Native tree layering, stage lights, and quality are preserved;
no save-format changes or `OnDestroy` callbacks are introduced.

Version 1.8.0 fixes nighttime brightness by sharing the native tree's diffuse
material. All plant art sources now respond to sunlight and local lights.
In **Stages & Art → Stage light / glow**, enable a separate light for any
stage. Morning Dew and Lantern presets provide native starting values; edit
RGB color, radius, intensity, flicker, and XYZ offsets independently per stage.
The artwork preview remains unlit and does not simulate the in-world glow.
Loading an unchanged growable or pressing Discard also leaves the item list
selectable; internal legacy-cache preparation no longer counts as a user edit.
Seeds now support Silverpine's native quality interface. The planted seed's
quality persists through growth, save/load, and repeated harvests. Under
**Harvest → Chance for higher quality**, optionally assign probabilities for
each upgrade amount (+1 through +6). See the quality section below.

Version 1.7.4 gives text fields full-width, word-wrapped boxes that grow and
shrink with their content, including trailing empty lines. Plant names accept
explicit line breaks. Sprite names, image/model paths, and harvest item
references wrap visually but remain single-line values in saved packs.
Artwork picker/import buttons have their own row so they do not squeeze the
input. Numeric and search fields remain single-line. Item cards and compact
dropdowns keep their existing ellipsis and full-value tooltips.

Version 1.7.3 corrects sidebar overflow that remained in 1.7.2. The sidebar is
420 design pixels wide, Search has its own single-line label, and item cards
reserve separate lines for the name and smaller ID. Long values use measured
ellipsis instead of wrapping into neighboring rows. Hover a card or truncated
choice to read its full value. Pack/stage choices and the selected-item header
also stay single-line. Fitted text is cached, and inherited skin dimensions
are reset so labels are not clipped by fixed-height styles. This update changes
only Growables; Item Loader is maintained separately.

Version 1.7.2 improves text spacing: short action buttons measure their text
instead of wrapping inside narrow fixed widths; field labels have wider,
single-line columns; and dropdowns use the available row width. Sidebar and
preview panels are wider, controls have sufficient height, and X/Y offsets
share a compact row. Help text and long item names still wrap when needed.
This follows Item Loader 3.0.2's matching layout correction. Build/regression
checks are headless; live layout at different resolutions still needs checking.

Version 1.7.0 adds **Planting → Growth seasons**. Toggle any combination of
Spring, Summer, Autumn, and Winter, or choose All seasons. At least one season
must be selected. Growth and post-harvest regrowth pause outside those seasons
without losing progress or maturity. Planting and harvesting already-ripe
plants remain available year-round. Save the pack and restart to apply edits.

Version 1.6.0 matches Item Loader's redesigned editor: a pack/item sidebar,
Lifecycle / Stages & Art / Harvest / Planting tabs, and a persistent stage
preview. Select one stage to edit and preview at a time. All art sources,
including native sprites and imported images, preview against grass turf.
Drag the plant in the preview to set that stage's X/Y offset, or enter tile
offsets numerically. Arrow keys nudge by 0.01 tile (Shift: 0.1); Escape cancels
a drag. Offsets move only the artwork, never the planted tile, collision,
interaction target, or native tree sorting anchor.

The Shared Art Library screen reads the framework-owned
`BepInEx/shared/ArtLibrary` catalog. Item Loader and Growables use the same
Modding Tools API; the library itself does not depend on Item Loader. Choose
one view from an entry (including four-view item art), or add the current
custom stage image/render to the library. Chosen art is copied into the pack
under a content-addressed filename, so recipients need neither the library
nor a source GLB. Imports never overwrite another plant's same-named image.

Normal Close/Escape prompts to Save, Discard, or Cancel when edits are dirty.
Save validates native sprite/anchor names, harvest IDs, and image decoding.
Host teardown still releases the editor immediately; it does not silently save.

Open `Modding Tools` on Silverpine's main menu and choose `Growables Editor`.
The editor lists the enabled items registered by Custom Item Loader. Create
the inventory/seed item in `Custom Item Editor` first, then select it in the
Growables Editor to configure:

- Annual, regrowing-crop, or perennial lifecycle behavior.
- Growth clock and planting restrictions.
- Base-game sprites, imported images, or imported GLB models for every stage.
- Stage order, durations, maturity, and the post-harvest return stage.
- One or more base-game or CIL harvest results.

Lifecycle modes, clocks, art sources, and stage references use dropdown-style
selection controls. Larger catalogs open a searchable selection screen:

- The harvest-item picker includes both registered CIL items and base-game
  items, while the text field remains available for direct ID/name entry.
- The artwork picker lists the native prefab sprite catalog, pack PNG/JPG/JPEG
  images, and GLBs from CIL's shared authoring-model library. `Import Image /
  GLB` copies images into the pack and imports GLBs into
  `BepInEx/shared/ArtLibrary/models` with readable hash-suffixed filenames. GLB stages expose
  rotation, zoom, resolution, and a one-direction preview; they do not require
  front/back/left/right variants. The preview places the plant over
  Silverpine's native one-tile grass turf at its configured world size.
- Escape or `Cancel` returns to the growable without changing the value.

The Growables Editor does not duplicate CIL's item editor or own a second pack
format. It reads and atomically writes only the selected item's `growable`
extension through `CustomItemApi`; CIL continues to own the pack, item
identity, inventory visuals, cloning, and registration. Restart Silverpine
after saving to apply registration changes.

## Example item pack

The seed item and harvested item are normal Custom Item Loader entries. Add a
`growable` object only to the seed item:

```json
{
  "packId": "example.moonroot",
  "enabled": true,
  "items": [
    {
      "id": "moonroot",
      "name": "Moonroot",
      "description": "A pale medicinal root.",
      "image": "moonroot_item.png",
      "category": "Alchemy",
      "sound": "Plant",
      "value": 8,
      "bulk": 0.5
    },
    {
      "id": "moonroot_seeds",
      "name": "Bag of Moonroot Seeds",
      "description": "Seeds that thrive in open soil.",
      "image": "moonroot_seeds.png",
      "category": "Miscellaneous",
      "sound": "Plant",
      "value": 5,
      "bulk": 0.5,
      "growable": {
        "visibleName": "Moonroot",
        "clock": "RealTimeHours",
        "playerPropertyOnly": true,
        "allowRoofed": false,
        "growsInWinter": false,
        "stages": [
          {
            "sprite": "sprite_nature_seeds",
            "duration": 8
          },
          {
            "image": "moonroot_growing.png",
            "duration": 16,
            "pixelsPerUnit": 32,
            "pivotX": 0.5,
            "pivotY": 0.5
          },
          {
            "image": "moonroot_mature.png"
          }
        ],
        "harvest": [
          {
            "item": "example.moonroot:moonroot",
            "minimum": 1,
            "maximum": 3,
            "chance": 1.0
          }
        ]
      }
    }
  ]
}
```

Custom Item Loader ignores the behavior itself but preserves the `growable`
block when its item editor saves the pack. Use the dedicated Growables Editor
for normal editing; direct JSON remains supported.

Use a separate seed item without another use behavior. Native seed bags,
saplings, edible items, potions, and other components overriding `OnUse`
conflict: Silverpine calls every component's use behavior. Sticky items are
also incompatible because the game blocks their use entirely. The editor
labels incompatible items, disables attachment, and refuses to save attached
growable behavior; runtime registration independently checks compatibility.
Passive components and the existing Growables seed component are allowed.
Harvest outputs can still be normal edible fruit; this restriction applies
only to the item planted. Use a component-free clone when reusing seed art.

## Growth stages and art

`stages` must contain at least two entries. Each stage must specify exactly one
art source:

- `sprite`: Exact asset name of a sprite referenced by a loaded base-game
  prefab, or an explicit Unity Resources path. The plugin indexes native
  prefab renderers and the turnip crop's mature sprite; it deliberately does
  not scan or bulk-load Silverpine's enormous full sprite library.
- `image`: PNG, JPG, or JPEG path relative to the item-pack JSON. The path
  cannot leave the pack directory.
- `model`: Relative GLB reference in CIL's shared
  `BepInEx/shared/ArtLibrary/models` authoring library. Custom Item Loader
  renders one transparent, tightly cropped sprite from `rotation`, `zoom`, and
  `resolution`. The deterministic PNG/key pair is cached inside the pack's
  `.cache` folder and reused until the model or render settings change. Legacy
  pack-relative GLBs are imported into shared storage when the growable is
  resaved, without deleting the legacy source file.

Examples of native sprite names include:

```text
sprite_nature_seeds
sprite_nature_turnip_seedling
sprite_nature_turnip_mature
sprite_nature_sapling_oak
sprite_nature_sapling_pine
```

Base-game art retains its native pivot and pixels-per-unit. Selecting native
art in the editor also copies those values into the stage, so a later imported
image starts from the same planted anchor. Custom pivots, including `(0.5, 0.5)`,
are always honored. For edited native art such as berried Juniper, explicitly
choose **Match native sprite** and select its native sprite (`anchorSprite` in
JSON). This copies that sprite's planted anchor in world units without relying
on a preceding stage or an asynchronous GLB fallback. GLBs remain centered by
default. Each stage accepts `offsetX` and `offsetY` in tiles (default 0, finite
values from -100 to 100); offsets remain constant when stage scale changes.
All generated growables use Silverpine's native oak-tree sorting layer, depth,
and Y-sort offset so larger stages overlap map tiles the same way native trees
do. Override `pixelsPerUnit`, `pivotX`, or `pivotY` on a stage when needed.

GLB-specific stage fields are `rotation` (`[x, y, z]`, default
`[20, 135, 0]`), `zoom` (default `1`), and `resolution` (default `512`). Unlike
turnable items or furniture, a growable generates only this one view.

`Load Cached Preview` displays the current CIL cache without rerendering.
`Render GLB Preview` uses the shared source model and updates the stage's
distributable cache. A released growable pack may omit the authoring GLB as long
as its `.cache` PNG remains present; cache-only consumers load that PNG exactly
as Custom Item Loader does for ordinary GLB items.

The editor assigns a stable `cacheId` to each stage. Its CIL cache suffix uses
that ID and a render-settings fingerprint, not its current list position.
Reordering/removing stages therefore cannot swap cache-only sprites, and an
unsaved preview with different settings cannot overwrite a saved stage's art.
Old index-based caches remain readable; opening the editor copies them to new
identities without deleting the originals. IDs are written on Save. New saves
also record stage IDs to retain stage identity after subsequent reordering.

Caches use CIL 3's collision-safe filenames:

```text
<pack folder>/.cache/v2-<full-identity-hash>.png
```

Every stage also accepts `scale` from `0.1` to `10` (default `1`). This is a
multiplier: an overall growable scale of `1.5` and stage scale of `0.5` display
that stage at an effective `0.75×` world scale. Stage scaling works with native
sprites and imported images as well as rendered GLBs.

`duration` belongs to the stage containing it and controls how long that stage
lasts before advancing. The mature final stage does not need a duration.

## Stage lights

The diffuse-material fix applies to every plant sprite, including native art,
custom images, and cached GLB renders. It does not alter inventory icons or
bake lighting into cached artwork. FakeAO/ground-shadow overlays are separate
and are not added by this feature.

Add an optional `light` object to any stage. Omit it, or set `enabled` to
`false`, for a stage without emitted light. A minimal `"light": {}` uses the
Morning Dew preset. A fully specified equivalent is:

```json
"light": {
  "enabled": true,
  "color": [1.0, 0.89451975, 0.49411762],
  "radius": 1.0,
  "intensity": 1.0,
  "flicker": false,
  "offsetX": 0.0,
  "offsetY": 0.0,
  "offsetZ": -0.55
}
```

The editor also offers Item Loader's Lantern defaults: RGB `[1, 0.68, 0.41]`,
radius `5`, intensity `2`, with native lantern flicker. RGB values must be
finite numbers from 0–1; radius is 0.1–50 tiles and intensity is 0–20. Light
offsets are relative to the planted tile in world units (X right, Y up, Z
depth), independent of stage/art scale and sprite offsets. X/Y accept -100–100;
Z accepts -10–10 and is relative to the terrain plane at world Z=1, not the
plant's tree-style sorting depth. Native -0.55 therefore places the light at
world Z=0.45, matching Morning Dew's ground-light footprint. In 1.8.0/1.8.1,
adding the same offset to the plant's Z=0.5 put grass outside a radius-1 light.

Stages do not inherit lights from the previous stage. Growth, harvesting,
regrowth, and save restoration select the current stage's light. Duplicated
stages get independent copies of their settings. The light is a transient
child, not an additional saved native component; no `OnDestroy` callback is
used. Ordinary/non-flickering lights have no new per-frame update loop.

## Seed and harvest quality

Growable seed components implement Silverpine's `IQualityItemComponent`, so
native quality assignment, stars, and applicable quality-upgrade mechanics
recognize them. Qualities are Normal, Quality, Superior, Exceptional, Pristine,
Blessed, and Legendary. Existing seeds and already-planted growables without
saved quality default to Normal. No automatic random quality is assigned when
buying or creating a seed.

Planting copies the used seed's quality into that plant instance. It survives
save/load, stage changes, and perennial harvests. Each harvested item that
already supports quality receives the plant's quality, including returned
growable seeds and inventory-overflow drops. Items without native quality
support are unchanged; they are not given artificial quality components.

Above-Normal plants automatically use the native world quality-particle prefab
at every growth stage: green for Quality, light blue for Superior, magenta for
Exceptional, yellow for Pristine, white for Blessed, and red for Legendary.
Normal plants have no glitter. It stays native-sized around the planted point
plus the current stage's X/Y art offsets, independently of sprite/GLB scale.
Sorting stays anchored to the planted tile, like the plant itself. This is a
visual quality indicator, not a light source; stage glow settings remain
independent. The editor's sprite preview does not simulate particles. Existing
saved quality-bearing plants gain the effect when loaded, without pack edits.
Disabling/removing a plant clears its transient effect through `OnDisable`
and explicit removal cleanup; no `OnDestroy` callback is added.

This is not a blanket restriction on `OnDestroy`: object-local cleanup can
use it. Silverpine's bootstrap-host destruction must not tear down persistent
plugin registrations or Harmony patches. Effect cleanup first deactivates
the child, then destroys it; it does not retain disabled effects or save them.

An optional per-plant harvest setting can improve individual products:

```json
"qualityBonusEnabled": true,
"qualityBonusChances": [0.20, 0.05, 0.01, 0.0, 0.0, 0.0]
```

The six entries are the probabilities of +1, +2, +3, +4, +5, and +6 quality
tiers, respectively. This example gives 20% +1, 5% +2, 1% +3, and 74% unchanged.
The editor displays percentages; JSON uses fractions from 0–1. Values must be
finite and total no more than 1 (100%). Each quality-capable product gets one
independent roll; chances are not sequential/compounding rolls. Results cap at
Legendary. The original plant never changes quality, but improved returned
seeds can grow higher-quality plants in the next generation.

Bonuses are **off by default**. When enabled without an explicit array, the
default is 10% +1 tier and 90% unchanged. Six zeroes mean no upgrades. This
setting affects quality only, not yield counts or harvest drop chances.

## Growable fields

| Field | Default | Meaning |
|---|---:|---|
| `visibleName` | Derived from seed item name | Plant name used in interaction and NPC text. |
| `clock` | `RealTimeHours` | `RealTimeHours` or `GameTurns`; controls the unit used by stage durations. |
| `qualityBonusEnabled` | `false` | Enable per-product chances to exceed the plant's inherited quality. |
| `qualityBonusChances` | `[0.1, 0, 0, 0, 0, 0]` | Probabilities for +1 through +6 quality tiers; total must be at most 1. |
| `playerPropertyOnly` | `true` | Requires the player's current tile to pass Silverpine's owned-property check. |
| `allowRoofed` | `false` | Allows planting beneath a roof tile. |
| `growthSeasons` | Legacy winter setting | Array of `Spring`, `Summer`, `Autumn`, `Winter`; at least one. Controls initial growth and regrowth. Overrides `growsInWinter` when present. |
| `growsInWinter` | `false` | Legacy fallback when `growthSeasons` is omitted/null: false selects Spring/Summer/Autumn; true selects all four seasons. |
| `blocksMovement` | `false` | Adds an impassable turf collider to the plant. |
| `replaceExistingPlants` | `true` | Removes an existing plant/herb/crop on the planting tile. |
| `interactionTurns` | `5` | Turns spent planting, destroying, or harvesting. |
| `perennial` | `false` | When true, the plant becomes mature once and retains that saved maturity across later harvest cycles. Requires `regrowStage`. |
| `matureStage` | `-1` | First stage considered permanently mature. `-1` uses `regrowStage` for perennials and the final stage otherwise. |
| `regrowStage` | `-1` | `-1` destroys the plant after harvest; otherwise resets its appearance and harvest timer to this zero-based non-final stage. |
| `scale` | `1` | Base world scale for the generated plant prefab, from `0.1` to `10`. Each stage multiplies this value by its own `scale`. |
| `stages` | required | Ordered art and duration definitions. |
| `harvest` | required | One or more item-result definitions. |

For turn-based growth, each duration is a processed game-turn count. For
real-time growth, durations are hours and elapsed time while the game is
closed is applied after the saved world finishes loading. If its restored
local season is not selected, that offline interval is discarded (not banked
for a later season); existing progress is preserved. Seasons follow
Silverpine's in-game calendar and native local overrides: dungeon tiles use
Summer and high mountains use Winter. They do not follow the computer's
calendar while the game is closed.

For example, `"growthSeasons": ["Spring", "Summer"]` permits growth and
regrowth only during those two local seasons. Omit the field to retain an
older pack's winter behavior. Case-insensitive names are accepted; unknown
names, null entries, and empty arrays are rejected when saving/registering.

### 1.7.1 comprehensive source audit

Reviewed every production source file and the relevant native planting,
harvest, inventory, serialization, and turn-event paths, plus CIL and Modding
Tools integration. This audit includes the following corrections:

- Harvest preparation finishes before granting any items. Reentrant inventory
  callbacks cannot repeat a harvest; callback exceptions after insertion do
  not duplicate ground loot. Missing outputs preserve harvest readiness,
  full inventories receive ground overflow, and cosmetic failures do not
  restore a consumed harvest.
- Planting rechecks seed ownership, player position, property permission, and
  roofing at completion. Cancellation and repeated/stale callbacks cannot
  plant again or unlock a newer interaction. Allocation failure preserves
  the seed and occurs before removing existing plants.
- Malformed saved entries fail safely without replacing their JSON. Invalid
  numeric input blocks Save instead of silently saving an older value; small
  floats retain their precision. External growable edits are detected before
  saving. Copying stages preserves unknown extension fields.
- Cache-only GLB validation now decodes cached art. Migration can recover an
  interrupted key/PNG copy without replacing unrelated art. Stage IDs are
  case-insensitive, model references normalize consistently, and unrenderable
  offset geometry is rejected before Unity sprite creation.
- Failed registrations release their owned templates and art. Ripe plants
  detach from turn events and resubscribe for regrowth. Quick-saving before
  the first world update no longer loses pending offline progress.
- Searchable pickers cache filtered results and display 64 entries per page.
  Structural editor transitions end the current IMGUI pass; closing the
  editor invalidates pending previews. Seed compatibility is cached for UI
  drawing and independently rechecked on Save.

Validation: Release build with zero warnings/errors; 143 automated checks,
including production plant/seed components in a simulated game host, season
and timing logic, editor validation, cache migration, shared art, and compiled
API references against the installed CIL/Modding Tools DLLs. No `OnDestroy`
cleanup was added. This update does not require replacing either dependency.

This is a comprehensive source audit, not a guarantee of zero defects. Live
Unity rendering, wall layering, IMGUI interaction, actual save/load dispatch,
asynchronous GLB rendering, and in-game frame-time profiling still need the
manual checks in `Tests/README.md`. Arbitrary failures inside other mods or
the native ground-loot system are not covered by the simulated inventory tests.

### 1.7.0 fixes and performance

- Offline growth waits for the saved season and local world data, rather
  than using the previous world's season during deserialization.
- Timers accumulate in double precision so multi-day stages do not stop
  accepting small frame increments. The existing float save field is kept
  for compatibility; only saving rounds progress to its older precision.
- Disabled plants detach from their original turn-event publisher using
  `OnDisable`, with resubscription on enable; no `OnDestroy` dependency.
- Zero-chance harvest outputs cannot drop on a zero random sample.
- Growth reuses resolved definitions, source images share decoded textures,
  and initial registration waits for CIL's completed item snapshot.
- Preview invalidation uses allocation-free value snapshots instead of
  per-event JSON serialization. Art-library search results are cached until
  the query or catalog changes.

Run `dotnet run --project Tests/RegressionChecks.csproj -c Release` for
definition, cache, placement, season, timing, and simulated component tests.
The test host exercises the production plant component with minimal game
stubs; native rendering, Unity callback dispatch, and live frame-time profiling
still require an in-game check.

### Perennial example

An apple tree can grow through juvenile stages once, then cycle only between
a mature bare tree and a harvest-ready fruiting tree:

```json
{
  "perennial": true,
  "matureStage": 2,
  "regrowStage": 2,
  "stages": [
    { "image": "apple_sapling.png", "duration": 168 },
    { "image": "apple_young.png", "duration": 336 },
    { "image": "apple_tree_bare.png", "duration": 72 },
    { "image": "apple_tree_fruit.png" }
  ],
  "harvest": [
    {
      "item": "example.orchard:apple",
      "minimum": 2,
      "maximum": 5,
      "chance": 1.0
    }
  ]
}
```

On the first pass, the tree reaches stage 2 and permanently records that it is
mature. Stage 3 is harvest-ready. Harvesting returns the same saved tree
object to stage 2 for 72 hours; it never returns to either juvenile stage.

## Harvest results

`harvest[].item` accepts either:

- A Custom Item Loader qualified ID such as `example.moonroot:moonroot`.
- A base-game display name such as `Turnip`.

`minimum` and `maximum` are inclusive. `chance` is evaluated once per harvest
result and ranges from `0` to `1`. Results that do not fit in the receiving
inventory are placed in a ground loot container on the crop tile.

## Build

```text
dotnet build "Plugin Development/CustomGrowables/CustomGrowables.csproj" -c Release
```

Copy the resulting `CustomGrowables.dll` into the plugin folder shown above.

## Credits

Created by **Saelac and ChatGPT**.
