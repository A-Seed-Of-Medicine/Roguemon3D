#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace UtilityAI.Editor
{
    [CustomPropertyDrawer(typeof(AIAction), true)]
    public class AIActionDrawer : ManagedReferencePropertyDrawer
    {
        protected override Type BaseType => typeof(AIAction);

        protected override string GetTypeDisplayName(Type type)
        {
            return base.GetTypeDisplayName(type).Replace(" Ai Action", string.Empty);
        }

        protected override float DrawReferenceContent(Rect totalRect, SerializedProperty property, float y)
        {
            y = base.DrawReferenceContent(totalRect, property, y);

            if (Application.isPlaying && property.serializedObject.targetObject is Brain brain)
            {
                var contextRect = new Rect(totalRect.x, y, totalRect.width, EditorGUIUtility.singleLineHeight);
                string label = brain.context != null ? $"Last Utility: {GetLastUtility(property)}" : "Context not initialized";
                EditorGUI.LabelField(contextRect, label, EditorStyles.miniLabel);
                y += EditorGUIUtility.singleLineHeight;
            }

            return y;
        }

        protected override float GetReferenceContentHeight(SerializedProperty property)
        {
            float height = base.GetReferenceContentHeight(property);
            if (Application.isPlaying && property.serializedObject.targetObject is Brain brain && brain.context != null)
            {
                height += EditorGUIUtility.singleLineHeight;
            }

            return height;
        }

        float GetLastUtility(SerializedProperty property)
        {
            var managed = property.managedReferenceValue as AIAction;
            if (managed == null || property.serializedObject.targetObject is not Brain brain)
                return 0f;

            try
            {
                return managed.CalculateUtility(brain.context, brain.GetPerceivedTargets());
            }
            catch
            {
                return 0f;
            }
        }
    }
}
#endif
