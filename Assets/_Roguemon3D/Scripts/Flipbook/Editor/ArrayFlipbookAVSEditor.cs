// Assets/Editor/ArrayFlipbookAVSEditor.cs
#if UNITY_EDITOR
using UnityEditor;

namespace Editor
{
    [CustomEditor(typeof(ArrayFlipbookAVS))]
    public class ArrayFlipbookAVSEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
