using UnityEngine;

namespace UnityToBeamNG
{
    public enum BeamNGCollisionMode
    {
        Auto,         // as set in the window (default: everything collides)
        None,         // no collision
        VisibleMesh,  // visible polygons = collision (safe but expensive)
        Bounds        // bounding box only
    }

    /// <summary>Unity-side configuration, not a native BeamNG component.</summary>
    [AddComponentMenu("BeamNG/Collision Settings")]
    public class BeamNGCollisionSettings : MonoBehaviour
    {
        public BeamNGCollisionMode mode = BeamNGCollisionMode.Auto;
        [Tooltip("Do not export this object (and its children).")]
        public bool excludeFromExport = false;
    }
}
