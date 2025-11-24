using UnityEditor;
using UnityEngine;

public class PropertyCopyWindow : EditorWindow
{
    private Object source;
    private Object target;

    private string statusMessage = "";

    [MenuItem("Tools/Property Copier")]
    private static void ShowWindow()
    {
        var window = GetWindow<PropertyCopyWindow>();
        window.titleContent = new GUIContent("Property Copier");
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Source and Target", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            source = EditorGUILayout.ObjectField("Source", source, typeof(Object), true);
            target = EditorGUILayout.ObjectField("Target", target, typeof(Object), true);
        }

        bool hasObjects = source != null && target != null;

        if (!hasObjects)
        {
            EditorGUILayout.HelpBox(
                "Assign a source and target object (MonoBehaviour, ScriptableObject, etc.) to copy from/to.",
                MessageType.Info
            );
        }

        using (new EditorGUI.DisabledScope(!hasObjects))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Copies all serialized properties that exist on both objects with the same path and type.\n" +
                "Works across MonoBehaviours, ScriptableObjects, etc.",
                MessageType.None
            );

            if (GUILayout.Button("Copy All Matching Properties"))
            {
                CopyAllMatching();
            }
        }

        if (!string.IsNullOrEmpty(statusMessage))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
        }
    }

    private void CopyAllMatching()
    {
        if (source == null || target == null)
        {
            statusMessage = "Source or target is not assigned.";
            return;
        }

        SerializedObject srcSO = new SerializedObject(source);
        SerializedObject dstSO = new SerializedObject(target);

        srcSO.Update();
        dstSO.Update();

        Undo.RecordObject(dstSO.targetObject, "Copy Matching Properties");

        SerializedProperty iterator = srcSO.GetIterator();
        bool enterChildren = true;

        int totalChecked = 0;
        int copiedCount = 0;

        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;

            // Skip script reference
            if (iterator.name == "m_Script")
                continue;

            totalChecked++;

            SerializedProperty dstProp = dstSO.FindProperty(iterator.propertyPath);
            if (dstProp == null)
                continue;

            if (dstProp.propertyType != iterator.propertyType)
                continue;

            CopySerializedPropertyValue(iterator, dstProp);
            copiedCount++;
        }

        dstSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(dstSO.targetObject);

        statusMessage = $"Copied {copiedCount} matching properties (checked {totalChecked} source properties).";
        Debug.Log(statusMessage, dstSO.targetObject);
    }

    private static void CopySerializedPropertyValue(SerializedProperty source, SerializedProperty target)
    {
        if (source == null || target == null)
            return;

        // Handle arrays (except strings)
        if (source.isArray && source.propertyType != SerializedPropertyType.String)
        {
            target.arraySize = source.arraySize;
            for (int i = 0; i < source.arraySize; i++)
            {
                SerializedProperty srcElement = source.GetArrayElementAtIndex(i);
                SerializedProperty dstElement = target.GetArrayElementAtIndex(i);
                CopySerializedPropertyValue(srcElement, dstElement);
            }

            return;
        }

        switch (source.propertyType)
        {
            case SerializedPropertyType.Integer:
                target.intValue = source.intValue;
                break;
            case SerializedPropertyType.Boolean:
                target.boolValue = source.boolValue;
                break;
            case SerializedPropertyType.Float:
                target.floatValue = source.floatValue;
                break;
            case SerializedPropertyType.String:
                target.stringValue = source.stringValue;
                break;
            case SerializedPropertyType.Color:
                target.colorValue = source.colorValue;
                break;
            case SerializedPropertyType.ObjectReference:
                target.objectReferenceValue = source.objectReferenceValue;
                break;
            case SerializedPropertyType.LayerMask:
                target.intValue = source.intValue;
                break;
            case SerializedPropertyType.Enum:
                target.enumValueIndex = source.enumValueIndex;
                break;
            case SerializedPropertyType.Vector2:
                target.vector2Value = source.vector2Value;
                break;
            case SerializedPropertyType.Vector3:
                target.vector3Value = source.vector3Value;
                break;
            case SerializedPropertyType.Vector4:
                target.vector4Value = source.vector4Value;
                break;
            case SerializedPropertyType.Rect:
                target.rectValue = source.rectValue;
                break;
            case SerializedPropertyType.AnimationCurve:
                target.animationCurveValue = source.animationCurveValue;
                break;
            case SerializedPropertyType.Bounds:
                target.boundsValue = source.boundsValue;
                break;
            case SerializedPropertyType.Quaternion:
                target.quaternionValue = source.quaternionValue;
                break;
#if UNITY_2017_2_OR_NEWER
            case SerializedPropertyType.Vector2Int:
                target.vector2IntValue = source.vector2IntValue;
                break;
            case SerializedPropertyType.Vector3Int:
                target.vector3IntValue = source.vector3IntValue;
                break;
            case SerializedPropertyType.RectInt:
                target.rectIntValue = source.rectIntValue;
                break;
            case SerializedPropertyType.BoundsInt:
                target.boundsIntValue = source.boundsIntValue;
                break;
#endif
#if UNITY_2021_1_OR_NEWER
            case SerializedPropertyType.Hash128:
                target.hash128Value = source.hash128Value;
                break;
#endif
            default:
                Debug.LogWarning(
                    $"Unsupported property type '{source.propertyType}' for '{source.propertyPath}'. " +
                    "Extend CopySerializedPropertyValue if needed."
                );
                break;
        }
    }
}
