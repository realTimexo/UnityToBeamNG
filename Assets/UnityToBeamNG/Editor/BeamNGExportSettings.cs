using System;
using UnityEditor;
using UnityEngine;

namespace UnityToBeamNG
{
    [Serializable]
    public class BeamNGExportSettings
    {
        const string PrefsKey = "UnityToBeamNG.Settings.v1";

        // --- Map ---
        public string mapName = "My Map";
        public string levelName = "my_map";          // internal name: only a-z, 0-9, _
        public string author = "";
        public string description = "";
        public string version = "1.0";
        public string previewImagePath = "";         // optional own preview image for the level list (otherwise a screenshot of the spawn is rendered)
        public string exportDirectory = "";

        // --- BeamNG ---
        public string beamNGInstallPath = "";        // optional, informational only
        public string beamNGModsFolder = "";         // optional target for "copy ZIP"
        public bool copyZipToModsFolder = false;
        public string beamNGTargetVersion = "BeamNG.drive 0.40";

        // --- Terrain ---
        public int terrainResolution = 1024;         // power of two, 64..8192
        public int maxTextureSize = 2048;

        // --- Modules ---
        public bool exportTerrain = true;
        public bool exportMeshes = true;
        public bool collideAllByDefault = true;      // meshes without a collider still collide
        public bool exportMinimap = true;            // top-down minimap image + info.json "minimap" entry
        public int minimapPixels = 0;                // 0 = auto (terrain resolution, max 2048); else 512..4096 (pixels, square)

        // --- Vegetation (v0.4.0) ---
        public bool autoDetectWater = true;           // flat meshes with a water material / HDRP WaterSurface -> BeamNG water
        public bool exportVegetation = true;         // Unity terrain trees (Paint Trees) + details (Paint Details) -> BeamNG forest items
        public bool treeCollision = true;            // trees get a trunk collision shape (Colmesh-1) + collidable = true
        public float minCollidableTreeHeight = 2.5f; // trees lower than this (bushes) get no collision
        public bool exportDetails = true;            // grass / detail meshes from Paint Details
        public float detailDensity = 0.5f;           // 0.05..1: share of Unity's detail instances that is exported
        public int maxDetailInstances = 200000;      // hard budget; density is reduced automatically if exceeded
        public bool forestLods = true;               // forest shapes: LOD naming (_a<px>), automatic billboard, null detail (far culling)
        public bool vegetationWind = true;           // vertex colours (wind weights) + windScale > 0 + ForestWindEmitter
        public float windStrength = 0.6f;            // windScale of trees / grass
        public bool detailMeshCollision = false;     // collision (bounding box) for detail MESHES (rocks etc.); grass never collides

        // --- Spawn ---
        public bool terrainPbr = true;               // PBR v1.5 terrain materials + TerrainMaterialTextureSet (real terrain textures). Off = classic v1 (BeamNG may show only an average colour)
        public bool driveOnLeft = false;             // left-hand traffic (info.json roadRules.rightHandDrive)
        public bool snapSpawnToGround = true;
        public float spawnClearance = 0.5f;          // meters above the ground that was found
        public string spawnObjectGid = "";           // persisted reference to the spawn GameObject
        [NonSerialized] public Transform spawnObject;

        // --- Environment ---
        public float timeOfDay = 0.13f;
        public float visibleDistance = 3000f;

        public string lastExportTime = "";
        public string lastExportStatus = "no export yet";

        [NonSerialized] public bool persist = true;

        public static BeamNGExportSettings Load()
        {
            BeamNGExportSettings s = null;
            var json = EditorPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { s = JsonUtility.FromJson<BeamNGExportSettings>(json); } catch { s = null; }
            }
            if (s == null) s = new BeamNGExportSettings();
            s.ResolveSpawnObject();
            return s;
        }

        public void Save()
        {
            if (!persist) return;
            spawnObjectGid = spawnObject != null ? GlobalObjectId.GetGlobalObjectIdSlow(spawnObject.gameObject).ToString() : "";
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
        }

        public void ResolveSpawnObject()
        {
            spawnObject = null;
            if (string.IsNullOrEmpty(spawnObjectGid)) return;
            GlobalObjectId id;
            if (!GlobalObjectId.TryParse(spawnObjectGid, out id)) return;
            var go = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
            if (go != null) spawnObject = go.transform;
        }
    }
}
