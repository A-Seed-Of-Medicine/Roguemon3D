using System;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Player;
using UnityEngine;

[ExecuteAlways]
public class CameraManager : MonoBehaviour
{
    public static CameraManager Instance { get; private set; }
    
    public PlayerController playerController;

    private Camera mainCamera;
    private Vector3 cachedCameraPosition;
    public Action<Vector3, Vector3> OnCameraPositionUpdated;
    public float verticalScale = 1.41421356f; // √2
    Matrix4x4 baseProj;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (Application.isPlaying)
            DontDestroyOnLoad(gameObject);

        mainCamera = Camera.main;
    }

    public Camera GetMainCamera()
    {
        return mainCamera;
    }
    
    void LateUpdate()
    {
        if (!mainCamera) return;
        if (mainCamera.transform.position != cachedCameraPosition)
        {
            cachedCameraPosition = mainCamera.transform.position;
            OnCameraPositionUpdated?.Invoke(cachedCameraPosition, playerController.transform.position);
        }
        OnPreCull();
    }

    void OnEnable()
    {
        if (!mainCamera) mainCamera = Camera.main;
        mainCamera.ResetProjectionMatrix();
        baseProj = mainCamera.projectionMatrix; // capture current
    }

    void OnDisable()
    {
        if (mainCamera != null) mainCamera.ResetProjectionMatrix();
    }

    void OnPreCull()
    {
        // Rebuild from current in case size/aspect changed
        mainCamera.ResetProjectionMatrix();
        baseProj = mainCamera.projectionMatrix;

        var S = Matrix4x4.identity;
        S[1,1] = verticalScale;   // scale Y in clip space
        mainCamera.projectionMatrix = S * baseProj;
    }
}
