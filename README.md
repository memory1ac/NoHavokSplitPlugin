# NoHavok Split StaticModelGroup

Frosty Editor plugin: splits Battlefield 1 `StaticModelGroupEntityData` into individual `ObjectReference` entries on existing `WorldPartData`, so instances can be edited (moved / dragged) instead of remaining locked in a Havok compound group.

Current version: **1.0.0.0**

## What it does

`StaticModelGroup` packs many static meshes into one group. Group transforms are constrained by the Havok compound, so you cannot move pieces independently. This plugin:

1. Scans `SubWorldData` under the map folders you select
2. Finds `StaticModelGroupEntityData` in those SubWorlds
3. Writes each member (mesh, variation, Havok instance transform) into **existing** `WorldPartData` in the same map folder
4. After every member of a group is placed, removes that group from the SubWorld and strips related connections

It does **not** create new WorldParts. If a SubWorld has no existing `WorldPartData`, its groups are left unchanged and the result dialog reports that.

If any member of a group cannot be placed into a WorldPart, **the whole group is kept**, so you never get a half-split group.

## Usage

1. Open BF1 in Frosty Editor
2. **Tools → Split StaticModelGroups (NoHavok)**
3. Maps are grouped as `Levels/.../MP|SP/<map>`. Use the filter, Select all, or Select none
4. Check maps, click **Split selected maps**, confirm
5. Read the result: SubWorlds scanned / modified, groups removed, WorldParts updated, ObjectReferences added, members skipped
6. **Save** the Frosty project, then export / verify in-game

If no game data is loaded, the plugin shows `No game data loaded.`

## Which maps appear

Only levels whose path contains `MP` / `SP` and whose folder name looks like `MP_*` / `SP_*` (or `MP`/`SP` followed by a letter). Example: `Levels/MP/MP_ItalianCoast`. The filter matches both the display name and the path key.

## Notes

- This is a destructive EBX edit. Back up your Frosty project first.
- After a split, spot-check in the editor: the original group is gone, objects sit on the matching WorldPart, transforms match the pre-split pose.
- If Havok `.hkx` / compound reads fail, those members may be skipped and the group is left in place.
