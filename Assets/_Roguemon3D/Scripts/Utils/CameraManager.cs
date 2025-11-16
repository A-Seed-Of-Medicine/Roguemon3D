using System;
using _PinBoy.Scripts.CharacterMovement;
using _PinBoy.Scripts.Gameplay.Effects;
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
    public Camera mainCamera;
    [SerializeField]
    private CinemachineCamera cinemachineCamera;
    [SerializeField]
    private CinemachineImpulseSource cinemachineImpulse;
    private Vector3 cachedCameraPosition;
    public Action<Vector3, Vector3> OnCameraPositionUpdated;
    [Header("Projection Settings")]
    public float cameraFOV = 80f;
    public float verticalScale = 1.41421356f; // √2
    [Min(0)] public float xSpriteRotationMultiplier = 10f;
    [Min(0)] public float xSpriteRotationOffset = 10f;
    Matrix4x4 baseProj;

    [Header("Hit Stop Settings")]
    public AnimCurveScale hitStopSlow = new () { scale = 0.2f, curve = AnimationCurve.EaseInOut(0, 0, 1, 1) };
    public AnimCurveScale hitCameraZoom = new () { scale = 0.5f, curve = AnimationCurve.EaseInOut(0, 0, 1, 1) };
    public AnimCurveScale hitStopDuration = new () { scale = 0.2f, curve = AnimationCurve.EaseInOut(0, 0, 1, 1) };
    public AnimCurveScale hitShakeIntensity = new () { scale = 0.5f, curve = AnimationCurve.EaseInOut(0, 0, 1, 1) };
    public AnimCurveScale hitShakeDuration = new () { scale = 0.5f, curve = AnimationCurve.EaseInOut(0, 0, 1, 1) };
    [SerializeField] private float damageTakenMultiplier = 1f;
    [SerializeField] private float damageDealtMultiplier = 1f;
    
    [Header("UI Settings")]
    public AnimCurveScale healthBarWidthPerUnit = new () { scale = 1f, curve = AnimationCurve.Linear(0, 0, 1, 1) };
    
    private float stopMultiplier;
    private float timeAccumulated;

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
        if (amount * damageTakenMultiplier < stopMultiplier)
            return;
        stopMultiplier = amount * damageTakenMultiplier;
        timeAccumulated = 0f;
    }
    
    public void AddDamageDealtHitStop(float amount)
    {
        if (amount * damageDealtMultiplier < stopMultiplier)
            return;
        stopMultiplier = amount * damageDealtMultiplier;
        timeAccumulated = 0f;
    }
    
    public void AddHitStop(float amount)
    {
        if (amount * damageDealtMultiplier < stopMultiplier)
            return;
        stopMultiplier = amount;
        timeAccumulated = 0f;
    }

    public bool TryAddHitStopForAgent(AgentController agent, float amount)
    {
        if (!agent || amount <= 0f)
        {
            return false;
        }

        PlayerController target = playerController;
        if (!playerController || !target || agent != target)
            return false;

        AddDamageDealtHitStop(amount);
        return true;
    }
    
    void HitStopUpdate(float deltaTime)
    {
        if (stopMultiplier <= 0f)
            return;
        
        timeAccumulated += deltaTime;
        float t = hitStopDuration.Evaluate(timeAccumulated) * stopMultiplier;
        float zoom = hitCameraZoom.Evaluate(t);
        float scale = hitStopSlow.Evaluate(t);
        Time.timeScale = 1 - scale;
        cinemachineCamera.Lens.FieldOfView = cameraFOV + zoom;
        if (t > 0) return;
        stopMultiplier = 0f;
        Time.timeScale = 1f;
        cinemachineCamera.Lens.FieldOfView = cameraFOV;
        timeAccumulated = 0f;
    }

    public void Update()
    {
        HitStopUpdate(Time.unscaledDeltaTime);
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
        if (mainCamera != null)
        {
            mainCamera.ResetProjectionMatrix();
            baseProj = mainCamera.projectionMatrix; // capture current
        }
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

    public void HandlePlayerDamageTaken(DamageInfo damageInfo)
    {
        if (damageInfo.amount <= 0f)
        {
            return;
        }
        float amount = damageInfo.amount * damageTakenMultiplier;
        
        float shake = hitShakeIntensity.Evaluate(amount);
        cinemachineImpulse.ImpulseDefinition.ImpulseDuration = hitShakeDuration.Evaluate(amount);
        cinemachineImpulse.DefaultVelocity = new Vector3(damageInfo.direction.x, cinemachineImpulse.DefaultVelocity.y, damageInfo.direction.z);
        cinemachineImpulse.GenerateImpulseWithForce(shake);
        

        AddDamageTakenHitStop(damageInfo.amount);
    }

    public void HandlePlayerDamageDealt(DamageInfo damageInfo)
    {
        if (damageInfo.amount <= 0f)
        {
            return;
        }

        AddDamageDealtHitStop(damageInfo.amount);
    }
}
