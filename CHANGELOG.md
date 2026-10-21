# Changelog

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
