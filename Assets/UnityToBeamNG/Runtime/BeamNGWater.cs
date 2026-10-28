using UnityEngine;

namespace UnityToBeamNG
{
    public enum BeamNGWaterType { Lake, Ocean, River, Pool, Stream }

    /// <summary>
    /// Marks a GameObject as a BeamNG water volume (WaterBlock). Footprint = X/Z scale of this object, the water surface is at its Y position,
    /// the volume reaches <see cref="depth"/> metres downwards. The preview quad in Unity is NOT exported as a mesh.
    /// Unity-side configuration only.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("BeamNG/Water")]
    public class BeamNGWater : MonoBehaviour
    {
        public BeamNGWaterType waterType = BeamNGWaterType.Lake;
        [Min(0.5f)] public float depth = 10f;
        public bool exportEnabled = true;

        public string MaterialName
        {
            get
            {
                switch (waterType)
                {
                    case BeamNGWaterType.Ocean: return "water_ocean";
                    case BeamNGWaterType.River: return "water_river";
                    case BeamNGWaterType.Pool: return "water_pool";
                    case BeamNGWaterType.Stream: return "water_stream";
                    default: return "water_lake";
                }
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.9f);
            var s = transform.lossyScale;
            Gizmos.matrix = Matrix4x4.TRS(transform.position + Vector3.down * depth * 0.5f, transform.rotation, new Vector3(s.x, depth, s.z));
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
    }
}
