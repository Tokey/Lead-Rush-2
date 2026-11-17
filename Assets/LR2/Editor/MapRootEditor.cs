using LR2.World;
using UnityEditor;
using UnityEngine;

namespace LR2.EditorTools
{
    [CustomEditor(typeof(MapRoot))]
    public class MapRootEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var root = (MapRoot)target;
            DrawDefaultInspector();
            if (root.config != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Build Config", EditorStyles.boldLabel);
                var ed = CreateEditor(root.config);
                ed.OnInspectorGUI();
            }
            EditorGUILayout.Space();
            if (GUILayout.Button("Rebuild Map (regenerates this scene)", GUILayout.Height(32)))
            {
                if (EditorUtility.DisplayDialog("Rebuild Saltworks", "Regenerate the whole map scene from the config? Manual edits in this scene will be lost.", "Rebuild", "Cancel"))
                    LR2MapBuilder.Build();
            }
        }
    }
}
