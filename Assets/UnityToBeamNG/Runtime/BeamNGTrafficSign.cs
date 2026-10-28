using UnityEngine;

namespace UnityToBeamNG
{
    public enum BeamNGSignType { Stop }

    /// <summary>
    /// A traffic sign the AI obeys (exported to signals.json). The blue Z axis must point in the driving direction of the
    /// traffic that has to stop (towards the junction). Place the object next to the stop line of a road that is part of
    /// the road network. The child objects are only a simple visual: delete them and use your own 3D model if you like.
    /// Unity-side configuration only.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("BeamNG/Traffic Sign")]
    public class BeamNGTrafficSign : MonoBehaviour
    {
        public BeamNGSignType type = BeamNGSignType.Stop;
        public bool exportEnabled = true;

        void OnDrawGizmos()
        {
            var p = transform.position;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(p + Vector3.up * 2.4f, 0.45f);
            Gizmos.color = Color.white;
            Gizmos.DrawLine(p, p + transform.forward * 4f);
        }
    }
}
