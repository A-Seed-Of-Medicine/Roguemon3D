using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class SplitAnimationByEventsWindow : EditorWindow
{
    private AnimationClip sourceClip;

    [MenuItem("Tools/Animation/Split Clip By Events")]
    private static void ShowWindow()
    {
        GetWindow<SplitAnimationByEventsWindow>("Split Clip By Events");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField(
            "Split an AnimationClip into multiple clips based on its Animation Events.\n" +
            "Each segment starts at an event and ends at the next event (or clip end).",
            EditorStyles.wordWrappedLabel);

        EditorGUILayout.Space();

        sourceClip = (AnimationClip)EditorGUILayout.ObjectField(
            "Source Clip",
            sourceClip,
            typeof(AnimationClip),
            false);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(sourceClip == null))
        {
            if (GUILayout.Button("Split Clip By Events"))
            {
                SplitClipByEvents();
            }
        }
    }

    private void SplitClipByEvents()
    {
        var events = AnimationUtility.GetAnimationEvents(sourceClip);
        if (events == null || events.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "No Animation Events",
                "The selected clip has no Animation Events to split on.",
                "OK");
            return;
        }

        // Ensure events are sorted by time
        System.Array.Sort(events, (a, b) => a.time.CompareTo(b.time));

        string assetPath = AssetDatabase.GetAssetPath(sourceClip);
        if (string.IsNullOrEmpty(assetPath))
        {
            EditorUtility.DisplayDialog(
                "Invalid Clip",
                "The selected clip is not an asset on disk.",
                "OK");
            return;
        }

        string directory = Path.GetDirectoryName(assetPath).Replace("\\", "/");

        for (int i = 0; i < events.Length; i++)
        {
            AnimationEvent evt = events[i];

            float startTime = evt.time;
            float endTime = (i < events.Length - 1) ? events[i + 1].time : sourceClip.length;

            // Skip degenerate segments
            if (endTime <= startTime + Mathf.Epsilon)
                continue;

            string eventName = string.IsNullOrEmpty(evt.functionName) ? $"Event{i}" : evt.functionName;

            // Name: original clip name + event name (+ index for uniqueness)
            string clipName = $"{sourceClip.name}_{eventName}";

            AnimationClip newClip = CreateSubClip(sourceClip, startTime, endTime, clipName);

            // Remove animation events from the new clip
            AnimationUtility.SetAnimationEvents(newClip, new AnimationEvent[0]);

            string newPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{clipName}.anim");
            AssetDatabase.CreateAsset(newClip, newPath);

            Debug.Log($"Created split clip '{clipName}' at {newPath} (from {startTime:F3} to {endTime:F3}).");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// Creates a new clip that is a sub-section of the source clip between startTime and endTime.
    /// Times in the new clip are re-based so that startTime -> 0.
    /// Curves are sampled at the start and end times so segments can play seamlessly back-to-back.
    /// </summary>
    private static AnimationClip CreateSubClip(AnimationClip source, float startTime, float endTime, string newName)
    {
        float duration = endTime - startTime;

        var newClip = new AnimationClip
        {
            name = newName,
            frameRate = source.frameRate,
            legacy = source.legacy,
            wrapMode = source.wrapMode,
            localBounds = source.localBounds
        };

        // 1) Float curves
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            AnimationCurve sourceCurve = AnimationUtility.GetEditorCurve(source, binding);
            if (sourceCurve == null)
                continue;

            var newCurve = new AnimationCurve();

            // Key at local time 0 so clip starts with the value at startTime
            float startValue = sourceCurve.Evaluate(startTime);
            newCurve.AddKey(new Keyframe(0f, startValue));

            // Keys from inside the interval
            foreach (Keyframe key in sourceCurve.keys)
            {
                if (key.time > startTime && key.time < endTime)
                {
                    Keyframe shifted = key;
                    shifted.time -= startTime;
                    newCurve.AddKey(shifted);
                }
            }

            // Key at the end of the segment so it matches the value at endTime
            if (duration > 0f)
            {
                float endValue = sourceCurve.Evaluate(endTime);
                newCurve.AddKey(new Keyframe(duration, endValue));
            }

            AnimationUtility.SetEditorCurve(newClip, binding, newCurve);
        }

        // 2) Object reference curves (e.g., sprite swaps)
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
        {
            ObjectReferenceKeyframe[] sourceKeys = AnimationUtility.GetObjectReferenceCurve(source, binding);
            if (sourceKeys == null || sourceKeys.Length == 0)
                continue;

            var newKeys = new List<ObjectReferenceKeyframe>();

            // Add a key at local time 0 using the last reference before or at startTime
            if (TryGetObjectKeyAtOrBeforeTime(sourceKeys, startTime, out ObjectReferenceKeyframe startKey))
            {
                startKey.time = 0f;
                newKeys.Add(startKey);
            }

            // Keys strictly inside the interval
            foreach (var key in sourceKeys)
            {
                if (key.time > startTime && key.time < endTime)
                {
                    var shifted = key;
                    shifted.time -= startTime;
                    newKeys.Add(shifted);
                }
            }

            if (newKeys.Count > 0)
            {
                AnimationUtility.SetObjectReferenceCurve(newClip, binding, newKeys.ToArray());
            }
        }

        // Improve continuity for rotation curves
        newClip.EnsureQuaternionContinuity();

        return newClip;
    }

    /// <summary>
    /// Gets the last ObjectReferenceKeyframe whose time is <= given time.
    /// If none, returns the first key.
    /// </summary>
    private static bool TryGetObjectKeyAtOrBeforeTime(ObjectReferenceKeyframe[] keys, float time, out ObjectReferenceKeyframe result)
    {
        bool found = false;
        result = default;

        foreach (var key in keys)
        {
            if (key.time <= time)
            {
                if (!found || key.time > result.time)
                {
                    result = key;
                    found = true;
                }
            }
        }

        if (!found && keys.Length > 0)
        {
            result = keys[0];
            found = true;
        }

        return found;
    }
}
