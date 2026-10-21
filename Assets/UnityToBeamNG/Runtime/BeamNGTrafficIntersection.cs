using UnityEngine;

namespace UnityToBeamNG
{
    /// <summary>
    /// Groups the BeamNGTrafficLight components below this object into one signal-controlled junction.
    /// The phases take turns: green, yellow, all-red.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("BeamNG/Traffic Intersection")]
    public class BeamNGTrafficIntersection : MonoBehaviour
    {
        public float greenSeconds = 12f;
        public float yellowSeconds = 3f;
        [Tooltip("Time when every direction is red between two phases.")]
        public float allRedSeconds = 2f;
        public bool exportEnabled = true;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, 1.2f);
        }
    }
}
