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
                if (GUILayout.Button("Open Combo Graph Editor", GUILayout.Height(24f)))
                {
                    ComboGraphEditorWindow.Open((CharacterComboAction)target);
                }
                GUILayout.FlexibleSpace();
            }
        }
    }
}
