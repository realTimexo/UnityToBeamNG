# Unity to BeamNG

Build your map in Unity, click **EXPORT TO BEAMNG**, get an installable BeamNG.drive mod ZIP.

> **Status: alpha (v0.4.0).** Meshes, materials, spawn point, collision and terrain height have been confirmed working in BeamNG.drive 0.40. Terrain textures, the road network (AI), traffic lights and (new in 0.4.0) **vegetation and the minimap** are **not yet verified in game**. See [Known limitations](#known-limitations).

> **This project is vibe coded.** It was written together with an AI assistant and tested by hand. Expect rough edges, and please [report bugs](https://github.com/realTimexo/UnityToBeamNG/issues/new/choose) with the files listed below - that is how it gets better.

## Features

- One-click export of the open Unity scene to a BeamNG level ZIP (`levels/<name>/...`)
- **Terrain** -> BeamNG `.ter` (version 9), multiple Unity terrains are merged, hole masks are kept, terrain layers become terrain materials
- **Vegetation (new in 0.4.0)** -> trees from *Paint Trees* and grass/meshes from *Paint Details* become BeamNG forest items, trees with trunk collision
- **Minimap (new in 0.4.0)** -> `minimap/terrain.png` + `info.json` entry
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

1. Download the latest release ZIP from the [Releases page](https://github.com/realTimexo/UnityToBeamNG/releases) (or clone [the repository](https://github.com/realTimexo/UnityToBeamNG)).
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
  art/forest/managedItemData.json, forest/*.forest4.json   (if the terrain has trees/details)
  main/MissionGroup/Vegetation/items.level.json            (Forest object)
  minimap/terrain.png
```

A fallback heightmap for the BeamNG World Editor terrain importer and an export manifest are written next to the ZIP in `extras/` (not part of the mod).

## Known limitations

Not implemented yet:

- Water, tree LODs/billboards and wind sway, stop/yield sign logic
- Normal maps, emission and transparency (opaque base color only)
- Per-vertex colors, skinned meshes, particle systems

Not verified in game yet (best guesses based on the BeamNG documentation):

- The binary details of the `.ter` file that the docs do not specify (material name encoding, row order)
- Terrain material JSON keys
- Rotation matrix layout of rotated objects (fixed in 0.2.1, needs confirmation)

If something is off, the export report says so. **A successful export means the files were created and structurally validated - it does not mean the map was tested in game.**

## Vegetation and minimap (v0.4.0, untested in game)

Open **Tools > Unity to BeamNG > Vegetation**.

- Paint trees with *Paint Trees* and grass / rocks with *Paint Details* on your Unity Terrain, then export as usual. Tree prefabs need **Read/Write** enabled on their meshes (the tab has a button that does this for you).
- **Trees collide** (trunk shape, `Colmesh-1` node in the DAE). Trees lower than 1.5 m (bushes) do not. Detail meshes can collide via a checkbox; grass never does.
- **Details:** set the density and the instance budget. Millions of grass blades would make BeamNG slow, so the tool lowers the density automatically when the budget is exceeded (see the report).
- Forest items have only one uniform scale, so tree width/height scale is averaged. No wind and no far LODs yet.
- **Minimap:** created automatically (terrain colours, roads, buildings, trees) and added to `info.json`. If it is shifted or flipped in game, send a screenshot.

## Reporting bugs

Use the [bug report template](https://github.com/realTimexo/UnityToBeamNG/issues/new/choose) (or the **Report an issue / bug on GitHub** button in the tool window). Please attach:

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


## Download

Grab the newest ZIP from the **[Releases page](https://github.com/realTimexo/UnityToBeamNG/releases)** and unpack it into your Unity project (it contains `Assets/UnityToBeamNG`). Found a problem? Use the **Report an issue** button in the tool window or [open an issue](https://github.com/realTimexo/UnityToBeamNG/issues).

## Roads, AI and traffic lights (v0.3.2, untested in game)

Open **Tools > Unity to BeamNG**, tabs **Roads** and **Traffic**.

- **Road network:** your visible roads stay normal meshes. The tool exports invisible `DecalRoad` objects (`road_invisible`) along the centre lines, which BeamNG uses for AI, traffic and navigation.
  - *Road Architect / any road object:* select it, press "Mark selected as BeamNG road" (the centre line is read from the Road Architect spline via reflection; if that fails, use child points).
  - *Own centre line:* "Create road from points".
  - *Modular 3D pieces:* "Add road piece" creates connectors at both ends; add more connectors for T-junctions and crossings. Touching connectors are joined automatically.
- **Traffic lights:** "Create traffic intersection" places 4 lights (2 phases) with pole, head and three emissive lamps. Timings (green/yellow/all-red) are on the intersection object. Logic is exported to `signals.json` (controller type `lightsBasic`); the red/yellow/green lamp objects are switched in game by a small Lua extension shipped in the ZIP (`scripts/` and `lua/`). For your own models assign the lamp objects on the Traffic Light component.
- **Road markings / emission:** transparent materials (road markings) are exported with alpha blending, emissive materials with an emissive map.
- **Lamps:** the generated intersection has a pole with red/yellow/green lamp objects (emissive). A small Lua extension shipped in the mod (`lua/ge/extensions/unityToBeamNG/lights.lua`, started by `scripts/unity_to_beamng/modScript.lua`) shows only the lamp of the current signal state. Use your own models by assigning the lamp objects on each `BeamNGTrafficLight`.
- **Road Architect:** the centre line is read from the road's `SplineN` node objects (and the spline itself, if available). Mark the `Road1` object (or press "Find Road Architect roads in scene").
- **Road markings:** textures with alpha on marking-like materials are exported with alpha blending.


## Guide: roads, intersections, traffic lights, AI traffic

1. **Road network (needed for AI).** Your visible road stays a normal mesh. For every road press *Roads > Find Road Architect roads in scene* (or select the road and *Mark selected as BeamNG road*). Every road becomes an invisible DecalRoad (the AI navigation graph).
2. **Intersections.** Roads that end at the same point (within 0.75 m) are joined automatically. At a 4-way crossing let all arms end in the middle of the crossing; for a T-junction end the side road exactly on a point of the main road (split the main road there so both halves also end at that point).
3. **Traffic lights.** *Traffic > Create traffic intersection*: move the whole object to the crossing, then move each light next to the stop line of its arm. The blue arrow of a light must point in the driving direction of the cars it controls. Lights with the same *phase* number are green together (0 and 1 alternate). Times are on the intersection object.
4. **Own models for lights.** Disable the simple poles and assign the red/yellow/green lamp objects on each *BeamNG/Traffic Light* (emissive materials).
5. **AI traffic does not move?** Spawn only a few cars (3-5) and not all at one spot; make sure the roads form one connected network (no gaps > 0.75 m) and are not tiny pieces; check in the World Editor (F11) that the DecalRoads exist under `Roads`.
6. **Transparent signs.** Use a PNG with a transparent background and set the Unity material to Transparent/Cutout. The tool exports the alpha as a separate opacity map.
