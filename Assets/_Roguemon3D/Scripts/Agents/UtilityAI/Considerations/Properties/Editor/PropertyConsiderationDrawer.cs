#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Properties;
using UnityEditor;
using UnityEngine;

namespace Agents.UtilityAI.Considerations.Properties.Editor
{
    [CustomPropertyDrawer(typeof(global::UtilityAI.PropertyConsideration))]
    public class PropertyConsiderationDrawer : PropertyDrawer
    {
        // Simple cache per-type to avoid re-scanning on each repaint.
        private static readonly Dictionary<System.Type, string[]> _pathsCache = new();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // 3 lines: header, object field, popup (or help box)
            return EditorGUIUtility.singleLineHeight * 3f + 8f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var srcProp   = property.FindPropertyRelative("source");
            var pathProp  = property.FindPropertyRelative("propertyPath");

            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(line, label);

            line.y += line.height + 2f;
            EditorGUI.PropertyField(line, srcProp, new GUIContent("Source"));

            var obj = srcProp.objectReferenceValue;
            line.y += line.height + 2f;

            if (obj == null)
            {
                EditorGUI.HelpBox(line, "Assign a Source to list numeric properties.", MessageType.Info);
                return;
            }

            var t = obj.GetType();
            if (!_pathsCache.TryGetValue(t, out var options))
            {
                options = CollectNumericPropertyPaths(obj);
                _pathsCache[t] = options;
            }

            if (options == null || options.Length == 0)
            {
                EditorGUI.HelpBox(line, $"No int/float properties found on {t.Name}.", MessageType.Warning);
                return;
            }

            // Current selection
            int idx = Mathf.Max(0, System.Array.IndexOf(options, pathProp.stringValue));
            int newIdx = EditorGUI.Popup(line, "Property", idx, options);
            if (newIdx != idx)
            {
                pathProp.stringValue = options[newIdx];
                property.serializedObject.ApplyModifiedProperties();
            }
        }

        private static string[] CollectNumericPropertyPaths(object instance)
        {
            // Visit with a PropertyVisitor that gathers paths for int/float values.
            var visitor = new NumericPathCollector(maxDepth: 3);
            object boxed = instance;
            PropertyContainer.Accept(visitor, ref boxed); // generic accept over runtime type
            return visitor.Results.ToArray();
        }

        private sealed class NumericPathCollector : PropertyVisitor
        {
            private readonly int _maxDepth;
            private readonly List<string> _stack = new();
            public readonly List<string> Results = new();

            public NumericPathCollector(int maxDepth) => _maxDepth = Mathf.Max(1, maxDepth);

            protected override bool IsExcluded<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value)
            {
                // Prevent exploding into UnityEngine.Object graphs.
                if (typeof(UnityEngine.Object).IsAssignableFrom(typeof(TValue))) return true;
                // Limit depth.
                return _stack.Count >= _maxDepth && !IsNumeric<TValue>();
            }

            protected override void VisitProperty<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value)
            {
                _stack.Add(property.Name);
                if (IsNumeric<TValue>())
                {
                    Results.Add(string.Join(".", _stack));
                }

                base.VisitProperty(property, ref container, ref value);
                _stack.RemoveAt(_stack.Count - 1);
            }

            private static bool IsNumeric<T>() => typeof(T) == typeof(float) || typeof(T) == typeof(int);
        }
    }
}
#endif
