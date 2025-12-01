using UnityEditor;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions.Editor
{
    [CustomEditor(typeof(CharacterComboAction))]
    public class CharacterComboActionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Open GraphToolkit Editor", GUILayout.Height(24f)))
                {
                    GraphToolkit.CharacterComboGraphWindow.Open((CharacterComboAction)target);
                }

                if (GUILayout.Button("Legacy Inspector", GUILayout.Height(24f)))
                {
                    ComboGraphEditorWindow.Open((CharacterComboAction)target);
                }
                GUILayout.FlexibleSpace();
            }
        }
    }
}
