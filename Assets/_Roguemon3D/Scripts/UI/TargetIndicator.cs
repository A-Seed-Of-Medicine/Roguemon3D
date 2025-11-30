using UnityEngine;
using UnityEngine.Rendering.Universal;

public class TargetIndicator : MonoBehaviour
{
    private static readonly int TimerStart = Shader.PropertyToID("_TimerStart");
    public SpriteRenderer targetRenderer;
    public MaterialPropertyBlock propertyBlock;
    public float duration = 1f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<SpriteRenderer>();
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    
    void OnValidate()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<SpriteRenderer>();
        ResetShaderTimer();
    }
    
    // Call this method to reset the timer
    public void ResetShaderTimer()
    {
        if (targetRenderer == null)
            return;
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
            targetRenderer.SetPropertyBlock(propertyBlock);
        }

        propertyBlock.SetFloat(TimerStart, Time.timeSinceLevelLoad);
        targetRenderer.SetPropertyBlock(propertyBlock);
    }
}
