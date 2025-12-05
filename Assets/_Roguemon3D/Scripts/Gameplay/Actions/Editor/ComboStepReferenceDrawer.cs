using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions.Editor
{
    [CustomPropertyDrawer(typeof(CharacterComboAction.ComboStepReference))]
    public class ComboStepReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty indexProperty = property.FindPropertyRelative("stepIndex");
            int currentIndex = indexProperty?.intValue ?? -1;

            SerializedProperty stepsProperty = property.serializedObject.FindProperty("steps");
            List<string> options = new List<string> { "None" };

            if (stepsProperty != null)
            {
                for (int i = 0; i < stepsProperty.arraySize; i++)
                {
                    SerializedProperty stepProperty = stepsProperty.GetArrayElementAtIndex(i);
                    string id = stepProperty.FindPropertyRelative("id")?.stringValue;
                    options.Add(string.IsNullOrWhiteSpace(id) ? $"Step {i}" : id);
                }
            }

            int popupIndex = Mathf.Clamp(currentIndex + 1, 0, options.Count - 1);
            int selected = EditorGUI.Popup(position, label, popupIndex, options.Select(o => new GUIContent(o)).ToArray());

            if (selected != popupIndex && indexProperty != null)
            {
                indexProperty.intValue = selected - 1;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }
}
