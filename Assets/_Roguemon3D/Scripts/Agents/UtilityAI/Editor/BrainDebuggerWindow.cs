#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UtilityAI;
using _PinBoy.Scripts.CharacterMovement;

namespace _PinBoy.Scripts.Agents.UtilityAI.Editor
{
    public class BrainDebuggerWindow : EditorWindow
    {
        static readonly GUIContent WindowTitle = new("Utility Brain Debugger");
        static readonly Color SelectedBrainColor = new(0.3f, 0.55f, 0.85f, 0.85f);
        const double BrainRefreshInterval = 1.0;

        Brain selectedBrain;
        Brain[] cachedBrains = Array.Empty<Brain>();
        Vector2 brainScroll;
        Vector2 detailScroll;
        double nextBrainRefresh;
        bool followSceneSelection = true;
        bool showBrainList = true;
        UnityEditor.Editor cachedEditor;

        [MenuItem("Tools/AI/Utility Brain Debugger")]
        public static void Open()
        {
            var window = GetWindow<BrainDebuggerWindow>();
            window.titleContent = WindowTitle;
            window.Show();
        }

        void OnEnable()
        {
            titleContent = WindowTitle;
            RefreshBrainCache(true);
            EditorApplication.update += HandleEditorUpdate;
            Selection.selectionChanged += HandleSelectionChanged;
        }

        void OnDisable()
        {
            EditorApplication.update -= HandleEditorUpdate;
            Selection.selectionChanged -= HandleSelectionChanged;
            SetSelectedBrain(null);
        }

        void HandleEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup >= nextBrainRefresh)
            {
                RefreshBrainCache();
            }

            if (!selectedBrain && followSceneSelection)
            {
                TryAdoptSelection();
            }

            if (EditorApplication.isPlaying)
            {
                Repaint();
            }
        }

        void HandleSelectionChanged()
        {
            if (!followSceneSelection)
            {
                return;
            }

            TryAdoptSelection();
        }

        void TryAdoptSelection()
        {
            var active = Selection.activeGameObject;
            if (active == null)
            {
                return;
            }

            Brain brain = active.GetComponentInParent<Brain>();
            if (brain == null)
            {
                AgentController controller = active.GetComponentInParent<AgentController>();
                if (controller != null)
                {
                    brain = controller.GetComponentInChildren<Brain>();
                }
            }

            if (brain != null)
            {
                SetSelectedBrain(brain);
            }
        }

        void RefreshBrainCache(bool force = false)
        {
            if (!force && EditorApplication.timeSinceStartup < nextBrainRefresh)
            {
                return;
            }

            nextBrainRefresh = EditorApplication.timeSinceStartup + BrainRefreshInterval;

            var allBrains = Resources.FindObjectsOfTypeAll<Brain>();
            cachedBrains = allBrains
                .Where(brain => brain != null && !EditorUtility.IsPersistent(brain) && brain.gameObject.scene.IsValid())
                .Distinct()
                .OrderBy(brain => brain.gameObject.scene.name)
                .ThenBy(brain => GetControllerName(brain))
                .ThenBy(brain => brain.name)
                .ToArray();

            if (selectedBrain && !cachedBrains.Contains(selectedBrain))
            {
                SetSelectedBrain(null);
            }

            Repaint();
        }

        void SetSelectedBrain(Brain brain)
        {
            if (selectedBrain == brain)
            {
                return;
            }

            selectedBrain = brain;
            if (cachedEditor != null)
            {
                DestroyImmediate(cachedEditor);
            }

            cachedEditor = selectedBrain != null ? UnityEditor.Editor.CreateEditor(selectedBrain) : null;
            Repaint();
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawBrainSelection();
                EditorGUILayout.Space();
                DrawBrainDetails();
            }
        }

        void DrawBrainSelection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(320f)))
            {
                EditorGUILayout.LabelField("Brain Selection", EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                Brain brainField = (Brain)EditorGUILayout.ObjectField("Brain", selectedBrain, typeof(Brain), true);
                if (EditorGUI.EndChangeCheck())
                {
                    SetSelectedBrain(brainField);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    followSceneSelection = EditorGUILayout.ToggleLeft("Follow Scene Selection", followSceneSelection);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
                    {
                        RefreshBrainCache(true);
                    }
                }

                showBrainList = EditorGUILayout.ToggleLeft("Show Scene Brains", showBrainList);
                if (!showBrainList)
                {
                    return;
                }

                if (cachedBrains.Length == 0)
                {
                    EditorGUILayout.HelpBox("No Brain instances found in the open scenes.", MessageType.Info);
                    return;
                }

                brainScroll = EditorGUILayout.BeginScrollView(brainScroll, GUILayout.MinHeight(Mathf.Min(240f, 24f * cachedBrains.Length + 8f)));
                foreach (Brain brain in cachedBrains)
                {
                    if (!brain)
                    {
                        continue;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string sceneName = brain.gameObject.scene.IsValid() ? brain.gameObject.scene.name : "<No Scene>";
                        EditorGUILayout.LabelField(sceneName, EditorStyles.miniLabel, GUILayout.Width(110f));

                        Color previous = GUI.color;
                        if (brain == selectedBrain)
                        {
                            GUI.color = SelectedBrainColor;
                        }

                        string controllerName = GetControllerName(brain);
                        string label = string.IsNullOrEmpty(controllerName) ? brain.name : $"{controllerName} / {brain.name}";
                        if (GUILayout.Button(label, GUILayout.ExpandWidth(true)))
                        {
                            SetSelectedBrain(brain);
                            Selection.activeGameObject = brain.gameObject;
                            EditorGUIUtility.PingObject(brain);
                        }

                        GUI.color = previous;

                        if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(50f)))
                        {
                            EditorGUIUtility.PingObject(brain);
                            Selection.activeGameObject = brain.gameObject;
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        void DrawBrainDetails()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Brain Details", EditorStyles.boldLabel);

                if (!selectedBrain)
                {
                    EditorGUILayout.HelpBox("Select a Brain to inspect its utility actions and runtime status.", MessageType.Info);
                    return;
                }

                detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                cachedEditor?.OnInspectorGUI();
                EditorGUILayout.EndScrollView();
            }
        }

        static string GetControllerName(Brain brain)
        {
            if (brain == null)
            {
                return string.Empty;
            }

            AgentController controller = brain.controller ? brain.controller : brain.GetComponentInParent<AgentController>();
            return controller != null ? controller.name : string.Empty;
        }
    }
}
#endif
