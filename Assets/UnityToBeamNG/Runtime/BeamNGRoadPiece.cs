using System.Collections.Generic;
using UnityEngine;

namespace UnityToBeamNG
{
    /// <summary>
    /// A modular 3D road piece (straight, curve, T, crossing). Connectors mark where cars enter/leave the piece:
    /// each connector's blue Z axis points OUT of the piece. Two connectors = one path (curved if the connectors are
    /// not opposite each other); three or more connectors = a junction (every arm leads to the junction centre).
    /// Pieces whose connectors touch are linked in BeamNG automatically. Unity-side configuration only.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("BeamNG/Road Piece")]
    public class BeamNGRoadPiece : MonoBehaviour
    {
        public List<Transform> connectors = new List<Transform>();
        public float width = 8f;
        [Range(0f, 1f)] public float drivability = 1f;
        public bool exportEnabled = true;

        void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            foreach (var c in connectors)
            {
                if (c == null) continue;
                Gizmos.DrawWireSphere(c.position, 0.5f);
                Gizmos.DrawLine(c.position, c.position + c.forward * 2.5f);
                Gizmos.DrawLine(c.position - c.right * width * 0.5f, c.position + c.right * width * 0.5f);
            }
        }
    }
}
