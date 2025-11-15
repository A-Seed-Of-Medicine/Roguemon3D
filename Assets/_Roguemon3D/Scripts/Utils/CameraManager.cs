using System;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Player;
using _Roguemon3D.Scripts.Utils;
using ImprovedTimers;
using Unity.Cinemachine;
using UnityEngine;

[ExecuteAlways]
public class CameraManager : MonoBehaviour
{
    public static CameraManager Instance { get; private set; }
    
    public PlayerController playerController;

    [SerializeField]
    private Camera mainCamera;
    [SerializeField]
    private CinemachineCamera cinemachineCamera;
    [SerializeField]
    private CinemachineBasicMultiChannelPerlin cinemachineNoise;
    private Vector3 cachedCameraPosition;
    public Action<Vector3, Vector3> OnCameraPositionUpdated;
    public float cameraFOV = 80f;
    public float verticalScale = 1.41421356f; // √2
    Matrix4x4 baseProj;

    [Header("Hit Stop Settings")]
    public AnimCurveScale hitStopScale = new () { scale = 0.2f, curve = AnimationCurve.EaseInOut(0, 0, 1, 1) };
    public AnimCurveScale hitStopZoom = new () { scale = 0.5f, curve = AnimationCurve.EaseInOut(0, 0, 1, 1) };
    public AnimCurveScale hitStopShake = new () { scale = 0.5f, curve = AnimationCurve.EaseInOut(0, 0, 1, 1) };
    public AnimCurveScale hitStopDecay = new () { scale = 0.2f, curve = AnimationCurve.EaseInOut(0, 0, 1, 1) };

    [SerializeField]
    private float damageTakenMultiplier = 1f;
    [SerializeField]
    private float damageDealtMultiplier = 1f;
    
    private float stopAccumulated;

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
        
        if (!mainCamera) mainCamera = Camera.main;
    }

    private void OnValidate()
    {
        if (cinemachineCamera)
        {
            cinemachineCamera.Lens.FieldOfView = cameraFOV;
        }
        OnPreCull();
    }
    
    public void AddDamageTakenHitStop(float amount)
    {
        stopAccumulated += amount * damageTakenMultiplier;
    }
    
    public void AddDamageDealtHitStop(float amount)
    {
        stopAccumulated += amount * damageDealtMultiplier;
    }
    
    void HitStopUpdate(float deltaTime)
    {
        if (stopAccumulated <= 0f)
            return;

        float zoom = hitStopZoom.InverseEvaluate(stopAccumulated);
        float scale = hitStopScale.InverseEvaluate(stopAccumulated);
        float shake = hitStopShake.InverseEvaluate(stopAccumulated);
        Time.timeScale = scale;
        cinemachineNoise.AmplitudeGain = shake;
        cinemachineCamera.Lens.FieldOfView = cameraFOV + zoom;
        
        stopAccumulated -= deltaTime * hitStopDecay.Evaluate(stopAccumulated);
        if (stopAccumulated < 0f)
            stopAccumulated = 0f;
    }

    public void Update()
    {
        HitStopUpdate(Time.deltaTime);   
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
