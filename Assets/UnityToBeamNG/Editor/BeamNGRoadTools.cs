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

        static void AddPole(Transform parent)
        {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole"; Object.DestroyImmediate(pole.GetComponent<Collider>());
            pole.transform.SetParent(parent, false);
            pole.transform.localScale = new Vector3(0.2f, 2.2f, 0.2f);
            pole.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head"; Object.DestroyImmediate(head.GetComponent<Collider>());
            head.transform.SetParent(parent, false);
            head.transform.localScale = new Vector3(0.45f, 1.2f, 0.35f);
            head.transform.localPosition = new Vector3(0f, 4.6f, 0f);
        }
    }
}
