# Changelog

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
