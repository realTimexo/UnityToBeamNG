using UnityEngine;

namespace UnityToBeamNG
{
    /// <summary>
    /// One traffic signal. The blue Z axis must point in the driving direction of the traffic it controls
    /// (towards the junction). Lights of the same phase show the same colour; the phases of a
    /// BeamNGTrafficIntersection take turns. Unity-side configuration only.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("BeamNG/Traffic Light")]
    public class BeamNGTrafficLight : MonoBehaviour
    {
        [Tooltip("Lights with the same phase number are green together.")]
        public int phase = 0;
        [Header("Lamps (optional): objects with a mesh that is shown only in the matching state")]
        [Tooltip("Object (with MeshRenderer, e.g. an emissive red sphere) that is visible while the signal is red.")]
        public Transform redLamp;
        public Transform yellowLamp;
        public Transform greenLamp;
        [Tooltip("Experimental: also link the Unity meshes below this object as the visible signal (signalInstance). Only useful for BeamNG stock signal models.")]
        public bool linkVisual = false;

        void OnDrawGizmos()
        {
            var p = transform.position;
            Gizmos.color = phase % 2 == 0 ? Color.green : Color.red;
            Gizmos.DrawWireCube(p + Vector3.up * 3f, new Vector3(0.5f, 1.2f, 0.5f));
            Gizmos.DrawLine(p, p + Vector3.up * 2.4f);
            Gizmos.color = Color.white;
            Gizmos.DrawLine(p, p + transform.forward * 4f);
        }
    }
}
