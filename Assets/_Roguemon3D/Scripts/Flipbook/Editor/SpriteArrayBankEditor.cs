#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering; // for GraphicsFormat

[CustomEditor(typeof(SpriteArrayBank))]
public class SpriteArrayBankEditor :  UnityEditor.Editor
{
    const string kArrayPropName = "_SpriteArray"; // material property name
    const float  kMinPreview    = 48f;
    const float  kMaxPreview    = 256f;

    // animation / preview state
    bool   _play = true;
    float  _speed = 1f;
    float  _previewSize = 64f;

    double _lastTime;
    double _accum;

    // scratch texture reused to display slices
    Texture2D _scratch;
    GraphicsFormat _scratchFmt = GraphicsFormat.None;

    // cache per-array to avoid format/size mismatches
    Texture2D GetScratch(Texture2DArray arr)
    {
        if (arr == null) return null;

        if (_scratch == null ||
            _scratch.width != arr.width ||
            _scratch.height != arr.height ||
            _scratchFmt != arr.graphicsFormat)
        {
            if (_scratch != null) DestroyImmediate(_scratch);
            _scratchFmt = arr.graphicsFormat;
            _scratch = new Texture2D(arr.width, arr.height, _scratchFmt, TextureCreationFlags.None);
            _scratch.filterMode = FilterMode.Point;
            _scratch.wrapMode   = TextureWrapMode.Clamp;

            _scratch.hideFlags = HideFlags.HideAndDontSave;
        }
        return _scratch;
    }

    void OnEnable()
    {
        _lastTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += EditorTick;
    }

    void OnDisable()
    {
        EditorApplication.update -= EditorTick;
        if (_scratch) DestroyImmediate(_scratch);
        _scratch = null;
    }

    void EditorTick()
    {
        // drive animation time & repaint
        var now = EditorApplication.timeSinceStartup;
        var dt  = Math.Max(0.0, now - _lastTime);
        _lastTime = now;

        if (_play) _accum += dt * Math.Max(0.0f, _speed);

        // Keep the inspector animating
        Repaint();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Draw the bank fields (material + clips list) via SerializedProperty
        var propMaterial = serializedObject.FindProperty("material");
        var propClips    = serializedObject.FindProperty("clips");

        EditorGUILayout.PropertyField(propMaterial);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("pixelsPerUnit"));

        // Controls for the preview
        using (new EditorGUILayout.HorizontalScope())
        {
            _play = GUILayout.Toggle(_play, _play ? "Pause" : "Play", EditorStyles.miniButtonLeft, GUILayout.Width(60));
            EditorGUI.BeginChangeCheck();
            _speed = EditorGUILayout.Slider(new GUIContent("Speed"), _speed, 0.0f, 4.0f);
        }
        _previewSize = EditorGUILayout.Slider(new GUIContent("Preview Size"), _previewSize, kMinPreview, kMaxPreview);

        EditorGUILayout.Space(4);

        // Material & array lookup
        var bank = (SpriteArrayBank)target;
        Texture2DArray spriteArray = null;
        if (bank.material == null)
        {
            EditorGUILayout.HelpBox("Assign a Material. The inspector will look for a Texture2DArray in the material property named " + kArrayPropName + ".", MessageType.Info);
        }
        else
        {
            if (!bank.material.HasProperty(kArrayPropName))
            {
                EditorGUILayout.HelpBox($"Material \"{bank.material.name}\" has no property \"{kArrayPropName}\". " +
                                        $"Add a Texture2DArray property with that exact name in your Shader Graph material.", MessageType.Warning);
            }
            else
            {
                spriteArray = bank.material.GetTexture(kArrayPropName) as Texture2DArray;
                if (spriteArray == null)
                {
                    EditorGUILayout.HelpBox($"Material \"{bank.material.name}\" has \"{kArrayPropName}\" but it is not a Texture2DArray or is unassigned.", MessageType.Warning);
                }
            }
        }

        // Clips list UI
        EditorGUILayout.PropertyField(propClips, includeChildren: true);
        if (EditorGUI.EndChangeCheck())
        {
            _accum = 0.0; // reset time on material change
        }

        EditorGUILayout.Space(8);

        // If we have an array, preview each clip
        if (spriteArray)
        {
            DrawClipsPreview(bank, spriteArray);
        }

        serializedObject.ApplyModifiedProperties();
    }

    void DrawClipsPreview(SpriteArrayBank bank, Texture2DArray array)
    {
        if (bank.clips == null || bank.clips.Count == 0)
        {
            EditorGUILayout.HelpBox("Add one or more Clips to preview.", MessageType.Info);
            return;
        }

        var scratch = GetScratch(array);
        if (!scratch)
        {
            EditorGUILayout.HelpBox("Failed to create scratch preview texture.", MessageType.Warning);
            return;
        }
        
        // Maintain aspect ratio of the source layer while respecting preview size
        float aspect = array.width / (float)array.height;
        float w = _previewSize;
        float h = Mathf.Floor(w / Mathf.Max(0.01f, aspect));

        foreach (var clip in bank.clips)
        {
            if (clip == null) continue;

            // Header
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent($"{clip.name}  (first:{clip.firstLayer}, frames:{clip.frameCount}, fps:{clip.fps:0.##})"),
                                            EditorStyles.boldLabel);
            }

            // Compute current layer
            int layer = SafeLayerForClip(clip);

            // Guard rails
            if (layer < 0 || layer >= array.depth)
            {
                EditorGUILayout.HelpBox($"Layer {layer} is out of range for this array (depth {array.depth}). Check clip settings.", MessageType.Error);
            }
            else
            {
                // Copy the slice into the scratch texture (GPU copy)
                try
                {
                    Graphics.CopyTexture(array, layer, 0, scratch, 0, 0);
                }
                catch (Exception e)
                {
                    EditorGUILayout.HelpBox("CopyTexture failed: " + e.Message, MessageType.Error);
                }
                float ppp = EditorGUIUtility.pixelsPerPoint; // Retina awareness
                // Draw preview
                Rect r = GUILayoutUtility.GetRect(w/ppp, h/ppp, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(r, scratch, null, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.Space(6);
        }
    }

    int SafeLayerForClip(SpriteArrayBank.Clip clip)
    {
        if (clip == null) return 0;
        int frames = Mathf.Max(1, clip.frameCount);
        // Use accumulated editor time scaled by clip fps and preview speed
        double t = _accum * clip.fps;
        int frameOffset = clip.loop ? (int)Math.Floor(t) % frames : Mathf.Clamp((int)Math.Floor(t), 0, frames - 1);
        return clip.firstLayer + frameOffset;
    }
}
#endif
