using UnityEngine;

namespace UnityToBeamNG
{
    /// <summary>
    /// Speed limit for a whole road section: drag two Road Architect nodes (or any two objects next to the road) into
    /// 'startNode' and 'endNode'. The limit applies to the road between them (both directions).
    /// Unity-side configuration only.
    /// </summary>
    [AddComponentMenu("BeamNG/Speed Zone")]
    public class BeamNGSpeedZone : MonoBehaviour
    {
        public Transform startNode;
        public Transform endNode;
        [Min(5)] public int limit = 50;
        public BeamNGSpeedUnit unit = BeamNGSpeedUnit.Kmh;
        public bool exportEnabled = true;

        public float MetersPerSecond { get { return unit == BeamNGSpeedUnit.Kmh ? limit / 3.6f : limit * 0.44704f; } }

        void OnDrawGizmos()
        {
            if (startNode == null || endNode == null) return;
            Gizmos.color = Color.yellow;
            // the limit follows the road: draw through the sibling nodes (Road Architect: Spline/Node0, Node1, ...) between start and end
            if (startNode.parent != null && startNode.parent == endNode.parent)
            {
                int a = startNode.GetSiblingIndex(), b = endNode.GetSiblingIndex(); int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
                var par = startNode.parent; Vector3 prev = par.GetChild(a).position + Vector3.up * 2f;
                int dir = b >= a ? 1 : -1;
                for (int i = a + dir; i != b + dir; i += dir) { var q = par.GetChild(i).position + Vector3.up * 2f; Gizmos.DrawLine(prev, q); prev = q; }
            }
            else Gizmos.DrawLine(startNode.position + Vector3.up * 2f, endNode.position + Vector3.up * 2f);
            Gizmos.DrawWireSphere(startNode.position + Vector3.up * 2f, 0.7f);
            Gizmos.DrawWireSphere(endNode.position + Vector3.up * 2f, 0.7f);
        }
    }
}
