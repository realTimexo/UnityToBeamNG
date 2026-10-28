using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityToBeamNG
{
    public class UnityToBeamNGWindow : EditorWindow
    {
        public const string ToolVersion = "0.4.2";
        public const string RepoUrl = "https://github.com/realTimexo/UnityToBeamNG";
        public const string ReleasesUrl = "https://github.com/realTimexo/UnityToBeamNG/releases";
        public const string IssuesUrl = "https://github.com/realTimexo/UnityToBeamNG/issues";
        const string DonateGumroad = "https://timexo.gumroad.com/coffee";
        const string DonatePayPal = "https://www.paypal.com/donate/?hosted_button_id=38GT8LH75W4GU";

        BeamNGExportSettings s;
        Vector2 scroll;
        int tab;
        static readonly string[] Tabs = { "Dashboard", "Map", "Spawn", "Roads", "Traffic", "Vegetation", "Water", "Modules", "Report", "About" };
        const int ReportTab = 8;

        [MenuItem("Tools/Unity to BeamNG")]
        static void Open() { GetWindow<UnityToBeamNGWindow>("Unity to BeamNG"); }

        void OnEnable() { s = BeamNGExportSettings.Load(); }
        void OnDisable() { if (s != null) s.Save(); }

        void OnGUI()
        {
            if (s == null) s = BeamNGExportSettings.Load();
            tab = GUILayout.Toolbar(tab, Tabs);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();
            switch (tab)
            {
                case 0: Dashboard(); break;
                case 1: MapConfig(); break;
                case 2: SpawnTab(); break;
                case 3: RoadsTab(); break;
                case 4: TrafficTab(); break;
                case 5: VegetationTab(); break;
                case 6: WaterTab(); break;
                case 7: Modules(); break;
                case 8: ReportView(); break;
                case 9: About(); break;
            }
            if (EditorGUI.EndChangeCheck()) s.Save();
            EditorGUILayout.EndScrollView();
            Buttons();
        }

        void Dashboard()
        {
            EditorGUILayout.LabelField("Project", Application.productName);
            EditorGUILayout.LabelField("Map", s.mapName);
            EditorGUILayout.LabelField("Level name (internal)", s.levelName);
            EditorGUILayout.LabelField("Author", s.author);
            EditorGUILayout.LabelField("Export folder", s.exportDirectory);
            EditorGUILayout.LabelField("Unity version", Application.unityVersion);
            EditorGUILayout.LabelField("Target BeamNG version", s.beamNGTargetVersion);
            var rep = BeamNGExportPipeline.LastReport;
            EditorGUILayout.LabelField("Detected objects (last run)", rep != null ? rep.Detected.ToString() : "-");
            EditorGUILayout.LabelField("Exported objects", rep != null ? rep.Exported.ToString() : "-");
            EditorGUILayout.LabelField("Last export", s.lastExportTime);
            EditorGUILayout.LabelField("Status", s.lastExportStatus);
            if (!string.IsNullOrEmpty(BeamNGExportPipeline.LastZipPath))
                EditorGUILayout.LabelField("Last ZIP", BeamNGExportPipeline.LastZipPath);
            EditorGUILayout.HelpBox(
                "The export creates an installable ZIP (levels/<name>/...). Files created and structure validated does NOT mean tested in game. " +
                "Not included: normal maps. See the report for details.",
                MessageType.Warning);
        }

        void MapConfig()
        {
            s.mapName = EditorGUILayout.TextField("Map name", s.mapName);
            s.levelName = EditorGUILayout.TextField("Internal level name", s.levelName);
            if (!BeamNGEditorUtilities.IsValidLevelName(s.levelName))
                EditorGUILayout.HelpBox("Only a-z, 0-9 and _ are allowed.", MessageType.Error);
            s.author = EditorGUILayout.TextField("Author", s.author);
            s.description = EditorGUILayout.TextField("Description (empty = 'Made with UnityToBeamNG!')", s.description);
            s.version = EditorGUILayout.TextField("Version", s.version);
            s.terrainResolution = EditorGUILayout.IntField("Terrain resolution (2^n, 64-8192)", s.terrainResolution);
            s.maxTextureSize = EditorGUILayout.IntField("Max texture size", s.maxTextureSize);
            s.timeOfDay = EditorGUILayout.Slider("Start time of day (0-1)", s.timeOfDay, 0f, 1f);
            s.visibleDistance = EditorGUILayout.FloatField("View distance (m)", s.visibleDistance);

            EditorGUILayout.BeginHorizontal();
            s.previewImagePath = EditorGUILayout.TextField("Own preview image (optional)", s.previewImagePath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                var pi = EditorUtility.OpenFilePanel("Preview image", s.previewImagePath, "png,jpg,jpeg");
                if (!string.IsNullOrEmpty(pi)) s.previewImagePath = pi;
            }
            if (GUILayout.Button("x", GUILayout.Width(22))) s.previewImagePath = "";
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Empty = a screenshot taken at the spawn point is used. An own image is cropped to 16:9 (1280x720).", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            s.exportDirectory = EditorGUILayout.TextField("Export folder", s.exportDirectory);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                var p = EditorUtility.OpenFolderPanel("Export folder", s.exportDirectory, "");
                if (!string.IsNullOrEmpty(p)) s.exportDirectory = p;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("BeamNG (optional)", EditorStyles.boldLabel);
            s.beamNGInstallPath = EditorGUILayout.TextField("Install path (info only)", s.beamNGInstallPath);
            s.beamNGTargetVersion = EditorGUILayout.TextField("Target version", s.beamNGTargetVersion);
            s.copyZipToModsFolder = EditorGUILayout.ToggleLeft("Copy ZIP to the mods folder after export", s.copyZipToModsFolder);
            using (new EditorGUI.DisabledScope(!s.copyZipToModsFolder))
            {
                EditorGUILayout.BeginHorizontal();
                s.beamNGModsFolder = EditorGUILayout.TextField("Mods folder", s.beamNGModsFolder);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    var p = EditorUtility.OpenFolderPanel("BeamNG mods folder", s.beamNGModsFolder, "");
                    if (!string.IsNullOrEmpty(p)) s.beamNGModsFolder = p;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.HelpBox("The mods folder is usually inside the BeamNG user folder (Windows: %LocalAppData%\\BeamNG\\BeamNG.drive\\current\\mods). Not checked by the tool.", MessageType.None);
        }

        void SpawnTab()
        {
            EditorGUILayout.HelpBox("An empty GameObject is enough: its position and facing direction (blue arrow / Z axis) become the BeamNG spawn point.", MessageType.Info);
            s.spawnObject = (Transform)EditorGUILayout.ObjectField("Spawn object (scene)", s.spawnObject, typeof(Transform), true);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create new spawn object")) CreateSpawn();
            using (new EditorGUI.DisabledScope(Selection.activeTransform == null))
                if (GUILayout.Button("Use selection as spawn")) { s.spawnObject = Selection.activeTransform; s.Save(); }
            EditorGUILayout.EndHorizontal();

            s.snapSpawnToGround = EditorGUILayout.ToggleLeft("Snap spawn to ground (raycast down)", s.snapSpawnToGround);
            using (new EditorGUI.DisabledScope(!s.snapSpawnToGround))
                s.spawnClearance = EditorGUILayout.FloatField("Height above ground (m)", s.spawnClearance);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("BeamNGSpawnPoint components in the scene", EditorStyles.boldLabel);
            var all = FindObjectsByType<BeamNGSpawnPoint>(FindObjectsSortMode.None);
            if (all.Length == 0) EditorGUILayout.LabelField("none");
            foreach (var sp in all)
                if (GUILayout.Button(sp.name + "  (" + sp.spawnName + (sp.isDefault ? ", default" : "") + ")", EditorStyles.miniButton))
                    Selection.activeGameObject = sp.gameObject;
        }

        void CreateSpawn()
        {
            var go = new GameObject("BeamNG Spawn");
            Undo.RegisterCreatedObjectUndo(go, "Create BeamNG Spawn");
            go.AddComponent<BeamNGSpawnPoint>();
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) go.transform.position = sv.pivot;
            s.spawnObject = go.transform;
            Selection.activeGameObject = go;
            s.Save();
        }

        void RoadsTab()
        {
            EditorGUILayout.HelpBox(
                "Your visible roads stay normal meshes. For AI, traffic and navigation BeamNG needs a road network: it is exported as invisible DecalRoads along your road centre lines.\n\n" +
                "1) Road Architect (or any road object): select the road object and press 'Mark selected as BeamNG road'.\n" +
                "2) Own centre line: 'Create road from points' - move the child points along your road.\n" +
                "3) Modular 3D road pieces: select a piece and press 'Add road piece': 2 connectors are created at its ends (blue arrow points OUT). " +
                "For crossings/T-junctions add more connectors and move them to the arms. Pieces whose connectors touch are linked automatically.", MessageType.Info);
            if (GUILayout.Button("Find Road Architect roads in scene (automatic)", GUILayout.Height(28)))
                EditorUtility.DisplayDialog("Roads", BeamNGRoadTools.FindRoadArchitectRoads() + " road(s) marked.", "OK");
            if (GUILayout.Button("Mark selected as BeamNG road (Road Architect / own object)", GUILayout.Height(24)))
                EditorUtility.DisplayDialog("Roads", BeamNGRoadTools.AddRoadToSelection() + " road(s) marked.", "OK");
            if (GUILayout.Button("Create road from points", GUILayout.Height(24))) BeamNGRoadTools.CreateRoadFromPoints();
            EditorGUILayout.Space();
            if (GUILayout.Button("Add road piece (selected model)", GUILayout.Height(24))) BeamNGRoadTools.AddPieceToSelection();
            if (GUILayout.Button("Add connector to selected road piece", GUILayout.Height(24))) BeamNGRoadTools.AddConnectorToSelectedPiece();
            EditorGUILayout.Space();
            s.driveOnLeft = EditorGUILayout.ToggleLeft("Left-hand traffic (info.json roadRules)", s.driveOnLeft);
            EditorGUILayout.LabelField("In scene: " + FindObjectsByType<BeamNGRoad>(FindObjectsSortMode.None).Length + " road(s), "
                + FindObjectsByType<BeamNGRoadPiece>(FindObjectsSortMode.None).Length + " piece(s)");
        }

        void TrafficTab()
        {
            EditorGUILayout.HelpBox(
                "Press the button to place a 4-way signal-controlled intersection in the scene. Move/rotate the whole object, then move each light next to the stop line of its road. " +
                "The blue arrow of every light must point in the driving direction of the traffic it controls. Lights with the same phase number are green together. " +
                "Green/yellow/all-red times are on the intersection object. The roads must be part of the road network (Roads tab).", MessageType.Info);
            if (GUILayout.Button("Create traffic intersection (with simple poles)", GUILayout.Height(28))) BeamNGRoadTools.CreateIntersection(true);
            if (GUILayout.Button("Create traffic intersection (logic only, use your own models)", GUILayout.Height(24))) BeamNGRoadTools.CreateIntersection(false);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Road Architect intersections", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Intersections built with Road Architect (traffic lights or all-way stop) are converted automatically at export: its green/yellow/red timing is taken over and the AI obeys the signals in BeamNG. Road Architect\'s lamp texture is replaced by own lamp discs at its redlight/yellowlight/greenlight positions that switch colour in BeamNG (the housing and poles stay as they are). In Unity play mode Road Architect keeps working as before; the added lamp discs just stay visible. Press the button to convert now and check/adjust the result.", MessageType.Info);
            if (GUILayout.Button("Convert Road Architect traffic lights / stop signs now", GUILayout.Height(26)))
            {
                int rl, rs; var nt = new List<string>();
                int ri = BeamNGRoadArchitectSignals.Convert(out rl, out rs, nt);
                if (ri == 0 && nt.Count == 0) nt.Add(BeamNGRoadArchitectSignals.Diagnose());
                EditorUtility.DisplayDialog("Road Architect", ri + " intersection(s): " + rl + " traffic light(s), " + rs + " stop sign(s)." + (nt.Count > 0 ? "\n\n" + string.Join("\n", nt) : ""), "OK");
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Stop signs (the AI stops)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Place the sign next to the stop line; its blue arrow must point in the driving direction of the traffic that has to stop. The simple model (pole + disc) can be deleted and replaced by your own 3D model - the logic sits on the root object. The road must be part of the road network (Roads tab).", MessageType.Info);
            if (GUILayout.Button("Create stop sign (simple model)", GUILayout.Height(26))) BeamNGRoadTools.CreateStopSign(true);
            if (GUILayout.Button("Create stop sign (logic only, use your own model)", GUILayout.Height(24))) BeamNGRoadTools.CreateStopSign(false);
            EditorGUILayout.LabelField("In scene: " + FindObjectsByType<BeamNGTrafficSign>(FindObjectsSortMode.None).Length + " sign(s)");
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Logic (who is green when, AI stops at red) goes to signals.json. The lamps (red/yellow/green objects assigned on each light) are switched in game by a small Lua extension that ships inside the ZIP. For your own models assign the three lamp objects (emissive materials) on the Traffic Light component.", MessageType.Warning);
            EditorGUILayout.LabelField("In scene: " + FindObjectsByType<BeamNGTrafficIntersection>(FindObjectsSortMode.None).Length + " intersection(s), "
                + FindObjectsByType<BeamNGTrafficLight>(FindObjectsSortMode.None).Length + " light(s)");
        }

        void WaterTab()
        {
            EditorGUILayout.HelpBox("Creates a water volume: the object's X/Z scale is the footprint, its height (Y) is the water surface and 'depth' reaches downwards. The blue quad is only a preview and is not exported. Select the water object to change type (lake/ocean/river/pool/stream) and depth.", MessageType.Info);
            if (GUILayout.Button("Create water", GUILayout.Height(28))) BeamNGRoadTools.CreateWater();
            EditorGUILayout.LabelField("In scene: " + FindObjectsByType<BeamNGWater>(FindObjectsSortMode.None).Length + " water volume(s)");
            EditorGUILayout.Space();
            s.autoDetectWater = EditorGUILayout.ToggleLeft("Also export water that already exists in my scene", s.autoDetectWater);
            EditorGUILayout.HelpBox("Flat, large meshes (at least 4x4 m, thinner than 1 m) whose material or shader name contains 'water', and HDRP WaterSurface objects become BeamNG water automatically (surface height = top of the object, depth 10 m, type guessed from the name: ocean/river/pool/stream, otherwise lake). Such a mesh is then not exported as a normal mesh. Turn this off if a normal object is mistaken for water.", MessageType.None);
        }

        void VegetationTab()
        {
            EditorGUILayout.HelpBox(
                "Trees from 'Paint Trees' and grass / meshes from 'Paint Details' on your Unity Terrain are exported as BeamNG forest items " +
                "(forest/*.forest4.json + art/forest/managedItemData.json + a Forest object). Tree prefabs need Read/Write enabled on their meshes.", MessageType.Info);
            int trees = 0, protos = 0, dprotos = 0, terrains = 0;
            foreach (var t in FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                if (!t.gameObject.activeInHierarchy || t.terrainData == null) continue;
                terrains++; trees += t.terrainData.treeInstanceCount; protos += t.terrainData.treePrototypes.Length; dprotos += t.terrainData.detailPrototypes.Length;
            }
            EditorGUILayout.LabelField("In scene", terrains + " terrain(s), " + trees.ToString("N0") + " tree(s) of " + protos + " prototype(s), " + dprotos + " detail prototype(s)");

            s.exportVegetation = EditorGUILayout.ToggleLeft("Export vegetation (trees + details)", s.exportVegetation);
            using (new EditorGUI.DisabledScope(!s.exportVegetation))
            {
                EditorGUILayout.LabelField("Trees", EditorStyles.boldLabel);
                s.treeCollision = EditorGUILayout.ToggleLeft("Trees collide (trunk collision shape)", s.treeCollision);
                using (new EditorGUI.DisabledScope(!s.treeCollision))
                    s.minCollidableTreeHeight = EditorGUILayout.FloatField("No collision below height (m)", s.minCollidableTreeHeight);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Details (Paint Details)", EditorStyles.boldLabel);
                s.exportDetails = EditorGUILayout.ToggleLeft("Export details (grass, detail meshes)", s.exportDetails);
                using (new EditorGUI.DisabledScope(!s.exportDetails))
                {
                    s.detailDensity = EditorGUILayout.Slider("Density (share of Unity's instances)", s.detailDensity, 0.05f, 1f);
                    s.maxDetailInstances = EditorGUILayout.IntField("Max detail instances (budget)", s.maxDetailInstances);
                    s.detailMeshCollision = EditorGUILayout.ToggleLeft("Detail meshes (rocks etc.) collide (bounding box). Grass never collides.", s.detailMeshCollision);
                }
            }
            if (GUILayout.Button("Enable Read/Write on tree & detail models (fixes 'not readable')", GUILayout.Height(24)))
                EditorUtility.DisplayDialog("Vegetation", EnableReadWriteOnVegetation() + " model(s) changed and re-imported.", "OK");
            using (new EditorGUI.DisabledScope(!s.exportVegetation))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Distance & wind", EditorStyles.boldLabel);
                s.forestLods = EditorGUILayout.ToggleLeft("LODs: automatic billboard for far trees, far culling of grass/details", s.forestLods);
                s.vegetationWind = EditorGUILayout.ToggleLeft("Wind sway (vertex colours + wind emitter)", s.vegetationWind);
                using (new EditorGUI.DisabledScope(!s.vegetationWind))
                    s.windStrength = EditorGUILayout.Slider("Wind strength (windScale)", s.windStrength, 0.1f, 2f);
            }
            EditorGUILayout.HelpBox("Forest items support one uniform scale (tree width/height scale is averaged). Wind sway is untested in game. The tree list with collision info is in the Report tab.", MessageType.Warning);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Minimap", EditorStyles.boldLabel);
            s.exportMinimap = EditorGUILayout.ToggleLeft("Create minimap (terrain colours, roads, buildings, trees)", s.exportMinimap);
            using (new EditorGUI.DisabledScope(!s.exportMinimap))
                s.minimapPixels = EditorGUILayout.IntPopup("Minimap resolution", s.minimapPixels, new[] { "Auto (terrain size, max 2048)", "512", "1024", "2048", "4096" }, new[] { 0, 512, 1024, 2048, 4096 });
        }

        static int EnableReadWriteOnVegetation()
        {
            var paths = new System.Collections.Generic.HashSet<string>();
            System.Action<GameObject> collect = go =>
            {
                if (go == null) return;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && !mf.sharedMesh.isReadable)
                    {
                        var path = AssetDatabase.GetAssetPath(mf.sharedMesh);
                        if (!string.IsNullOrEmpty(path)) paths.Add(path);
                    }
            };
            foreach (var t in FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                if (t.terrainData == null) continue;
                foreach (var tp in t.terrainData.treePrototypes) collect(tp.prefab);
                foreach (var dp in t.terrainData.detailPrototypes) if (dp.usePrototypeMesh) collect(dp.prototype);
            }
            int n = 0;
            foreach (var p in paths)
            {
                var mi = AssetImporter.GetAtPath(p) as ModelImporter;
                if (mi != null && !mi.isReadable) { mi.isReadable = true; mi.SaveAndReimport(); n++; }
            }
            return n;
        }

        void Modules()
        {
            s.exportTerrain = EditorGUILayout.ToggleLeft("Terrain (.ter + terrain materials)", s.exportTerrain);
            s.exportMeshes = EditorGUILayout.ToggleLeft("Static meshes (.dae, TSStatic) incl. materials/textures", s.exportMeshes);
            s.terrainPbr = EditorGUILayout.ToggleLeft("PBR terrain textures (real Unity terrain textures in BeamNG; switch off if BeamNG crashes on load)", s.terrainPbr);
            s.collideAllByDefault = EditorGUILayout.ToggleLeft("All meshes collide (otherwise only with a collider)", s.collideAllByDefault);
            EditorGUILayout.HelpBox("Can be overridden per object with the 'BeamNG/Collision Settings' component (collision: Auto/None/VisibleMesh/Bounds, exclude object).", MessageType.None);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Not implemented yet", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                string[] todo = { "Yield signs, pedestrian crossings", "Normal maps", "Bridges/tunnels as dedicated objects" };
                foreach (var t in todo) EditorGUILayout.ToggleLeft(t, false);
            }
        }

        void ReportView()
        {
            var r = BeamNGExportPipeline.LastReport;
            if (r == null) { EditorGUILayout.LabelField("No report yet."); return; }
            EditorGUILayout.LabelField($"Detected {r.Detected} | Exported {r.Exported} | Skipped {r.Skipped}");
            foreach (var e in r.Entries)
            {
                var type = e.Status == ReportStatus.ERROR ? MessageType.Error
                    : e.Status == ReportStatus.PASS ? MessageType.Info : MessageType.Warning;
                EditorGUILayout.HelpBox($"[{e.Status}] {e.Category}: {e.Message}" + (string.IsNullOrEmpty(e.Hint) ? "" : "\n-> " + e.Hint), type);
            }
        }

        void About()
        {
            EditorGUILayout.LabelField("Unity to BeamNG", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Version " + ToolVersion + " (alpha)");
            EditorGUILayout.HelpBox("This project is vibe coded with AI assistance. Expect rough edges and please report bugs with the export report and the BeamNG log attached.", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Download latest release (GitHub)")) Application.OpenURL(ReleasesUrl);
            if (GUILayout.Button("Report an issue / bug")) Application.OpenURL(IssuesUrl);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Like it? Support the project:");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Buy me a coffee (Gumroad)")) Application.OpenURL(DonateGumroad);
            if (GUILayout.Button("Donate via PayPal")) Application.OpenURL(DonatePayPal);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("If you show this tool or maps made with it on social media, please link to [link to site].", MessageType.None);
        }

        void Buttons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate", GUILayout.Height(28)))
            { BeamNGExportPipeline.Validate(s); tab = ReportTab; }
            var old = GUI.backgroundColor; GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
            if (GUILayout.Button("EXPORT TO BEAMNG", GUILayout.Height(28)))
            { BeamNGExportPipeline.Export(s); tab = ReportTab; }
            GUI.backgroundColor = old;
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Report an issue / bug on GitHub")) Application.OpenURL(IssuesUrl);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open export folder"))
            {
                var p = BeamNGExportPipeline.LastZipPath;
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) p = s.exportDirectory;
                if (!string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p))) EditorUtility.RevealInFinder(p);
            }
            if (GUILayout.Button("Show report")) tab = ReportTab;
            EditorGUILayout.EndHorizontal();
        }
    }
}
