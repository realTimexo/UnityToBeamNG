using UnityEngine;

namespace UnityToBeamNG
{
    /// <summary>
    /// Unity-side configuration (NOT a native BeamNG component).
    /// Put it on an empty GameObject. The position and facing direction (blue Z axis)
    /// become the SpawnSphere object in the BeamNG level.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("BeamNG/Spawn Point")]
    public class BeamNGSpawnPoint : MonoBehaviour
    {
        [Tooltip("Internal object name in BeamNG (only a-z, A-Z, 0-9, _). Referenced in info.json.")]
        public string spawnName = "spawns_default";

        [Tooltip("Display name in the BeamNG map selection.")]
        public string displayName = "Default spawn";

        [TextArea] public string description = "";

        [Tooltip("Exactly one spawn point should be the default. Otherwise the first one (alphabetically) is used.")]
        public bool isDefault = true;

        void OnDrawGizmos()
        {
            var p = transform.position;
            var f = transform.forward;
            Gizmos.color = isDefault ? Color.green : Color.cyan;
            Gizmos.DrawWireSphere(p, 0.75f);
            Gizmos.DrawLine(p, p + f * 4f);
            Gizmos.DrawLine(p + f * 4f, p + f * 3.2f + transform.right * 0.5f);
            Gizmos.DrawLine(p + f * 4f, p + f * 3.2f - transform.right * 0.5f);
        }
    }
}
