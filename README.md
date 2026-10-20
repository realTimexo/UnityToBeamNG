# Unity to BeamNG

Build your map in Unity, click **EXPORT TO BEAMNG**, get an installable BeamNG.drive mod ZIP.

> **Status: alpha (v0.2.2).** Meshes, materials, spawn point and collision have been confirmed working in BeamNG.drive 0.40. The terrain export is implemented but **not yet verified in game**. See [Known limitations](#known-limitations).

> **This project is vibe coded.** It was written together with an AI assistant and tested by hand. Expect rough edges, and please [report bugs](../../issues/new/choose) with the files listed below - that is how it gets better.

## Features

- One-click export of the open Unity scene to a BeamNG level ZIP (`levels/<name>/...`)
- **Terrain** -> BeamNG `.ter` (version 9), multiple Unity terrains are merged, hole masks are kept, terrain layers become terrain materials
- **Static meshes** -> COLLADA `.dae` (reused meshes are written once), placed as `TSStatic` objects with collision
- **Materials and textures** -> `main.materials.json` + PNG textures (base color, metallic, roughness, tiling/offset baked into the UVs)
- **Spawn point** from any empty GameObject (position and facing direction), optional ground snapping
- `info.json`, preview image, sky/time of day, level objects
- Validation report with `PASS` / `WARNING` / `ERROR` / `MANUAL` and an `export_report.txt` next to the ZIP
- Cancelable export, writes to a temp folder first and only keeps the result if validation passes
- Works with larger scenes (streamed writing, terrain up to 8192x8192)

## Requirements

- Unity 2022.2 or newer (uses `FindObjectsByType`)
- BeamNG.drive (tested with 0.40)
- Meshes need **Read/Write enabled** in their import settings

## Install

1. Download or clone this repository.
2. Copy the folder `Assets/UnityToBeamNG` into the `Assets` folder of your Unity project.
3. Wait for Unity to compile. The menu `Tools > Unity to BeamNG` appears.

## Usage

1. Build your scene (terrain and/or meshes with materials).
2. Open `Tools > Unity to BeamNG`.
3. **Map** tab: set the map name, an internal level name (`a-z`, `0-9`, `_`) and an export folder.
4. **Spawn** tab: click *Create new spawn object* (or pick any empty GameObject). Move it where you want to start and rotate it so the blue arrow points in the driving direction.
5. Click **Validate**, read the **Report** tab, then click **EXPORT TO BEAMNG**.
6. Put the resulting `<levelname>.zip` into your BeamNG mods folder (on Windows usually `%LocalAppData%\BeamNG\BeamNG.drive\current\mods`) and start the game. The map appears in the level list.

Optional per-object settings:

| Component | Menu | What it does |
|---|---|---|
| `BeamNGSpawnPoint` | `BeamNG/Spawn Point` | Marks a GameObject as a spawn (name, display name, default flag) |
| `BeamNGCollisionSettings` | `BeamNG/Collision Settings` | Collision mode per object (Auto / None / VisibleMesh / Bounds) or exclude it from the export |

These are Unity-side settings. They are **not** native BeamNG components.

## What ends up in the ZIP

```
levels/<levelname>/
  info.json
  preview.png
  main.materials.json
  theTerrain.ter, theTerrain.terrain.json      (if a terrain is exported)
  main/items.level.json                         (MissionGroup)
  main/MissionGroup/{Environment,Terrain,Buildings,Gameplay}/items.level.json
  art/shapes/*.dae, art/shapes/main.materials.json
  art/textures/*.color.png
  art/terrains/main.materials.json
```

A fallback heightmap for the BeamNG World Editor terrain importer and an export manifest are written next to the ZIP in `extras/` (not part of the mod).

## Known limitations

Not implemented yet:

- Traffic light / traffic logic, road network objects (roads are exported as plain meshes), vegetation (terrain trees/details), water, minimap
- Normal maps, emission and transparency (opaque base color only)
- Per-vertex colors, skinned meshes, particle systems

Not verified in game yet (best guesses based on the BeamNG documentation):

- The binary details of the `.ter` file that the docs do not specify (material name encoding, row order)
- Terrain material JSON keys
- Rotation matrix layout of rotated objects (fixed in 0.2.1, needs confirmation)

If something is off, the export report says so. **A successful export means the files were created and structurally validated - it does not mean the map was tested in game.**

## Reporting bugs

Use the [bug report template](../../issues/new/choose). Please attach:

1. `export_report.txt` (in your export folder, also shown in the **Report** tab)
2. `beamng.log` from right after loading the map
3. Unity console errors (if any)
4. Screenshots of the scene in Unity and in BeamNG, plus Unity / BeamNG / tool versions

## Support the project

If this tool saves you time, you can support its development:

- Buy me a coffee: https://timexo.gumroad.com/coffee
- PayPal: https://www.paypal.com/donate/?hosted_button_id=38GT8LH75W4GU

## License

Copyright (c) 2026 Timexo. All rights reserved. See [LICENSE](LICENSE).

In short: free to use for exporting your own maps (including commercial ones). You may not redistribute or sell the code. If you show the tool or maps made with it on social media or elsewhere, link to the project: **[link to site]**.

Maps you export belong to you. This project is not affiliated with BeamNG GmbH or Unity Technologies.

## Credits

Built against the public [BeamNG documentation](https://documentation.beamng.com/) (level file formats, terrain `.ter`, level objects, materials).
