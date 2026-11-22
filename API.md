# Growables skills API

Available in Custom Growables **1.9.0**, API version **1**, namespace
`SilverpineMods.CustomGrowables`. The same API handles custom plants and the
base game's crops, planted saplings, and harvestable herbs. It does not require
an item pack, and does not implement a skills system itself.

Reference `CustomGrowables.dll`, the game's `Assembly-CSharp.dll`, and
`UnityEngine.CoreModule.dll`. Declare this BepInEx dependency on your plugin:

```csharp
[BepInDependency("renegadex.silverpine.customgrowables", "1.9.0")]
```

Custom Growables' normal Item Loader and Modding Tools dependencies still apply,
even when your integration only observes base-game plants. Do not distribute
copies of those assemblies inside your skills mod.

## Hooks

| Member on `GrowableApi` | When it runs / intended use |
| --- | --- |
| `Planted` | Successful seed planting, after a plant exists and the seed has left its source inventory. Award planting XP here. |
| `GrowthChanged` | An actual growth-stage transition or post-harvest regrowth reset. Observe readiness, track replacement objects, or update UI. |
| `PreparingHarvestItem` | Each rolled output, after existing quality/tool rules and before delivery. Adjust `Count` and `Quality`; **do not award XP here**. |
| `Harvested` | Once for a harvest that actually delivers at least one item to inventory or, for custom plants, the ground. Award harvesting XP here. |
| `TryGetPlant(GameObject, out GrowableInfo)` | Query a live plant's identity, planted tile, quality, stage, readiness, and perennial status without triggering events. Pass the plant root, not an artwork/particle child. |

Events run synchronously on the game thread, with a null sender. Keep handlers
short; do not access Unity objects from background work. Subscribe once, after
the dependency loads. Unsubscribe only on a genuine end to your integration:
Silverpine destroying BepInEx's startup host is **not** a plugin unload.

Each subscriber is isolated from exceptions thrown by other subscribers. A
throwing harvest modifier has its `Count` and `Quality` changes rolled back;
later subscribers still run. Exceptions are logged. Notification side effects,
such as your own XP changes, cannot be rolled back: make those handlers atomic.

### Identity and attribution

`GrowableInfo` is a point-in-time snapshot with `GameObject`, `Id`, `DisplayName`,
`Kind`, `IsCustom`, `Position`, `Quality`, zero-based `StageIndex`, `StageCount`,
`IsHarvestReady`, and `IsPerennial`. Custom IDs are pack-qualified growable IDs;
native IDs use `native:` plus the prefab name without its trailing `(Clone)`.
IDs describe a **plant type**, not a unique planted instance. Native display
names may be prefab names where the game provides no crop display name.

Planting and harvesting supply an `Actor`. Harvest actors come from the
destination inventory owner, so **NPCs are included**. Filter for the actor your
skills mod owns (for example, `actor == Player.Instance.gameObject`), and handle
null owners. `Planted.Seed` references the consumed seed item; do not reinsert it.

Growth has `Before`, `After`, and `Reason`, but no actor. The game does not
persist planter ownership, and naturally generated herbs need not have a
planter. Your mod owns any planter tracking, skills, XP, and persistence.
Unity object references are session-local; do not serialize them or retain
completed event arguments indefinitely. Custom single-harvest objects are
scheduled for destruction when `Harvested` runs. Use the snapshot's value
properties rather than assuming the referenced object remains alive.

### Harvest bonuses

`PreparingHarvestItem` supplies `Plant`, `Actor`, `Item`, `SupportsQuality`, and
two supported writable properties:

- `Count` starts at 1 **per already-rolled output**, not per harvest definition
  or whole harvest. It is clamped to 0–100 after each subscriber. For example,
  five rolled apples with `Count = 2` each produce ten independent apple items.
  Setting 0 suppresses that output; it does not cancel or refund the harvest.
- `Quality` starts at the output's existing quality. Set it only when
  `SupportsQuality` is true. Undefined enum values are rejected; unsupported
  items never gain a quality component. Plant quality itself is unchanged.

Modifiers compose in subscription order. Prefer additive changes that preserve
previous subscribers' decisions. Native herb tool-quality limits and strange
plant output selection happen **before** this hook; a skills bonus can therefore
raise the final output above a tool's normal quality limit. Custom seed-quality
inheritance and configured random upgrades also happen before this hook.
The API does not add extra probability rolls of its own.

Treat `Item` as read-only. Directly mutating its components, the plant, or the
inventory is unsupported and cannot be rolled back. Do not start another
harvest from these handlers. Reentrant/duplicate completion is guarded, but
handlers are not a replacement for the normal interaction workflow.

`Harvested` supplies the pre-harvest `Plant` snapshot, `Actor`, `Inventory`,
`InventoryItems`, `DroppedItems`, and `ItemCount`. The two lists are disjoint,
structurally read-only, and contain only confirmed delivered items. Their item
instances are game-owned; treat them as read-only too. Rejected deliveries and
zero-output harvests do not grant a `Harvested` event. Failed output preparation
does not grant one either. Custom harvest overflow is reported only after its
ground-drop operation succeeds. Native harvests retain the game's capacity
behavior: rejected items are not automatically converted into ground drops.

### Growth and native coverage

| Plant path | Planting | Growth | Harvest modifiers/results |
| --- | --- | --- | --- |
| Custom growables | Custom seed completion | Stage changes, offline catch-up, regrowth resets | Yes, including NPCs and multiple outputs |
| Native `Crop` (turnip) | Native seed completion | Immature → mature | Yes, including NPCs |
| Native `InGameTimeCrop` (saplings/foliage) | Native seed completion | Replacement with the mature world object | Not tree felling |
| Native herb `ResourceNode` | If actually created by a supported seed action | No invented growth cycle | Yes, including naturally generated herbs and NPCs |
| Decorative native seed plants | Native seed completion | Only if using the native timed-crop path | No invented harvest action |

Ore mining, tree felling, explosions, uprooting/destruction, generic item
transactions, and world generation are **not** planting/harvesting events.
Cancelled/rejected seed uses and starting a long interaction grant no XP events.
Restoring a save does not emit planting/harvest notifications.

Custom `GrowthChanged` reports the before/after stage for each advance, not one
callback for every intermediate stage skipped by a large elapsed-time update.
`Reason` is `Growth`, `OfflineCatchUp`, or `Regrowth` (harvest resets readiness).
Save-loading activity is suppressed; a subsequent offline catch-up may report
`OfflineCatchUp`. Native crops use `Growth` when the game actually matures them,
including when that follows elapsed offline time; the native maturity method
does not distinguish those causes.

Timed saplings replace their `GameObject`: `Before.GameObject` and
`After.GameObject` differ. The replacement may be a tree/decorative
`NativePlant`, not a harvest-ready crop. That replacement snapshot is available
in the event even when `TryGetPlant` would not otherwise recognize the mature
tree. Native crops/saplings without a quality concept report `Quality.Normal`;
native herbs report their own quality. The API does not retrofit seed-quality
inheritance or perennial behavior onto base-game plants.

This version does not provide growth-speed, planting-permission, cancellation,
or forced-harvest controls. Native season/visibility/interaction rules remain
the game's responsibility. No new save fields or serializable components are
added, and unsubscribed paths avoid per-frame scans or per-tick event snapshots.

## Compile-checked example

[`ExampleSkillsIntegration.cs`](Tests/ApiConsumer/ExampleSkillsIntegration.cs)
is a separate-assembly example using only public API members. It accepts your
actor filter, level lookup, XP-award callback, and optional growth observer. Its
sample policy grants planting/harvest XP and level-based extra yield/quality;
the values are illustrative, not balancing rules built into Growables.

Keep one instance for the integration's session. Do **not** call its `Dispose`
from the short-lived BepInEx bootstrap host's `OnDestroy`.

After building Custom Growables, compile the consumer from this repository:

```powershell
dotnet build Tests/ApiConsumer/ApiConsumer.csproj -c Release --nologo
```

This verifies public API accessibility against the real plugin and game DLLs;
it does not install a second plugin or run Unity.
