using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace _PinBoy.Scripts.Utils.Editor
{
    public class SpriteShadowTool : EditorWindow
    {
        Transform rootTransform;

        [MenuItem("Tools/Sprite Shadow Tool")]
        static void ShowWindow()
        {
            SpriteShadowTool window = GetWindow<SpriteShadowTool>();
            window.titleContent = new GUIContent("Sprite Shadows");
            window.Show();
        }

        void OnSelectionChange()
        {
            if (rootTransform == null && Selection.activeTransform != null)
            {
                rootTransform = Selection.activeTransform;
            }

            Repaint();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Sprite Shadow Utility", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Set shadow settings on Sprite Renderers in a hierarchy or across the entire scene.", MessageType.Info);

            EditorGUILayout.Space();

            rootTransform = (Transform)EditorGUILayout.ObjectField("Root Transform", rootTransform, typeof(Transform), true);

            using (new EditorGUI.DisabledScope(rootTransform == null && Selection.activeTransform == null))
            {
                if (GUILayout.Button("Apply To Selected Transform Hierarchy"))
                {
                    Transform target = rootTransform != null ? rootTransform : Selection.activeTransform;
                    ApplyToHierarchy(target);
                }
            }

            if (GUILayout.Button("Apply To Entire Scene"))
            {
                ApplyToScene();
            }
        }

        void ApplyToHierarchy(Transform target)
        {
            if (target == null)
            {
                ShowNotification(new GUIContent("Select a transform first."));
                return;
            }

            SpriteRenderer[] renderers = target.GetComponentsInChildren<SpriteRenderer>(true);
            ApplySettings(renderers);
        }

        void ApplyToScene()
        {
            SpriteRenderer[] renderers = FindObjectsOfType<SpriteRenderer>(true);
            ApplySettings(renderers);
        }

        static void ApplySettings(IEnumerable<SpriteRenderer> renderers)
        {
            foreach (SpriteRenderer spriteRenderer in renderers)
            {
                if (spriteRenderer == null)
                {
                    continue;
                }

                Undo.RecordObject(spriteRenderer, "Update Sprite Shadows");
                spriteRenderer.receiveShadows = true;
                spriteRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                EditorUtility.SetDirty(spriteRenderer);
            }
        }
    }
}
