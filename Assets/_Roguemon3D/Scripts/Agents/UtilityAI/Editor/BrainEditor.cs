#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UtilityAI;

namespace _PinBoy.Scripts.Agents.UtilityAI.Editor
{
    [CustomEditor(typeof(Brain))]
    public class BrainEditor : UnityEditor.Editor
    {
        ReorderableList _actionsList;
        SerializedProperty _actionsProperty;

        void OnEnable()
        {
            if (target == null)
                return;
            
            _actionsProperty = serializedObject.FindProperty("actions");
            _actionsList = new ReorderableList(serializedObject, _actionsProperty, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Utility Actions"),
                drawElementCallback = DrawElement,
                elementHeightCallback = GetElementHeight,
                onAddDropdownCallback = ShowAddMenu
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawDefaultInspector();
            EditorGUILayout.Space();
            _actionsList.DoLayoutList();

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                DrawRuntimeDebugging();
            }
        }

        void DrawRuntimeDebugging()
        {
            Brain brain = (Brain)target;
            if (brain.context == null)
            {
                EditorGUILayout.HelpBox("Context has not been initialized yet.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Runtime Debug", EditorStyles.boldLabel);
            foreach (var action in brain.actions)
            {
                if (action == null)
                    continue;

                float score = 0f;
                string error = null;
                try
                {
                    score = action.CalculateUtility(brain.context, brain.GetPerceivedTargets());
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                if (!string.IsNullOrEmpty(error))
                {
                    EditorGUILayout.HelpBox($"{action.GetType().Name} threw: {error}", MessageType.Error);
                }
                else
                {
                    EditorGUILayout.LabelField(action.GetType().Name, score.ToString("0.###"));
                }
            }
        }

        void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (index >= _actionsProperty.arraySize)
                return;

            SerializedProperty element = _actionsProperty.GetArrayElementAtIndex(index);
            rect.y += 2f;
            rect.height = EditorGUI.GetPropertyHeight(element, true);
            EditorGUI.PropertyField(rect, element, GUIContent.none, true);
        }

        float GetElementHeight(int index)
        {
            if (index >= _actionsProperty.arraySize)
                return EditorGUIUtility.singleLineHeight;

            SerializedProperty element = _actionsProperty.GetArrayElementAtIndex(index);
            return EditorGUI.GetPropertyHeight(element, true) + 4f;
        }

        void ShowAddMenu(Rect buttonRect, ReorderableList list)
        {
            var menu = new GenericMenu();
            bool hasEntries = false;
            foreach (var type in TypeCache.GetTypesDerivedFrom(typeof(AIAction)))
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition || type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                hasEntries = true;
                menu.AddItem(new GUIContent(type.Name), false, () => AddAction(type));
            }

            if (!hasEntries)
            {
                menu.AddDisabledItem(new GUIContent("No AIAction implementations found"));
            }

            menu.ShowAsContext();
        }

        void AddAction(Type actionType)
        {
            serializedObject.Update();
            _actionsProperty.arraySize++;
            SerializedProperty element = _actionsProperty.GetArrayElementAtIndex(_actionsProperty.arraySize - 1);
            element.managedReferenceValue = Activator.CreateInstance(actionType);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
