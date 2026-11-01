using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityToBeamNG
{
    /// <summary>Editor helpers behind the Roads / Traffic tabs.</summary>
    public static class BeamNGRoadTools
    {
        static Vector3 Pivot()
        {
            var sv = SceneView.lastActiveSceneView;
            return sv != null ? sv.pivot : Vector3.zero;
        }

        public static int AddRoadToSelection()
        {
            int added = 0;
            foreach (var go in Selection.gameObjects)
            {
                // prefer the Road Architect road object (this object, a parent or a child)
                GameObject target = null;
                foreach (var mb in go.GetComponents<MonoBehaviour>()) if (BeamNGRoadExporter.IsRoadArchitectRoad(mb)) { target = go; break; }
                if (target == null) foreach (var mb in go.GetComponentsInParent<MonoBehaviour>(true)) if (BeamNGRoadExporter.IsRoadArchitectRoad(mb)) { target = mb.gameObject; break; }
                if (target == null) foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (BeamNGRoadExporter.IsRoadArchitectRoad(mb)) { target = mb.gameObject; break; }
                bool ra = target != null;
                if (target == null) target = go;
                if (target.GetComponent<BeamNGRoad>() != null) continue;
                var road = Undo.AddComponent<BeamNGRoad>(target);
                if (ra) road.source = BeamNGRoadSource.RoadArchitect;
                added++;
            }
            return added;
        }

        /// <summary>Adds a BeamNGRoad (Road Architect source) to every Road Architect road in the open scene.</summary>
        public static int FindRoadArchitectRoads()
        {
            int added = 0;
            // remove marks that older versions put on Road Architect spline nodes (they are not roads)
            foreach (var br in Object.FindObjectsByType<BeamNGRoad>(FindObjectsSortMode.None))
                foreach (var mb0 in br.GetComponents<MonoBehaviour>())
                    if (BeamNGRoadExporter.IsRaInternal(mb0)) { Object.DestroyImmediate(br); break; }
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb == null || mb is BeamNGRoad || mb is BeamNGRoadPiece) continue;
                if (!BeamNGRoadExporter.IsRoadArchitectRoad(mb)) continue;
                if (mb.GetComponent<BeamNGRoad>() != null) continue;
                var road = Undo.AddComponent<BeamNGRoad>(mb.gameObject);
                road.source = BeamNGRoadSource.RoadArchitect;
                added++;
            }
            return added;
        }

        public static void CreateRoadFromPoints()
        {
            var go = new GameObject("BeamNG Road");
            Undo.RegisterCreatedObjectUndo(go, "Create BeamNG Road");
            go.transform.position = Pivot();
            go.AddComponent<BeamNGRoad>();
            for (int i = 0; i < 3; i++)
            {
                var p = new GameObject("Point " + i);
                p.transform.SetParent(go.transform, false);
                p.transform.localPosition = new Vector3(0f, 0f, i * 15f);
            }
            Selection.activeGameObject = go;
        }

        /// <summary>Adds a BeamNGRoadPiece with two connectors at the ends of the longer horizontal axis of the selected object.</summary>
        public static void AddPieceToSelection()
        {
            foreach (var go in Selection.gameObjects)
            {
                if (go.GetComponent<BeamNGRoadPiece>() != null) continue;
                var piece = Undo.AddComponent<BeamNGRoadPiece>(go);
                var b = LocalBounds(go.transform);
                var c = b.center; var e = b.extents;
                bool alongX = e.x > e.z;
                float top = b.max.y;
                Vector3 axis = alongX ? Vector3.right : Vector3.forward;
                float half = alongX ? e.x : e.z;
                piece.width = Mathf.Max(2f, (alongX ? e.z : e.x) * 2f);
                var centerTop = new Vector3(c.x, top, c.z);
                AddConnector(piece, centerTop + axis * half, go.transform.TransformDirection(axis));
                AddConnector(piece, centerTop - axis * half, go.transform.TransformDirection(-axis));
            }
        }

        public static void AddConnectorToSelectedPiece()
        {
            var go = Selection.activeGameObject;
            var piece = go != null ? go.GetComponentInParent<BeamNGRoadPiece>() : null;
            if (piece == null) return;
            var pos = piece.transform.InverseTransformPoint(piece.transform.position) + new Vector3(0f, 0.2f, 5f);
            AddConnector(piece, pos, piece.transform.forward);
        }

        static void AddConnector(BeamNGRoadPiece piece, Vector3 localPos, Vector3 worldOutDir)
        {
            var c = new GameObject("Connector " + piece.connectors.Count);
            Undo.RegisterCreatedObjectUndo(c, "Add connector");
            c.transform.SetParent(piece.transform, false);
            c.transform.localPosition = localPos;
            c.transform.rotation = Quaternion.LookRotation(worldOutDir);
            Undo.RecordObject(piece, "Add connector");
            piece.connectors.Add(c.transform);
            EditorUtility.SetDirty(piece);
        }

        static Bounds LocalBounds(Transform root)
        {
            bool init = false; var b = new Bounds(Vector3.zero, Vector3.one);
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var l = root.InverseTransformPoint(corner);
                    if (!init) { b = new Bounds(l, Vector3.zero); init = true; } else b.Encapsulate(l);
                }
            }
            return b;
        }

        /// <summary>4-way signal-controlled intersection: 4 lights (phase 0/1), simple visual poles.</summary>
        public static void CreateIntersection(bool withVisuals)
        {
            var root = new GameObject("BeamNG Traffic Intersection");
            Undo.RegisterCreatedObjectUndo(root, "Create traffic intersection");
            root.transform.position = Pivot();
            root.AddComponent<BeamNGTrafficIntersection>();
            for (int i = 0; i < 4; i++)
            {
                var dir = Quaternion.Euler(0f, 90f * i, 0f) * Vector3.forward;       // driving direction towards the centre
                var right = Vector3.Cross(Vector3.up, dir);
                var go = new GameObject("Light " + (i + 1));
                go.transform.SetParent(root.transform, false);
                go.transform.position = root.transform.position - dir * 12f + right * 5f;
                go.transform.rotation = Quaternion.LookRotation(dir);
                var tl = go.AddComponent<BeamNGTrafficLight>();
                tl.phase = i % 2;
                if (withVisuals) AddPole(go.transform);
            }
            Selection.activeGameObject = root;
        }

        /// <summary>Simple stop sign (pole + red disc + white disc). Replace the child objects with your own model if you like; the logic sits on the root.</summary>
        public static void CreateStopSign(bool withVisuals)
        {
            var root = new GameObject("BeamNG Stop Sign");
            Undo.RegisterCreatedObjectUndo(root, "Create stop sign");
            root.transform.position = Pivot();
            root.AddComponent<BeamNGTrafficSign>();
            if (withVisuals)
            {
                var pole = LampMaterial("BeamNG_Signal_Dark", new Color(0.08f, 0.08f, 0.08f), false);
                var red = LampMaterial("BeamNG_Sign_Red", new Color(0.8f, 0.05f, 0.05f), false);
                var white = LampMaterial("BeamNG_Sign_White", new Color(0.95f, 0.95f, 0.95f), false);
                Part(PrimitiveType.Cylinder, "Pole", root.transform, new Vector3(0f, 1.2f, 0f), new Vector3(0.08f, 1.2f, 0.08f), pole);
                var disc = Part(PrimitiveType.Cylinder, "Sign", root.transform, new Vector3(0f, 2.5f, -0.06f), new Vector3(0.75f, 0.02f, 0.75f), red);
                disc.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var ring = Part(PrimitiveType.Cylinder, "Sign Mark", root.transform, new Vector3(0f, 2.5f, -0.09f), new Vector3(0.45f, 0.01f, 0.45f), white);
                ring.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            Selection.activeGameObject = root;
        }

        /// <summary>Speed limit sign: pole + red ring + white disc with the number (generated texture). Replace the child objects with your own model if you like.</summary>
        /// <summary>Speed zone object; if two objects (e.g. Road Architect nodes) are selected they become start and end node.</summary>
        public static void CreateSpeedZone()
        {
            var sel = Selection.transforms;
            var go = new GameObject("BeamNG Speed Zone");
            Undo.RegisterCreatedObjectUndo(go, "Create speed zone");
            var z = go.AddComponent<BeamNGSpeedZone>();
            if (sel.Length >= 2) { z.startNode = sel[0]; z.endNode = sel[1]; go.transform.position = (sel[0].position + sel[1].position) * 0.5f; }
            else go.transform.position = Pivot();
            Selection.activeGameObject = go;
        }

        /// <summary>Adds the sign logic to the selected objects (your own 3D models stay as they are).</summary>
        public static int AddSignLogicToSelection(bool speed)
        {
            int n = 0;
            foreach (var go in Selection.gameObjects)
            {
                if (speed) { if (go.GetComponent<BeamNGSpeedSign>() == null) { Undo.AddComponent<BeamNGSpeedSign>(go); n++; } }
                else if (go.GetComponent<BeamNGTrafficSign>() == null) { Undo.AddComponent<BeamNGTrafficSign>(go); n++; }
            }
            return n;
        }

        /// <summary>Diagnosis: prints position, size, scale, vertex count and materials of every mesh below the selection.</summary>
        public static void CheckSelected()
        {
            var sb = new System.Text.StringBuilder("UnityToBeamNG - selected objects\n");
            int n = 0;
            foreach (var go in Selection.gameObjects)
                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (n++ >= 25) break;
                    var mf = mr.GetComponent<MeshFilter>();
                    var m = mf != null ? mf.sharedMesh : null;
                    var b = mr.bounds; var ls = mr.transform.lossyScale;
                    int miss = 0; foreach (var mt in mr.sharedMaterials) if (mt == null) miss++;
                    sb.AppendLine(BeamNGEditorUtilities.HierarchyPath(mr.transform) + " | active " + mr.gameObject.activeInHierarchy + " enabled " + mr.enabled + " | pos " + b.center.ToString("F1") + " size " + b.size.ToString("F2") + " | scale " + ls.ToString("F3") + " | mesh " + (m != null ? m.name + " v" + m.vertexCount + (m.isReadable ? "" : " NOT READABLE") : "none") + " | materials " + mr.sharedMaterials.Length + " (empty " + miss + ")" + (mr.GetComponentInParent<LODGroup>() != null ? " | LODGroup" : ""));
                }
            Debug.Log(sb.ToString());
            EditorUtility.DisplayDialog("Selected objects", n == 0 ? "No mesh below the selection." : "Details for " + Mathf.Min(n, 25) + " mesh(es) are in the Unity Console (copy them and send them to me).", "OK");
        }

        public static void CreateSpeedSign(bool withVisuals)
        {
            var root = new GameObject("BeamNG Speed Sign");
            Undo.RegisterCreatedObjectUndo(root, "Create speed sign");
            root.transform.position = Pivot();
            var sg = root.AddComponent<BeamNGSpeedSign>();
            if (withVisuals) BuildSpeedSignVisual(root.transform, sg.limit);
            Selection.activeGameObject = root;
        }

        /// <summary>(Re)builds the simple speed sign model for the current limit of the sign.</summary>
        public static void RebuildSpeedSign(BeamNGSpeedSign sg)
        {
            for (int i = sg.transform.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(sg.transform.GetChild(i).gameObject);
            BuildSpeedSignVisual(sg.transform, sg.limit);
        }

        static void BuildSpeedSignVisual(Transform root, int limit)
        {
            var pole = LampMaterial("BeamNG_Signal_Dark", new Color(0.08f, 0.08f, 0.08f), false);
            var red = LampMaterial("BeamNG_Sign_Red", new Color(0.8f, 0.05f, 0.05f), false);
            Part(PrimitiveType.Cylinder, "Pole", root, new Vector3(0f, 1.3f, 0f), new Vector3(0.08f, 1.3f, 0.08f), pole);
            var ring = Part(PrimitiveType.Cylinder, "Sign Ring", root, new Vector3(0f, 2.75f, -0.05f), new Vector3(0.8f, 0.01f, 0.8f), red);
            ring.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = "Sign Face";
            Object.DestroyImmediate(face.GetComponent<Collider>());
            face.transform.SetParent(root, false);
            face.transform.localPosition = new Vector3(0f, 2.75f, -0.065f);   // quad front side looks towards -Z = towards the oncoming driver
            face.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            face.GetComponent<MeshRenderer>().sharedMaterial = SpeedSignMaterial(limit);
        }

        static Material SpeedSignMaterial(int limit)
        {
            const string dir = "Assets/UnityToBeamNG_Generated";
            LampMaterial("BeamNG_Signal_Dark", Color.black, false);   // makes sure the folder exists
            string png = dir + "/BeamNG_Speed_" + limit + ".png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(png) == null)
            {
                var tex = BeamNGSpeedSignTexture.Make(limit, 256);
                System.IO.File.WriteAllBytes(png, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(png);
            }
            string matPath = dir + "/BeamNG_Speed_" + limit + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m != null) return m;
            var sh = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Texture");
            m = new Material(sh) { name = "BeamNG_Speed_" + limit };
            m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(png);
            if (m.HasProperty("_Mode")) { m.SetFloat("_Mode", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450; }   // round sign: cut out the corners
            AssetDatabase.CreateAsset(m, matPath);
            return m;
        }

        /// <summary>Water volume: root object (X/Z scale = footprint, Y position = surface) + a preview quad that is not exported.</summary>
        public static void CreateWater()
        {
            var root = new GameObject("BeamNG Water");
            Undo.RegisterCreatedObjectUndo(root, "Create water");
            root.transform.position = Pivot();
            root.transform.localScale = new Vector3(100f, 1f, 100f);
            root.AddComponent<BeamNGWater>();
            root.AddComponent<BeamNGCollisionSettings>().excludeFromExport = true; // the preview quad must not become a mesh in the map
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Water Preview (not exported)";
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(root.transform, false);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var m = LampMaterial("BeamNG_Water_Preview", new Color(0.2f, 0.45f, 0.8f, 0.55f), false);
            if (m.HasProperty("_Mode")) { m.SetFloat("_Mode", 3); m.SetInt("_SrcBlend", 5); m.SetInt("_DstBlend", 10); m.SetInt("_ZWrite", 0); m.EnableKeyword("_ALPHABLEND_ON"); m.renderQueue = 3000; }
            q.GetComponent<MeshRenderer>().sharedMaterial = m;
            Selection.activeGameObject = root;
        }

        /// <summary>Gives lamp objects whose material was lost (e.g. the tool folder was replaced by an update) their colour again.</summary>
        public static int RepairLampMaterials()
        {
            int fixedCount = 0;
            foreach (var tl in Object.FindObjectsByType<BeamNGTrafficLight>(FindObjectsSortMode.None))
            {
                var roles = new[] { new KeyValuePair<Transform, string>(tl.redLamp, "Red"), new KeyValuePair<Transform, string>(tl.yellowLamp, "Yellow"), new KeyValuePair<Transform, string>(tl.greenLamp, "Green") };
                foreach (var kv in roles)
                {
                    if (kv.Key == null) continue;
                    foreach (var mr in kv.Key.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var mats = mr.sharedMaterials; bool bad = false;
                        for (int i = 0; i < mats.Length; i++) if (mats[i] == null) bad = true;
                        if (!bad) continue;
                        Color col = kv.Value == "Red" ? new Color(1f, 0.05f, 0.05f) : kv.Value == "Yellow" ? new Color(1f, 0.75f, 0f) : new Color(0.05f, 1f, 0.2f);
                        var m = LampMaterial("BeamNG_Signal_" + kv.Value, col, true);
                        for (int i = 0; i < mats.Length; i++) if (mats[i] == null) mats[i] = m;
                        mr.sharedMaterials = mats; fixedCount++;
                    }
                }
            }
            return fixedCount;
        }

        static Material LampMaterial(string name, Color color, bool emissive)
        {
            const string dir = "Assets/UnityToBeamNG_Generated";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "UnityToBeamNG_Generated");   // outside the tool folder: survives a tool update
            }
            var path = dir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var sh = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Color");
            var m = new Material(sh) { name = name };
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", color * 2f);
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Transform Part(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        /// <summary>One emissive lamp disc at a world position, flattened along the driving direction (it faces the oncoming traffic).</summary>
        internal static Transform MakeLamp(Transform parent, string role, Vector3 worldPos, float diameter)
        {
            Color col = role == "Red" ? new Color(1f, 0.05f, 0.05f) : role == "Yellow" ? new Color(1f, 0.75f, 0f) : new Color(0.05f, 1f, 0.2f);
            var m = LampMaterial("BeamNG_Signal_" + role, col, true);
            var t = Part(PrimitiveType.Sphere, "Lamp " + role, parent, Vector3.zero, new Vector3(diameter, diameter, diameter * 0.4f), m);
            t.position = worldPos - parent.forward * (diameter * 0.25f);   // sticks out of the housing towards the driver
            return t;
        }

        /// <summary>Pole + signal head with three emissive lamps. Lamps face the oncoming traffic (against the light's blue arrow).</summary>
        static void AddPole(Transform parent)
        {
            var dark = LampMaterial("BeamNG_Signal_Dark", new Color(0.08f, 0.08f, 0.08f), false);
            var red = LampMaterial("BeamNG_Signal_Red", new Color(1f, 0.05f, 0.05f), true);
            var yellow = LampMaterial("BeamNG_Signal_Yellow", new Color(1f, 0.75f, 0f), true);
            var green = LampMaterial("BeamNG_Signal_Green", new Color(0.05f, 1f, 0.2f), true);
            Part(PrimitiveType.Cylinder, "Pole", parent, new Vector3(0f, 2.2f, 0f), new Vector3(0.18f, 2.2f, 0.18f), dark);
            Part(PrimitiveType.Cube, "Head", parent, new Vector3(0f, 4.7f, 0f), new Vector3(0.5f, 1.45f, 0.35f), dark);
            var tl = parent.GetComponent<BeamNGTrafficLight>();
            var lampScale = new Vector3(0.32f, 0.32f, 0.12f);
            tl.redLamp = Part(PrimitiveType.Sphere, "Lamp Red", parent, new Vector3(0f, 5.15f, -0.2f), lampScale, red);
            tl.yellowLamp = Part(PrimitiveType.Sphere, "Lamp Yellow", parent, new Vector3(0f, 4.7f, -0.2f), lampScale, yellow);
            tl.greenLamp = Part(PrimitiveType.Sphere, "Lamp Green", parent, new Vector3(0f, 4.25f, -0.2f), lampScale, green);
        }
    }
}
