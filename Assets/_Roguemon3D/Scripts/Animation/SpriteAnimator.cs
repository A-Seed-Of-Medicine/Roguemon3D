using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityUtils;

namespace _PinBoy.Scripts.Animation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class SpriteAnimator : MonoBehaviour
    {
        [Header("Animation")]
        [SerializeField] private AnimationClip defaultClip;
        [SerializeField] private bool playOnAwake = true;
        [SerializeField, Min(0f)] private float speedMultiplier = 1f;
        
        [Header("Rendering")]
        [Min(0)] public float cameraXOffsetMax = 17f;

        Animator animator;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable currentPlayable;
        AnimationClip currentClip;
        bool isPlaying;
        bool graphInitialized;

        public AnimationClip CurrentClip => currentClip;
        public float SpeedMultiplier => speedMultiplier;

        public bool IsFlipped => animator && animator.transform.localScale.x < 0;
        
        public void SetFlipX(bool flipped)
        {
            if (!animator)
                return;

            if (flipped && animator.transform.localScale.x > 0)
                animator.transform.localScale = new Vector3(-animator.transform.localScale.x, animator.transform.localScale.y, animator.transform.localScale.z);
            else if (!flipped && animator.transform.localScale.x < 0)
                animator.transform.localScale = new Vector3(-animator.transform.localScale.x, animator.transform.localScale.y, animator.transform.localScale.z);
        }

        void OnValidate()
        {
            FaceCamera(Camera.main?.GetComponent<CameraManager>());
        }
        
        void FaceCamera(CameraManager camera)
        {
            if (cameraXOffsetMax <= 0 || !camera)
                return;
            
            // Calculate the distance along the camera forward vector
            Vector3 toCamera = camera.transform.position - transform.position;
            float distanceAlongForward = Vector3.Dot(toCamera.With(y:0), camera.transform.forward);
            float xOffset = (1 - -distanceAlongForward / camera.xSpriteRotationOffset) * camera.xSpriteRotationMultiplier;
            if (xOffset > 1f) xOffset = 1f;
            if (xOffset < 0f) xOffset = 0f;
            transform.eulerAngles = new Vector3(xOffset * cameraXOffsetMax, camera.transform.eulerAngles.y, transform.eulerAngles.z);
            
        }

        void Awake()
        {
            animator = GetComponent<Animator>();
            speedMultiplier = Mathf.Max(0f, speedMultiplier);
            EnsureGraph();

            if (defaultClip != null)
            {
                SetClip(defaultClip, 0f, true);
            }

            if (playOnAwake)
            {
                Play();
            }
            else
            {
                PauseGraph();
            }
        }

        public void Start()
        {
            if (CameraManager.Instance)
                CameraManager.Instance.OnCameraPositionUpdated += (position, position2) => FaceCamera(CameraManager.Instance);
        }

        void OnEnable()
        {
            if (graphInitialized && isPlaying)
            {
                graph.Play();
            }
        }

        void OnDisable()
        {
            if (graphInitialized)
            {
                graph.Stop();
            }
        }

        void OnDestroy()
        {
            if (graphInitialized)
            {
                graph.Destroy();
                graphInitialized = false;
            }
        }

        void EnsureGraph()
        {
            if (graphInitialized)
            {
                return;
            }

            graph = PlayableGraph.Create($"SpriteAnimator_{name}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            mixer = AnimationMixerPlayable.Create(graph, 1);
            var output = AnimationPlayableOutput.Create(graph, "SpriteAnimation", animator);
            output.SetSourcePlayable(mixer);
            graph.Play();
            graphInitialized = true;
            isPlaying = true;
        }

        void PauseGraph()
        {
            isPlaying = false;
            UpdatePlayableSpeed();
            if (graphInitialized)
            {
                graph.Stop();
            }
        }

        void UpdatePlayableSpeed()
        {
            if (!currentPlayable.IsValid())
            {
                return;
            }

            double speed = isPlaying ? Mathf.Max(0f, speedMultiplier) : 0d;
            currentPlayable.SetSpeed(speed);
        }

        public void SetSpeed(float value)
        {
            speedMultiplier = Mathf.Max(0f, value);
            UpdatePlayableSpeed();
        }

        public void SetClip(AnimationClip clip, float startNormalizedTime = 0f, bool force = false)
        {
            if (!force && ReferenceEquals(currentClip, clip))
            {
                return;
            }

            EnsureGraph();

            currentClip = clip;

            if (currentPlayable.IsValid())
            {
                mixer.DisconnectInput(0);
                currentPlayable.Destroy();
            }

            if (clip == null)
            {
                currentPlayable = default;
                return;
            }

            currentPlayable = AnimationClipPlayable.Create(graph, clip);
            currentPlayable.SetApplyFootIK(false);
            currentPlayable.SetApplyPlayableIK(false);

            double startTime = clip.length * Mathf.Clamp01(startNormalizedTime);
            currentPlayable.SetTime(startTime);
            currentPlayable.SetDuration(clip.isLooping ? double.PositiveInfinity : clip.length);

            mixer.ConnectInput(0, currentPlayable, 0);
            mixer.SetInputWeight(0, 1f);

            if (!graph.IsPlaying())
            {
                graph.Play();
            }

            isPlaying = true;
            UpdatePlayableSpeed();
        }

        public void Play()
        {
            if (!graphInitialized)
            {
                EnsureGraph();
            }

            if (!currentPlayable.IsValid() && defaultClip != null)
            {
                SetClip(defaultClip, 0f, true);
            }

            isPlaying = true;
            UpdatePlayableSpeed();
            if (graphInitialized && !graph.IsPlaying())
            {
                graph.Play();
            }
        }

        public void Stop()
        {
            if (!currentPlayable.IsValid())
            {
                return;
            }

            isPlaying = false;
            UpdatePlayableSpeed();
        }

        public bool IsPlaying()
        {
            return isPlaying && currentPlayable.IsValid();
        }
    }
}
