#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UtilityAI;

namespace _PinBoy.Scripts.Agents.UtilityAI.Editor
{
    [CustomEditor(typeof(Brain))]
    public class BrainEditor : UnityEditor.Editor
    {
        const int MaxHistoryEntries = 6;

        ReorderableList _actionsList;
        SerializedProperty _actionsProperty;
        readonly Dictionary<AIAction, Queue<Brain.ActionEvaluation>> _evaluationHistory = new();
        readonly Dictionary<AIAction, bool> _actionFoldouts = new();
        Brain _brain;

        void OnEnable()
        {
            if (target == null)
                return;

            _brain = (Brain)target;
            _actionsProperty = serializedObject.FindProperty("actions");
            _actionsList = new ReorderableList(serializedObject, _actionsProperty, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Utility Actions"),
                drawElementCallback = DrawElement,
                elementHeightCallback = GetElementHeight,
                onAddDropdownCallback = ShowAddMenu
            };

            if (_brain != null)
            {
                _brain.ActionEvaluated += HandleActionEvaluated;
            }
        }

        void OnDisable()
        {
            if (_brain != null)
            {
                _brain.ActionEvaluated -= HandleActionEvaluated;
            }
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
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string currentAction = brain.CurrentAction != null ? brain.CurrentAction.GetType().Name : "<None>";
                EditorGUILayout.LabelField("Current Action", currentAction);
                string bestAction = brain.LastBestAction != null ? brain.LastBestAction.GetType().Name : "<None>";
                EditorGUILayout.LabelField("Best Utility Action", bestAction);
                EditorGUILayout.LabelField("Best Utility Score", brain.LastBestUtility.ToString("0.###"));
                EditorGUILayout.LabelField("Best Target", FormatTarget(brain.LastBestTarget));
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to view live action evaluation details.", MessageType.Info);
                return;
            }

            if (_evaluationHistory.Count == 0)
            {
                EditorGUILayout.HelpBox("Waiting for action evaluations...", MessageType.Info);
                return;
            }

            foreach (var action in brain.actions)
            {
                if (action == null)
                {
                    continue;
                }

                _actionFoldouts.TryGetValue(action, out bool isExpanded);
                string actionLabel = action.GetType().Name;
                if (_evaluationHistory.TryGetValue(action, out Queue<Brain.ActionEvaluation> history) && history.Count > 0)
                {
                    Brain.ActionEvaluation latest = history.Last();
                    actionLabel = $"{actionLabel} ({latest.Utility:0.###})";
                }

                isExpanded = EditorGUILayout.Foldout(isExpanded, actionLabel, true);
                _actionFoldouts[action] = isExpanded;
                if (!isExpanded)
                {
                    continue;
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (!_evaluationHistory.TryGetValue(action, out Queue<Brain.ActionEvaluation> actionHistory) || actionHistory.Count == 0)
                    {
                        EditorGUILayout.LabelField("No evaluations yet.");
                        continue;
                    }

                    Brain.ActionEvaluation evaluation = actionHistory.Last();
                    EditorGUILayout.LabelField("Utility", evaluation.Utility.ToString("0.###"));
                    EditorGUILayout.LabelField("Target", FormatTarget(evaluation.Target));
                    EditorGUILayout.LabelField("Last Evaluated", $"{Time.time - evaluation.Time:0.00}s ago");

                    if (!string.IsNullOrEmpty(evaluation.Error))
                    {
                        EditorGUILayout.HelpBox(evaluation.Error, MessageType.Error);
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Recent History", EditorStyles.miniBoldLabel);
                    foreach (Brain.ActionEvaluation entry in actionHistory)
                    {
                        EditorGUILayout.LabelField($"{entry.Time:0.00}s", $"{entry.Utility:0.###} | {FormatTarget(entry.Target)}");
                    }
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

        void HandleActionEvaluated(Brain.ActionEvaluation evaluation)
        {
            if (evaluation.Action == null)
            {
                return;
            }

            if (!_evaluationHistory.TryGetValue(evaluation.Action, out Queue<Brain.ActionEvaluation> history))
            {
                history = new Queue<Brain.ActionEvaluation>();
                _evaluationHistory[evaluation.Action] = history;
            }

            history.Enqueue(evaluation);
            while (history.Count > MaxHistoryEntries)
            {
                history.Dequeue();
            }

            Repaint();
        }

        static string FormatTarget(TargetContext target)
        {
            if (target == null || target.transform == null)
            {
                return "<None>";
            }

            return target.transform.name;
        }
    }
}
#endif
