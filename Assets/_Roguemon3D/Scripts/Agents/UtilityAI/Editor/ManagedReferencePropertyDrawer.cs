#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UtilityAI.Editor
{
    /// <summary>
    /// Base class that adds a type selector and foldout behaviour for managed reference fields.
    /// </summary>
    public abstract class ManagedReferencePropertyDrawer : PropertyDrawer
    {
        static readonly Dictionary<Type, List<Type>> TypeCache = new Dictionary<Type, List<Type>>();

        protected abstract Type BaseType { get; }

        protected virtual string GetTypeDisplayName(Type type)
        {
            return ObjectNames.NicifyVariableName(type.Name);
        }

        protected virtual string GetNullDisplayName() => "None";

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            Rect headerRect = new Rect(position.x, position.y, position.width, lineHeight);
            Rect buttonRect = new Rect(headerRect.xMax - 90f, headerRect.y, 90f, lineHeight);

            string typeName = GetCurrentTypeName(property);
            var foldoutContent = new GUIContent($"{label.text} ({typeName})");
            property.isExpanded = EditorGUI.Foldout(headerRect, property.isExpanded, foldoutContent, false);

            using (new EditorGUI.DisabledScope(!GUI.enabled))
            {
                string buttonLabel = string.IsNullOrEmpty(property.managedReferenceFullTypename) ? "Assign" : "Change";
                if (GUI.Button(buttonRect, buttonLabel, EditorStyles.miniButton))
                {
                    ShowTypeMenu(property);
                }
            }

            float y = headerRect.y + lineHeight;
            if (property.isExpanded)
            {
                y += spacing;
                EditorGUI.indentLevel++;
                y = DrawReferenceContent(position, property, y);
                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (property.isExpanded)
            {
                height += EditorGUIUtility.standardVerticalSpacing;
                height += GetReferenceContentHeight(property);
            }

            return height;
        }

        protected virtual float DrawReferenceContent(Rect totalRect, SerializedProperty property, float y)
        {
            if (property.managedReferenceValue == null)
            {
                float infoHeight = EditorGUIUtility.singleLineHeight * 1.5f;
                Rect infoRect = new Rect(totalRect.x, y, totalRect.width, infoHeight);
                EditorGUI.HelpBox(infoRect, "Select a type to edit its properties.", MessageType.Info);
                return y + infoRect.height;
            }

            var copy = property.Copy();
            var end = property.GetEndProperty();
            bool enterChildren = true;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            bool drewChild = false;
            while (copy.NextVisible(enterChildren) && !SerializedProperty.EqualContents(copy, end))
            {
                float childHeight = EditorGUI.GetPropertyHeight(copy, true);
                Rect childRect = new Rect(totalRect.x, y, totalRect.width, childHeight);
                EditorGUI.PropertyField(childRect, copy, true);
                y += childHeight + spacing;
                enterChildren = false;
                drewChild = true;
            }

            if (drewChild)
                y -= spacing;

            return y;
        }

        protected virtual float GetReferenceContentHeight(SerializedProperty property)
        {
            if (property.managedReferenceValue == null)
                return EditorGUIUtility.singleLineHeight * 1.5f;

            float height = 0f;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            bool drewChild = false;

            var copy = property.Copy();
            var end = property.GetEndProperty();
            bool enterChildren = true;
            while (copy.NextVisible(enterChildren) && !SerializedProperty.EqualContents(copy, end))
            {
                height += EditorGUI.GetPropertyHeight(copy, true) + spacing;
                enterChildren = false;
                drewChild = true;
            }

            if (drewChild)
                height -= spacing;

            return height;
        }

        void ShowTypeMenu(SerializedProperty property)
        {
            var menu = new GenericMenu();
            string currentFullName = property.managedReferenceFullTypename;

            menu.AddItem(new GUIContent(GetNullDisplayName()), string.IsNullOrEmpty(currentFullName), () => AssignType(property, null));

            bool hasEntries = false;
            foreach (var type in GetAssignableTypes())
            {
                string fullName = GetManagedReferenceFullName(type);
                bool isCurrent = fullName == currentFullName;
                menu.AddItem(new GUIContent(GetTypeDisplayName(type)), isCurrent, () => AssignType(property, type));
                hasEntries = true;
            }

            if (!hasEntries)
            {
                menu.AddDisabledItem(new GUIContent("No assignable types found"));
            }

            menu.ShowAsContext();
        }

        void AssignType(SerializedProperty property, Type type)
        {
            property.serializedObject.Update();
            property.managedReferenceValue = type == null ? null : Activator.CreateInstance(type);
            property.serializedObject.ApplyModifiedProperties();
        }

        string GetCurrentTypeName(SerializedProperty property)
        {
            string fullName = property.managedReferenceFullTypename;
            if (string.IsNullOrEmpty(fullName))
                return GetNullDisplayName();

            int spaceIndex = fullName.IndexOf(' ');
            string typeName = spaceIndex >= 0 ? fullName.Substring(spaceIndex + 1) : fullName;
            int lastDot = typeName.LastIndexOf('.');
            return lastDot >= 0 ? typeName.Substring(lastDot + 1) : typeName;
        }

        IEnumerable<Type> GetAssignableTypes()
        {
            Type baseType = BaseType;
            if (!TypeCache.TryGetValue(baseType, out var types))
            {
                types = UnityEditor.TypeCache.GetTypesDerivedFrom(baseType)
                    .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && HasPublicParameterlessConstructor(t))
                    .OrderBy(t => t.Name)
                    .ToList();
                TypeCache[baseType] = types;
            }

            return types;
        }

        static bool HasPublicParameterlessConstructor(Type type)
        {
            return type.GetConstructor(Type.EmptyTypes) != null || type.IsValueType;
        }

        static string GetManagedReferenceFullName(Type type)
        {
            return $"{type.Assembly.GetName().Name} {type.FullName}";
        }
    }
}
#endif
