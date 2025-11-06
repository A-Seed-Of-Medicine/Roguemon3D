using System;
using UnityEditor;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Effects.Editor
{
    [CustomPropertyDrawer(typeof(Effect), true)]
    sealed class EffectPropertyDrawer : PropertyDrawer
    {
        const float TypeFieldWidth = 150f;
        static readonly GUIContent HelpContent = new("Select an effect type from the dropdown to configure its properties.");

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property == null)
            {
                return 0f;
            }

            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            float spacing = EditorGUIUtility.standardVerticalSpacing;
            height += spacing;

            if (property.managedReferenceValue == null)
            {
                height += GetHelpBoxHeight();
                return height;
            }

            var iterator = property.Copy();
            var endProperty = iterator.GetEndProperty();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProperty))
            {
                float childHeight = EditorGUI.GetPropertyHeight(iterator, true);
                height += childHeight + spacing;
                enterChildren = false;
            }

            height -= spacing;

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property == null)
            {
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            Rect headerRect = new(position.x, position.y, position.width, lineHeight);
            Rect popupRect = new(headerRect.xMax - TypeFieldWidth, headerRect.y, TypeFieldWidth, lineHeight);
            Rect foldoutRect = new(headerRect.x, headerRect.y, headerRect.width - TypeFieldWidth - 4f, lineHeight);

            Type currentType = property.managedReferenceValue?.GetType();
            string effectName = EffectEditorUtility.GetFriendlyName(currentType);
            GUIContent foldoutLabel = new($"{label.text} ({effectName})");
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, foldoutLabel, true);

            EditorGUI.BeginDisabledGroup(!EffectEditorUtility.HasEffectTypes);
            using (var change = new EditorGUI.ChangeCheckScope())
            {
                int currentIndex = EffectEditorUtility.GetTypeIndex(currentType) + 1;
                int newIndex = EditorGUI.Popup(popupRect, currentIndex, EffectEditorUtility.PopupOptions);
                if (change.changed && newIndex != currentIndex)
                {
                    EffectEditorUtility.AssignType(property, newIndex - 1);
                    currentType = property.managedReferenceValue?.GetType();
                }
            }
            EditorGUI.EndDisabledGroup();

            if (property.isExpanded)
            {
                if (property.managedReferenceValue == null)
                {
                    Rect helpRect = new(position.x, headerRect.yMax + spacing, position.width, GetHelpBoxHeight());
                    EditorGUI.HelpBox(helpRect, HelpContent.text, MessageType.Info);
                }
                else
                {
                    EditorGUI.indentLevel++;
                    float yOffset = headerRect.yMax + spacing;
                    var iterator = property.Copy();
                    var endProperty = iterator.GetEndProperty();
                    bool enterChildren = true;

                    while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProperty))
                    {
                        float childHeight = EditorGUI.GetPropertyHeight(iterator, true);
                        Rect childRect = new(position.x, yOffset, position.width, childHeight);
                        EditorGUI.PropertyField(childRect, iterator, true);
                        yOffset += childHeight + spacing;
                        enterChildren = false;
                    }

                    EditorGUI.indentLevel--;
                }
            }

            EditorGUI.EndProperty();
        }

        static float GetHelpBoxHeight()
        {
            float viewWidth = EditorGUIUtility.currentViewWidth > 0f ? EditorGUIUtility.currentViewWidth : 400f;
            return EditorStyles.helpBox.CalcHeight(HelpContent, viewWidth);
        }
    }
}
