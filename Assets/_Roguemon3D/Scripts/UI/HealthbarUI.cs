using _PinBoy.Scripts.Gameplay.Effects;
using UnityEngine;
using UnityEngine.UI;

public class HealthbarUI : WorldUI {
    public Slider slider;
    
    public void SetHealthPercent(Health health) 
    {
        if (slider) 
            slider.value = health.Ratio;
        // Set canvas width based on health bar max and camera manager settings
        canvasRectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, CameraManager.Instance.healthBarWidthPerUnit.InverseEvaluate(health.Max));
    }
    
}