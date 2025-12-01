using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ProceduralMeshGenerator))]
public class ProceduralMeshGeneratorEditor : Editor
{
    // Mesh properties
    SerializedProperty shapeProp, tilingProp, uvRotationProp, autoUpdateProp;
    SerializedProperty quadSizeProp, borderProp;
    SerializedProperty innerRadiusProp, outerRadiusProp, angleProp, segmentsProp, ringsProp;
    SerializedProperty cylinderHeightProp, cylinderSegmentsProp, cylinderRingsProp, cylinderRadiusProp, cylinderProfileProp;
    SerializedProperty sphereSegmentsProp, sphereRingsProp, sphereRadiusProp;

    // Trigger volume properties
    SerializedProperty generateTriggerVolumeProp, triggerHeightProp, triggerSizeOffsetProp;
    SerializedProperty radialTriggerSegmentsOverrideProp, cylinderTriggerSlicesOverrideProp;
    SerializedProperty alignTriggerVolumeToParticleRotationProp, triggerVolumeIsTriggerProp;
    SerializedProperty triggerVolumeRootProp, triggerLayerMaskProp, triggerCollidersProp;
    SerializedProperty particleSystemProp, subEmittersProp;

    static bool triggerFoldout = true;

    private void OnEnable()
    {
        shapeProp = serializedObject.FindProperty("shape");
        tilingProp = serializedObject.FindProperty("tiling");
        uvRotationProp = serializedObject.FindProperty("uvRotation");
        autoUpdateProp = serializedObject.FindProperty("autoUpdate");

        quadSizeProp = serializedObject.FindProperty("quadSize");
        borderProp = serializedObject.FindProperty("border");

        innerRadiusProp = serializedObject.FindProperty("innerRadius");
        outerRadiusProp = serializedObject.FindProperty("outerRadius");
        angleProp = serializedObject.FindProperty("angle");
        segmentsProp = serializedObject.FindProperty("segments");
        ringsProp = serializedObject.FindProperty("rings");

        cylinderHeightProp = serializedObject.FindProperty("cylinderHeight");
        cylinderSegmentsProp = serializedObject.FindProperty("cylinderSegments");
        cylinderRingsProp = serializedObject.FindProperty("cylinderRings");
        cylinderRadiusProp = serializedObject.FindProperty("cylinderRadius");
        cylinderProfileProp = serializedObject.FindProperty("cylinderProfile");

        sphereSegmentsProp = serializedObject.FindProperty("sphereSegments");
        sphereRingsProp = serializedObject.FindProperty("sphereRings");
        sphereRadiusProp = serializedObject.FindProperty("sphereRadius");

        generateTriggerVolumeProp = serializedObject.FindProperty("generateTriggerVolume");
        triggerHeightProp = serializedObject.FindProperty("triggerHeight");
        triggerSizeOffsetProp = serializedObject.FindProperty("triggerSizeOffset");
        radialTriggerSegmentsOverrideProp = serializedObject.FindProperty("radialTriggerSegmentsOverride");
        cylinderTriggerSlicesOverrideProp = serializedObject.FindProperty("cylinderTriggerSlicesOverride");
        alignTriggerVolumeToParticleRotationProp = serializedObject.FindProperty("alignTriggerVolumeToParticleRotation");
        triggerVolumeIsTriggerProp = serializedObject.FindProperty("triggerVolumeIsTrigger");
        triggerVolumeRootProp = serializedObject.FindProperty("triggerVolumeRoot");
        triggerLayerMaskProp = serializedObject.FindProperty("triggerLayerMask");
        triggerCollidersProp = serializedObject.FindProperty("colliders");
        particleSystemProp = serializedObject.FindProperty("particleSystem");
        subEmittersProp = serializedObject.FindProperty("subEmitterSystems");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var shape = (ProceduralMeshGenerator.ShapeType)shapeProp.enumValueIndex;

        EditorGUILayout.PropertyField(shapeProp);
        EditorGUILayout.PropertyField(tilingProp);
        EditorGUILayout.PropertyField(uvRotationProp);
        EditorGUILayout.PropertyField(autoUpdateProp);
        EditorGUILayout.Space();

        float min = innerRadiusProp.floatValue;
        float max = outerRadiusProp.floatValue;

        switch (shape)
        {
            case ProceduralMeshGenerator.ShapeType.Quad:
                EditorGUILayout.LabelField("Quad Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(quadSizeProp);
                break;

            case ProceduralMeshGenerator.ShapeType.NineSliceQuad:
                EditorGUILayout.LabelField("Nine Slice Quad Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(quadSizeProp);
                EditorGUILayout.PropertyField(borderProp);
                break;

            case ProceduralMeshGenerator.ShapeType.Radial:
                EditorGUILayout.LabelField("Radial Settings", EditorStyles.boldLabel);

                EditorGUILayout.MinMaxSlider(new GUIContent("Radius Range"), ref min, ref max, 0f, 10f);

                min = Mathf.Max(0.001f, min);
                max = Mathf.Max(min + 0.001f, max);
                innerRadiusProp.floatValue = min;
                outerRadiusProp.floatValue = max;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Inner", GUILayout.Width(50));
                innerRadiusProp.floatValue = EditorGUILayout.FloatField(innerRadiusProp.floatValue);
                EditorGUILayout.LabelField("Outer", GUILayout.Width(50));
                outerRadiusProp.floatValue = EditorGUILayout.FloatField(outerRadiusProp.floatValue);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(angleProp);
                EditorGUILayout.IntSlider(segmentsProp, 3, 256);
                EditorGUILayout.IntSlider(ringsProp, 1, 64);
                break;

            case ProceduralMeshGenerator.ShapeType.Cylinder:
                EditorGUILayout.LabelField("Cylinder Settings", EditorStyles.boldLabel);

                EditorGUILayout.PropertyField(cylinderHeightProp);
                EditorGUILayout.IntSlider(cylinderSegmentsProp, 3, 128);
                EditorGUILayout.IntSlider(cylinderRingsProp, 1, 64);
                EditorGUILayout.LabelField("Radius Profile", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(cylinderRadiusProp);
                EditorGUILayout.PropertyField(cylinderProfileProp);
                break;

            case ProceduralMeshGenerator.ShapeType.Sphere:
                EditorGUILayout.LabelField("Sphere Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(sphereRadiusProp);
                EditorGUILayout.IntSlider(sphereSegmentsProp, 3, 128);
                EditorGUILayout.IntSlider(sphereRingsProp, 2, 128);
                break;
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        triggerFoldout = EditorGUILayout.Foldout(triggerFoldout, "Trigger Volume (Collision)", true);
        if (triggerFoldout)
        {
            EditorGUILayout.PropertyField(generateTriggerVolumeProp);
            using (new EditorGUI.DisabledScope(!generateTriggerVolumeProp.boolValue))
            {
                EditorGUILayout.PropertyField(triggerHeightProp, new GUIContent("Height (Planar)"));
                EditorGUILayout.PropertyField(triggerSizeOffsetProp, new GUIContent("Size Offset"));
                EditorGUILayout.PropertyField(radialTriggerSegmentsOverrideProp, new GUIContent("Radial Segments Override"));
                EditorGUILayout.PropertyField(cylinderTriggerSlicesOverrideProp, new GUIContent("Cylinder Slices Override"));

                EditorGUILayout.PropertyField(alignTriggerVolumeToParticleRotationProp);
                EditorGUILayout.PropertyField(triggerVolumeIsTriggerProp);
                EditorGUILayout.PropertyField(triggerVolumeRootProp);
                EditorGUILayout.PropertyField(triggerLayerMaskProp);
                EditorGUILayout.PropertyField(triggerCollidersProp);
            }

            EditorGUILayout.Space(5);
            if (GUILayout.Button("Rebuild Trigger Volume"))
                ((ProceduralMeshGenerator)target).UpdateTriggerVolume(true);
        }
        
        EditorGUILayout.PropertyField(particleSystemProp);
        EditorGUILayout.PropertyField(subEmittersProp, true);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        if (GUILayout.Button("Bake Mesh to Asset"))
        {
            ProceduralMeshBakeWindow.Open((ProceduralMeshGenerator)target);
        }
        if (GUILayout.Button("Reassign Particle Mesh"))
        {
            ((ProceduralMeshGenerator)target).ReassignParticleMesh();
        }

        serializedObject.ApplyModifiedProperties();
    }
}
