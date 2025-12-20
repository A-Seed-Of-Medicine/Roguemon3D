using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Actions.Editor
{
    [CustomPropertyDrawer(typeof(PhaseFX), true)]
    public class PhaseFxDrawer : PropertyDrawer
    {
        const string NoneOption = "None";
        static List<Type> cachedTypes;

        static List<Type> FxTypes
        {
            get
            {
                if (cachedTypes == null)
                {
                    cachedTypes = TypeCache.GetTypesDerivedFrom<PhaseFX>()
                        .Where(t => !t.IsAbstract && !t.IsGenericType && t.GetConstructor(Type.EmptyTypes) != null)
                        .OrderBy(t => t.Name)
                        .ToList();
                }

                return cachedTypes;
            }
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            Rect headerRect = new(position.x, position.y, position.width, lineHeight);
            string typeLabel = GetCurrentType(property)?.Name ?? NoneOption;
            property.isExpanded = EditorGUI.Foldout(headerRect, property.isExpanded, $"{label.text} ({typeLabel})", true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;

                Rect typeRect = new(position.x, headerRect.yMax + spacing, position.width, lineHeight);
                DrawTypePopup(typeRect, property);

                Type currentType = GetCurrentType(property);
                if (currentType != null)
                {
                    DrawChildProperties(position, property, typeRect.yMax + spacing);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            height += EditorGUIUtility.standardVerticalSpacing + EditorGUIUtility.singleLineHeight;

            if (GetCurrentType(property) != null)
            {
                height += EditorGUIUtility.standardVerticalSpacing;
                height += CalculateChildHeight(property);
            }

            return height;
        }

        void DrawTypePopup(Rect position, SerializedProperty property)
        {
            List<Type> types = FxTypes;
            string[] typeNames = BuildTypeNames(types);
            int currentIndex = GetCurrentTypeIndex(property, types);

            EditorGUI.BeginChangeCheck();
            int selected = EditorGUI.Popup(position, "Type", currentIndex, typeNames);
            if (EditorGUI.EndChangeCheck())
            {
                if (selected <= 0)
                {
                    property.managedReferenceValue = null;
                }
                else
                {
                    Type selectedType = types[selected - 1];
                    property.managedReferenceValue = Activator.CreateInstance(selectedType);
                }
            }
        }

        static string[] BuildTypeNames(IReadOnlyList<Type> types)
        {
            string[] names = new string[types.Count + 1];
            names[0] = NoneOption;
            for (int i = 0; i < types.Count; i++)
            {
                names[i + 1] = types[i].Name;
            }

            return names;
        }

        int GetCurrentTypeIndex(SerializedProperty property, IReadOnlyList<Type> types)
        {
            Type currentType = GetCurrentType(property);
            if (currentType == null)
            {
                return 0;
            }

            for (int i = 0; i < types.Count; i++)
            {
                if (types[i] == currentType)
                {
                    return i + 1;
                }
            }

            return 0;
        }

        static Type GetCurrentType(SerializedProperty property)
        {
            string fullTypeName = property.managedReferenceFullTypename;
            if (string.IsNullOrWhiteSpace(fullTypeName))
            {
                return null;
            }

            string[] split = fullTypeName.Split(' ');
            if (split.Length != 2)
            {
                return null;
            }

            return Type.GetType($"{split[1]}, {split[0]}");
        }

        void DrawChildProperties(Rect position, SerializedProperty property, float startY)
        {
            SerializedProperty iterator = property.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            bool enterChildren = true;
            float y = startY;

            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                float height = EditorGUI.GetPropertyHeight(iterator, true);
                Rect fieldRect = new(position.x, y, position.width, height);
                EditorGUI.PropertyField(fieldRect, iterator, true);
                y += height + EditorGUIUtility.standardVerticalSpacing;
            }
        }

        float CalculateChildHeight(SerializedProperty property)
        {
            SerializedProperty iterator = property.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            bool enterChildren = true;
            float height = 0f;

            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                height += EditorGUI.GetPropertyHeight(iterator, true) + EditorGUIUtility.standardVerticalSpacing;
            }

            return Mathf.Max(0f, height);
        }
    }
}
