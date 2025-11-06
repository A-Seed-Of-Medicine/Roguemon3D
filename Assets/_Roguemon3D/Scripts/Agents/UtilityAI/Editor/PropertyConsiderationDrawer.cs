#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UtilityAI.Editor
{
    [CustomPropertyDrawer(typeof(PropertyConsideration))]
    public class PropertyConsiderationDrawer : ConsiderationDrawer
    {
        protected override float DrawReferenceContent(Rect totalRect, SerializedProperty property, float y)
        {
            if (property.managedReferenceValue == null)
                return base.DrawReferenceContent(totalRect, property, y);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            var sourceProp = property.FindPropertyRelative("source");
            var pathProp = property.FindPropertyRelative("propertyPath");

            Rect sourceRect = new Rect(totalRect.x, y, totalRect.width, lineHeight);
            EditorGUI.PropertyField(sourceRect, sourceProp);
            y += lineHeight + spacing;

            string display = string.IsNullOrEmpty(pathProp.stringValue) ? "<None Selected>" : pathProp.stringValue;
            Rect currentRect = new Rect(totalRect.x, y, totalRect.width, lineHeight);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.TextField(currentRect, "Property", display);
            }
            y += lineHeight + spacing;

            Rect buttonRect = new Rect(totalRect.x, y, totalRect.width, lineHeight);
            using (new EditorGUI.DisabledScope(sourceProp.objectReferenceValue == null))
            {
                if (GUI.Button(buttonRect, "Choose Property"))
                {
                    ShowPropertySelector(sourceProp.objectReferenceValue, pathProp);
                }
            }
            y += lineHeight + spacing;

            return y - spacing;
        }

        protected override float GetReferenceContentHeight(SerializedProperty property)
        {
            if (property.managedReferenceValue == null)
                return base.GetReferenceContentHeight(property);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            return lineHeight * 3f + spacing * 2f;
        }

        void ShowPropertySelector(UnityEngine.Object source, SerializedProperty pathProp)
        {
            var menu = new GenericMenu();
            var paths = PropertyPathUtility.GetNumericPaths(source?.GetType());

            if (paths.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No numeric properties found"));
            }
            else
            {
                foreach (var path in paths)
                {
                    bool selected = path.Path == pathProp.stringValue;
                    menu.AddItem(new GUIContent(path.DisplayName), selected, () => AssignPath(pathProp, path.Path));
                }
            }

            menu.ShowAsContext();
        }

        void AssignPath(SerializedProperty pathProp, string path)
        {
            pathProp.serializedObject.Update();
            pathProp.stringValue = path;
            pathProp.serializedObject.ApplyModifiedProperties();
        }

        static class PropertyPathUtility
        {
            static readonly Dictionary<Type, List<PropertyPathInfo>> Cache = new Dictionary<Type, List<PropertyPathInfo>>();

            public static List<PropertyPathInfo> GetNumericPaths(Type type)
            {
                Debug.Log("Getting numeric paths for " + type.FullName);
                if (type == null)
                    return new List<PropertyPathInfo>();

                if (!Cache.TryGetValue(type, out var paths))
                {
                    paths = new List<PropertyPathInfo>();
                    BuildPaths(type, string.Empty, paths, new HashSet<Type>());
                    Cache[type] = paths;
                }

                return paths;
            }

            static void BuildPaths(Type type, string prefix, List<PropertyPathInfo> results, HashSet<Type> visited)
            {
                Debug.Log($"Building paths for type: {type.FullName} with prefix: '{prefix}'");
                if (type == null || !visited.Add(type))
                    return;

                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                foreach (var field in type.GetFields(flags))
                {
                    if (field.IsDefined(typeof(NonSerializedAttribute), true))
                        continue;

                    string path = string.IsNullOrEmpty(prefix) ? field.Name : $"{prefix}.{field.Name}";
                    if (IsNumeric(field.FieldType))
                    {
                        results.Add(new PropertyPathInfo(path));
                    }
                    else if (ShouldRecurse(field.FieldType))
                    {
                        BuildPaths(field.FieldType, path, results, visited);
                    }
                }

                foreach (var property in type.GetProperties(flags))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length > 0)
                        continue;

                    string path = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                    if (IsNumeric(property.PropertyType))
                    {
                        results.Add(new PropertyPathInfo(path));
                    }
                    else if (ShouldRecurse(property.PropertyType))
                    {
                        BuildPaths(property.PropertyType, path, results, visited);
                    }
                }

                visited.Remove(type);
            }

            static bool IsNumeric(Type type)
            {
                return type == typeof(float) || type == typeof(int);
            }

            static bool ShouldRecurse(Type type)
            {
                if (type == null || type.IsPrimitive || type.IsEnum || type == typeof(string))
                    return false;

                if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                    return false;

                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(Vector2) && type != typeof(Vector3))
                    return false;

                return true;
            }
        }

        readonly struct PropertyPathInfo
        {
            public string Path { get; }
            public string DisplayName { get; }

            public PropertyPathInfo(string path)
            {
                Path = path;
                DisplayName = string.Join("/", path.Split('.').Select(ObjectNames.NicifyVariableName));
            }
        }
    }
}
#endif
