# Cornucopia Bootstrap

Object name: `Cornucopia:Cornucopia`

Runtime behavior is in place:

- Add `CornucopiaAuraAuthoring` to the placed object entity prefab.
- The aura applies a short visible `DrainLessHunger` buff to nearby players.
- The system also blocks hunger accumulation directly while the player is inside the radius, so it does not stomp existing food buffs when the player leaves.
- The placed aura entity is marked `DontUnloadCD`/`DontDisableCD`; it does not keep the whole aura area loaded.
- `CornucopiaMod` adds the item to the first empty slot on `ObjectID.CopperWorkbench`.

Unity prefab wiring still needs to be done in-editor:

- Create or update the Cornucopia item/entity prefab.
- Add `ObjectAuthoring` with object name `Cornucopia:Cornucopia` and object type `PlaceablePrefab`.
- Add `InventoryItemAuthoring` with icons, stack settings, crafting cost, and crafting time.
- Add `PlaceableObjectAuthoring`, physics/health/mineable/state/animation authoring components, and normal NetCode ghost authoring.
- Use a graphical prefab whose root has `CornucopiaEntityMono`.
