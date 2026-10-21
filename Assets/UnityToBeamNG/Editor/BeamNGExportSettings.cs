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
        public string exportDirectory = "";

        // --- BeamNG ---
        public string beamNGInstallPath = "";        // optional, informational only
        public string beamNGModsFolder = "";         // optional target for "copy ZIP"
        public bool copyZipToModsFolder = false;
        public string beamNGTargetVersion = "BeamNG.drive 0.40 (meshes/spawn tested, terrain untested)";

        // --- Terrain ---
        public int terrainResolution = 1024;         // power of two, 64..8192
        public int maxTextureSize = 2048;

        // --- Modules ---
        public bool exportTerrain = true;
        public bool exportMeshes = true;
        public bool collideAllByDefault = true;      // meshes without a collider still collide

        // --- Spawn ---
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
