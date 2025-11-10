// Assets/Editor/ArrayFlipbookAVSEditor.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Editor
{
    [CustomEditor(typeof(ArrayFlipbookAVS))]
    public class ArrayFlipbookAVSEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ArrayFlipbookAVS t = (ArrayFlipbookAVS)target;
            if (GUILayout.Button("Recalculate Quad"))
                t.SetClip(t.defaultClip, t.startFrame / (float)t.defaultClip?.FrameCount, true);

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
            
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
