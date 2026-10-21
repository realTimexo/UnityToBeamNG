using System.Collections.Generic;
using UnityEngine;

namespace UnityToBeamNG
{
    public enum BeamNGRoadSource
    {
        /// <summary>The direct children of this object are the road nodes (in child order).</summary>
        ChildPoints,
        /// <summary>Read the centre line from a Road Architect road on the same object (via reflection).</summary>
        RoadArchitect
    }

    /// <summary>
    /// Marks a centre line as a drivable road for BeamNG (AI, traffic, navigation). Exported as an invisible
    /// DecalRoad; your own road meshes stay as they are. Unity-side configuration only.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("BeamNG/Road")]
    public class BeamNGRoad : MonoBehaviour
    {
        public BeamNGRoadSource source = BeamNGRoadSource.ChildPoints;
        [Tooltip("Road width in metres (used for lane detection of the AI).")]
        public float width = 8f;
        [Range(0f, 1f)] public float drivability = 1f;
        public bool oneWay = false;
        [Tooltip("Lanes are derived from the width. Turn off to set the lane counts manually.")]
        public bool autoLanes = true;
        public int lanesLeft = 1;
        public int lanesRight = 1;
        public bool closedLoop = false;
        public bool exportEnabled = true;

        /// <summary>World-space node positions in Unity. Child points only; Road Architect is read in the editor.</summary>
        public List<Vector3> ChildPointPositions()
        {
            var res = new List<Vector3>();
            foreach (Transform t in transform) if (t.gameObject.activeInHierarchy) res.Add(t.position);
            return res;
        }

        void OnDrawGizmos()
        {
            if (source != BeamNGRoadSource.ChildPoints) return;
            var pts = ChildPointPositions();
            Gizmos.color = new Color(1f, 0.6f, 0.1f);
            for (int i = 0; i < pts.Count; i++)
            {
                Gizmos.DrawWireSphere(pts[i], 0.6f);
                if (i > 0) Gizmos.DrawLine(pts[i - 1], pts[i]);
            }
            if (closedLoop && pts.Count > 2) Gizmos.DrawLine(pts[pts.Count - 1], pts[0]);
        }
    }
}
