// Assets/Editor/ArrayFlipbookAVSEditor.cs
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    [CustomEditor(typeof(ArrayFlipbookAVS))]
    public class ArrayFlipbookAVSEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var t = (ArrayFlipbookAVS)target;
            serializedObject.Update();
            if (GUILayout.Button("Recalculate Quad"))
                t.SetClip(t.clipName, 0, true);

            if (t.bank)
            {
                // Assign material to all targets
                if (t.bank.material)
                {
                    var r = t.renderers;
                    if (r != null)
                    {
                        foreach (var mr in r)
                        {
                            if (!mr) continue;
                            if (mr.sharedMaterial != t.bank.material)
                                mr.sharedMaterial = t.bank.material;
                        }
                    }
                    else
                    {
                        var mr = t.GetComponent<MeshRenderer>();
                        if (mr && mr.sharedMaterial != t.bank.material)
                            mr.sharedMaterial = t.bank.material;
                    }
                }

                // Ensure BaseQuad on all filters
                var fs = t.filters;
                if (fs != null && fs.Length > 0)
                {
                    foreach (var mf in fs)
                    {
                        if (!mf) continue;
                        if (!mf.sharedMesh || mf.sharedMesh != BaseQuad.Shared)
                            mf.sharedMesh = BaseQuad.Shared;
                    }
                }
                else
                {
                    var mf = t.GetComponent<MeshFilter>();
                    if (mf && (!mf.sharedMesh || mf.sharedMesh != BaseQuad.Shared))
                        mf.sharedMesh = BaseQuad.Shared;
                }

                // Clip popup
                var names = t.bank.clips.ConvertAll(c => c.name).ToArray();
                int idx = Mathf.Max(0, System.Array.IndexOf(names, t.clipName));
                int newIdx = EditorGUILayout.Popup("Clip", idx, names);
                if (newIdx >= 0 && newIdx < names.Length && t.clipName != names[newIdx])
                {
                    Undo.RecordObject(t, "Change Clip");
                    t.SetClip(names[newIdx]);
                    EditorUtility.SetDirty(t);
                }

                t.ApplyPPUScale(t.bank);
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("clipName"));
            }

            DrawDefaultInspector();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
