using UnityEngine;

namespace UnityToBeamNG
{
    public enum BeamNGSpeedUnit { Kmh, Mph }

    /// <summary>
    /// A speed limit sign. The blue Z axis must point in the driving direction the sign is valid for (the sign face looks against it).
    /// The visible model (child objects) can be deleted and replaced by your own 3D model. For the game, the limit is applied to the
    /// road in front of the sign (see 'lengthMeters'): the AI and the game's speed limit logic use it.
    /// Unity-side configuration only.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("BeamNG/Speed Sign")]
    public class BeamNGSpeedSign : MonoBehaviour
    {
        [Min(5)] public int limit = 50;
        public BeamNGSpeedUnit unit = BeamNGSpeedUnit.Kmh;
        [Tooltip("The limit is applied to the road in front of the sign for this many metres (or until the road ends).")]
        [Min(20f)] public float lengthMeters = 500f;
        [Tooltip("Write the limit into the road network of the map (map.json). Turn off for a purely visual sign.")]
        public bool affectNavigation = true;
        public bool exportEnabled = true;

        public float MetersPerSecond { get { return unit == BeamNGSpeedUnit.Kmh ? limit / 3.6f : limit * 0.44704f; } }

        void OnDrawGizmos()
        {
            var p = transform.position;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(p + Vector3.up * 2.4f, 0.4f);
            Gizmos.color = Color.white;
            Gizmos.DrawLine(p, p + transform.forward * 4f);
        }
    }
}
