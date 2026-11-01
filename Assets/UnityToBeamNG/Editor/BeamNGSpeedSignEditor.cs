using UnityEditor;
using UnityEngine;

namespace UnityToBeamNG
{
    [CustomEditor(typeof(BeamNGSpeedSign))]
    public class BeamNGSpeedSignEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var sg = (BeamNGSpeedSign)target;
            EditorGUILayout.LabelField("= " + sg.MetersPerSecond.ToString("F1") + " m/s (" + (sg.unit == BeamNGSpeedUnit.Kmh ? (sg.limit / 1.609344f).ToString("F0") + " mph" : (sg.limit * 1.609344f).ToString("F0") + " km/h") + ")");
            if (GUILayout.Button("Rebuild simple model for this limit (replaces all child objects)"))
                BeamNGRoadTools.RebuildSpeedSign(sg);
        }
    }
}
