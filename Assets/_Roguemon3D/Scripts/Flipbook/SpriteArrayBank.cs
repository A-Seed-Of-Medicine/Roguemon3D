// Assets/Scripts/SpriteArrayBank.cs
using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Sprites/Texture2DArray Bank")]
public class SpriteArrayBank : ScriptableObject
{
    public Material material;

    [Tooltip("Name of the Texture2DArray property on the material.")]
    public string arrayPropertyName = "_SpriteArray";

    [Tooltip("World units per sprite pixel, e.g. 100 = Unity default PPU.")]
    public float pixelsPerUnit = 100f;

    [Serializable] public class Clip {
        public string name;
        public int firstLayer;
        public int frameCount;
        public float fps = 10f;
        public bool loop = true;
    }
    public List<Clip> clips = new();

    public Clip GetClip(string clipName) => clips.Find(c => c.name == clipName);
}