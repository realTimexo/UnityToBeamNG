# Changelog

## 0.4.0 (alpha)
- **New: vegetation.** Trees painted with Unity's *Paint Trees* and grass / meshes painted with *Paint Details* on a Unity Terrain are exported as BeamNG forest items: `art/forest/managedItemData.json` (type `TSForestItemData`), `forest/<type>.forest4.json` (one JSON object per line: `type`, `pos`, `rotationMatrix`, `scale`) and a `Forest` object (`theForest`) in a new `Vegetation` group. Format taken from the BeamNG docs (Forest Data / Forest class).
  - Tree prefabs (LOD0 only, billboards skipped) are merged into one DAE per prefab; materials/textures go through the normal material export. Leaf/grass materials are exported double-sided with alpha test.
  - **Collision:** trees get `"collidable": true` and a convex trunk collision shape written as an extra `Colmesh-1` node inside the DAE (radius from the prefab's `CapsuleCollider` or estimated from the trunk; bushes below a configurable height get none). Detail meshes (rocks...) can optionally collide with a bounding box. Grass never collides.
  - **Details:** grass textures become three crossed quads (tinted with Unity's healthy/dry colour), detail meshes use their prefab. Density slider + hard instance budget (the density is lowered automatically when the budget would be exceeded). Positions come from Unity's detail layer; heights from the terrain.
  - Limits (reported in the export report): forest items only have ONE uniform scale (tree width/height scale is averaged), no wind sway (`windScale` 0), no distance LOD / billboards.
- **New: minimap.** `minimap/terrain.png` is rendered on the CPU (terrain layer colours + hillshade, roads, building footprints, trees) and registered in `info.json` as `"minimap":[{"file","size","offset"}]` (docs). Resolution 512-4096.
- New "Vegetation" tab (trees, details, minimap options). Structure check now also verifies forest files (every placement type is defined, every shape exists, minimap file exists).
- Unit tests for forest line format, collision orientation and foliage detection.

## 0.3.2 (alpha)
- Transparency: opacity maps are now named `*_o.data.png` (BeamNG's documented naming; `.dataR.png` was not honored), `alphaRef` is an integer, and the color PNG background is filled with the sign's own colors (no green/black fringe even if the opacity map is ignored).
- Fix: objects with negative scale (e.g. -21.5 on all axes, the Object_13 house) were invisible in BeamNG. The mirroring is now baked into the DAE (vertices, normals, winding) and the TSStatic gets the absolute scale.
- Fix (from beamng.log): meshes whose name ends in a number (`Cylinder_001`, `Object_7` ...) were rejected by BeamNG with "Shape with invalid detail levels" (Torque reads a trailing number as detail-level size) - this made stop sign, traffic lights and other models invisible. DAE node names now never end in a digit.
- Fix (from beamng.log): materials named only with digits (`1`, `3`, `8`, typical for FBX buildings) failed with "Object name cannot start with a digit" / "Unable to find material" -> houses without textures. Names starting with a digit now get the prefix `n_`.
- Fix: traffic-light lamp Lua extension crashed every frame (`green` was both a duration and the lamp list). Durations renamed to `greenSec/yellowSec/allRedSec`. modScript now uses the documented `load()` / `setExtensionUnloadMode()`.
- Meshes with more submeshes than materials use the last material (like Unity) instead of the grey missing_material.
- Fix: structure check compared the DAE material id (`<name>-material`) with main.materials.json and aborted the export; it now compares the material name.
- Fix: transparent materials had a black background in BeamNG. BeamNG's texture cooker drops the alpha channel of `*.color.png`. Textures with alpha are now split: opaque color PNG (colors bled into transparent areas) + separate grayscale `*.opacity.dataR.png` written as `opacityMap`. Sign-like (binary alpha) textures use alpha test, soft ones (markings, glass) use translucent + Z-write. `doubleSided` is exported for materials with Render Face = Both (`_Cull` = 0).
- Fix: DAE ids - material ids are now `<name>-material` so they can never collide with the mesh/node id (a model whose mesh and material share a name, typical for Blender exports, could make BeamNG drop the object).
- New: `SkinnedMeshRenderer` (FBX/Blender models with an armature or shape keys) are baked in their current pose and exported as static meshes instead of being skipped.
- Report: lists every renderer that was NOT exported (disabled / LOD1+ ...), plus a note for materials set to a transparent render mode without alpha in the texture.
- README: how to set up intersections, signals and AI traffic.

## 0.3.1 (alpha)
- Road Architect: detection via its `SplineN` node objects (works from Road1, the Spline or MainMeshes object) + spline sampling; diagnostic list of components when nothing is found.
- Road markings: transparent/cutout materials are exported (translucent / alphaTest); emission exported as emissiveMap/emissiveFactor.
- Traffic lights: poles with three emissive lamps, `signal_lamps.json` + Lua extension (`scripts/unity_to_beamng/modScript.lua`, `lua/ge/extensions/unityToBeamNG/lights.lua`) switch the lamps; ZIP now also contains `scripts/` and `lua/`.
- Tool window: "Report an issue / bug on GitHub" button. README: download link.
## 0.3.0 (alpha)
- Terrain textures: classic terrain materials with `diffuseMap`/`diffuseSize` per Unity layer. The PBR `TerrainMaterialTextureSet` mode is EXPERIMENTAL and off by default (BeamNG crashed with D3D12 'root cbv' when the set was not found).
- Roads: Road Architect detection searches parents/children; new 'Find Road Architect roads in scene' button; fixed roads with a single point.
- Road network: `BeamNGRoad` (child points or Road Architect) and `BeamNGRoadPiece` (modular 3D pieces with connectors) are exported as invisible DecalRoads; endpoints within 0.75 m are snapped together.
- Traffic lights: `BeamNGTrafficIntersection` + `BeamNGTrafficLight`, one-button 4-way intersection, `signals.json`.
- New tabs: Roads, Traffic. New option: left-hand traffic.

## 0.2.2 (alpha)
- Fix: solid-color materials (e.g. a plain red Unity material) stayed white in BeamNG. BeamNG's TextureCooker rejects textures smaller than 16x16 ("Size must be a power of two and at least 16x16"). Solid color textures are now 16x16, and all exported textures are at least 16x16.
- Collision mode now written as `Visible Mesh Final` (BeamNG logged a performance warning for `Visible Mesh`).
- Removed the `CloudLayer` object (it logged "Missing texture"); set `globalEnviromentMap` on `LevelInfo` (BeamNG logged it as missing).
- Confirmed in game: image textures now display correctly.

## 0.2.1 (alpha)
- Renamed the project to **Unity to BeamNG** (menu: `Tools > Unity to BeamNG`, folder `Assets/UnityToBeamNG`, namespace `UnityToBeamNG`).
- Whole tool (UI, report, messages, comments) translated to English.
- Fix: materials were ignored by BeamNG (array values in `baseColorMap`, `metallicFactor`, `roughnessFactor`). Now written as plain string/number values.
- `main.materials.json` is now also written at the level root; textures are exported with power-of-two sizes.
- Fix: object and spawn rotation was inverted in BeamNG (matrix layout is now transposed). Needs one more in-game confirmation.
- New "About" tab, README, license, issue templates.

## 0.2.0 (alpha)
- First version that exports an installable BeamNG mod ZIP: terrain (`.ter`), meshes (`.dae`), materials/textures, spawn points, `info.json`, preview.
- Verified once in BeamNG 0.40: the map loads, the spawn works and meshes collide. Terrain export is still untested in game.
