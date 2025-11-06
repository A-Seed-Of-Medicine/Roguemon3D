using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace _PinBoy.Scripts.Gameplay.Effects.Editor
{
    static class EffectEditorUtility
    {
        static readonly Type[] effectTypes;
        static readonly string[] effectTypeDisplayNames;
        static readonly GUIContent[] effectTypePopupOptions;

        static EffectEditorUtility()
        {
            effectTypes = TypeCache.GetTypesDerivedFrom<Effect>()
                .Where(type => !type.IsAbstract && !type.IsGenericType && typeof(Effect).IsAssignableFrom(type))
                .Where(type => type.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(type => type.Name)
                .ToArray();

            effectTypeDisplayNames = effectTypes
                .Select(GetFriendlyName)
                .ToArray();

            effectTypePopupOptions = new GUIContent[effectTypeDisplayNames.Length + 1];
            effectTypePopupOptions[0] = new GUIContent("None");
            for (int i = 0; i < effectTypeDisplayNames.Length; i++)
            {
                effectTypePopupOptions[i + 1] = new GUIContent(effectTypeDisplayNames[i]);
            }
        }

        public static IReadOnlyList<Type> EffectTypes => effectTypes;
        public static GUIContent[] PopupOptions => effectTypePopupOptions;

        public static bool HasEffectTypes => effectTypes.Length > 0;

        public static string GetFriendlyName(Type type)
        {
            if (type == null)
            {
                return "None";
            }

            string nicified = UnityEditor.ObjectNames.NicifyVariableName(type.Name);
            return nicified;
        }

        public static int GetTypeIndex(Type type)
        {
            if (type == null)
            {
                return -1;
            }

            for (int i = 0; i < effectTypes.Length; i++)
            {
                if (effectTypes[i] == type)
                {
                    return i;
                }
            }

            return -1;
        }

        public static Type GetTypeAtIndex(int index)
        {
            if (index < 0 || index >= effectTypes.Length)
            {
                return null;
            }

            return effectTypes[index];
        }

        public static void AssignType(SerializedProperty property, int typeIndex)
        {
            if (property == null)
            {
                return;
            }

            property.serializedObject.Update();
            var targets = property.serializedObject.targetObjects;
            if (targets is { Length: > 0 })
            {
                Undo.RecordObjects(targets, "Change Effect Type");
            }

            if (typeIndex < 0)
            {
                property.managedReferenceValue = null;
                property.isExpanded = false;
            }
            else
            {
                Type type = GetTypeAtIndex(typeIndex);
                if (type == null)
                {
                    property.managedReferenceValue = null;
                }
                else
                {
                    object instance = Activator.CreateInstance(type);
                    property.managedReferenceValue = instance;
                    property.isExpanded = true;
                }
            }

            property.serializedObject.ApplyModifiedProperties();
            property.serializedObject.Update();
        }

        public static void AddEffectInstance(SerializedProperty listProperty, Type type)
        {
            if (listProperty == null)
            {
                return;
            }

            SerializedObject serializedObject = listProperty.serializedObject;
            serializedObject.Update();
            var targets = serializedObject.targetObjects;
            if (targets is { Length: > 0 })
            {
                Undo.RecordObjects(targets, "Add Effect");
            }

            int newIndex = listProperty.arraySize;
            listProperty.arraySize++;
            SerializedProperty element = listProperty.GetArrayElementAtIndex(newIndex);
            element.managedReferenceValue = type != null ? Activator.CreateInstance(type) : null;

            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
        }
    }
}
