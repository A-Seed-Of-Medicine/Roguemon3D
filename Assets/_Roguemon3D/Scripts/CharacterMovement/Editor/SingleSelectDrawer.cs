using System;
using UnityEditor;
using UnityEngine;

namespace _PinBoy.Scripts.CharacterMovement.Editor
{
    [CustomPropertyDrawer(typeof(SingleSelectAttribute))]
    public class SingleSelectDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            if (property.propertyType != SerializedPropertyType.Enum)
            {
                EditorGUI.PropertyField(position, property, label);
                EditorGUI.EndProperty();
                return;
            }

            Enum enumValue = (Enum)Enum.ToObject(fieldInfo.FieldType, property.intValue);
            Enum selected = EditorGUI.EnumPopup(position, label, enumValue);
            property.intValue = Convert.ToInt32(selected);

            EditorGUI.EndProperty();
        }
    }
}
