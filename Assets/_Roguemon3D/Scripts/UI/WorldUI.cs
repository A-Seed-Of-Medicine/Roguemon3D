using System;
using System.Collections.Generic;
using _PinBoy.Scripts.Player;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityUtils;

[DisallowMultipleComponent]
public class WorldUI : MonoBehaviour {
    public enum FacingTarget { Camera, Player, Hybrid }
    
    [Header("References")]
    public Canvas canvas;
    public RectTransform canvasRectTransform;
    
    [Header("Behavior")]
    public PlayerController Player => uiCamera.playerController;
    public CameraManager uiCamera;
    public FacingTarget faceTarget = FacingTarget.Camera;
    
    [Header("UI Fade")]
    public CanvasGroup canvasGroup;
    bool pendingHide;
    public float fadeSmoothTime = 0.2f;
    float targetAlpha, currentAlpha, alphaVelocity;
    
    Action targetAction;

    protected virtual void Start() {
        if (!uiCamera) uiCamera = CameraManager.Instance;
        UpdateTargetAction();
    }

    protected virtual void OnValidate()
    {
        if (!canvas) 
            canvas = GetComponent<Canvas>();
        if (!canvas)
            return;
        
        if (!canvasRectTransform)
            canvasRectTransform = canvas.GetComponent<RectTransform>();
        
        
        canvas.renderMode = RenderMode.WorldSpace;
        if (!uiCamera)
            uiCamera = CameraManager.Instance;
        if (uiCamera)
            canvas.worldCamera = uiCamera.mainCamera;
        if (!canvasGroup) canvas.TryGetComponent(out canvasGroup);

        if (canvasGroup) {
            targetAlpha = canvasGroup.alpha;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        
        switch (faceTarget) {
            case FacingTarget.Camera:
                FaceCamera();
                break;
            case FacingTarget.Player:
                FacePlayer();
                break;
            case FacingTarget.Hybrid:
                FaceHybrid();
                break;
            default:
                FaceCamera();
                break;
        }
    }

    void UpdateTargetAction() {
        targetAction = faceTarget switch {
            FacingTarget.Camera => FaceCamera,
            FacingTarget.Player => FacePlayer,
            FacingTarget.Hybrid => FaceHybrid,
            _ => null
        };
    }
    
    void Update() {
        UpdateTargetAction();

        if (canvasGroup) {
            
            bool interact = currentAlpha > 0.5f;
            canvasGroup.interactable = interact;
            canvasGroup.blocksRaycasts = interact;
        }

        if (pendingHide) {
            bool alphaDone = !canvasGroup || currentAlpha <= 0.01f || Mathf.Approximately(currentAlpha, 0f);

            if (alphaDone) {
                canvas.gameObject.SetActive(false);
                pendingHide = false;
            }
        }
    }

    void FacePlayer() {
        if (!canvas || !Player) return;
        var toPlayer = canvas.transform.position - Player.transform.position;
        var flat = Vector3.ProjectOnPlane(toPlayer, Vector3.up); // pitch=0, roll=0
        if (flat.sqrMagnitude <= Vector3.kEpsilon) return;
        canvas.transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
    }

    void FaceHybrid() {
        if (!canvas || !uiCamera) return;
        var camFwdFlat = Vector3.ProjectOnPlane(uiCamera.transform.forward, Vector3.up);
        if (camFwdFlat.sqrMagnitude <= Vector3.kEpsilon) return;
        canvas.transform.rotation = Quaternion.LookRotation(camFwdFlat, Vector3.up);
    }

    void FaceCamera() {
        if (!canvas || !uiCamera) return;
        canvas.transform.eulerAngles = uiCamera.transform.eulerAngles;
    }

    void LateUpdate() {
        if (canvas.gameObject.activeSelf) targetAction?.Invoke();
    }

    void ShowUI() {
        canvas.gameObject.SetActive(true);
        targetAlpha = 1f;
    }

    void HideUI() {
        targetAlpha = 0f;
    }
}