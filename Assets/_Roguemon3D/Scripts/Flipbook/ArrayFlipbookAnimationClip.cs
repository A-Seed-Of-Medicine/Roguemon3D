using System;
using UnityEngine;

[Serializable]
public class ArrayFlipbookAnimationClip
{
    [SerializeField] private Texture2DArray textureArray;
    [SerializeField, Min(0f)] private float framesPerSecond = 10f;
    [SerializeField] private bool loop = true;
    [SerializeField, Min(0f)] private float pixelsPerUnit = 100f;

    public Texture2DArray TextureArray => textureArray;
    public float FramesPerSecond => Mathf.Max(0f, framesPerSecond);
    public bool Loop => loop;
    public float PixelsPerUnit => Mathf.Max(0.0001f, pixelsPerUnit);
    public int FrameCount => textureArray ? Mathf.Max(1, textureArray.depth) : 0;
    public bool IsValid => textureArray != null && FrameCount > 0;

    public void ApplyTexture(MeshRenderer renderer, string propertyName)
    {
        if (!renderer)
        {
            return;
        }

        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetTexture(propertyName, textureArray);
        renderer.SetPropertyBlock(block);
    }
}
