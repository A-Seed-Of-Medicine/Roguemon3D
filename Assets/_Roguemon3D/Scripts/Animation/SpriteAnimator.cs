using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace _PinBoy.Scripts.Animation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SpriteAnimator : MonoBehaviour
    {
        [Header("Animation")]
        [SerializeField] private AnimationClip defaultClip;
        [SerializeField] private bool playOnAwake = true;
        [SerializeField, Min(0f)] private float speedMultiplier = 1f;
        
        [Header("Rendering")]
        public bool faceCamera = true;

        Animator animator;
        SpriteRenderer spriteRenderer;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable currentPlayable;
        AnimationClip currentClip;
        bool isPlaying;
        bool graphInitialized;

        public AnimationClip CurrentClip => currentClip;
        public float SpeedMultiplier => speedMultiplier;

        public bool flipX
        {
            get => spriteRenderer != null && spriteRenderer.flipX;
            set
            {
                if (spriteRenderer != null)
                {
                    spriteRenderer.flipX = value;
                }
            }
        }

        void OnValidate()
        {
            FaceCamera(Camera.main?.GetComponent<CameraManager>());
        }
        
        void FaceCamera(CameraManager camera)
        {
            if (!faceCamera || !camera)
                return;
            
            Debug.Log("FaceCamera called");
            transform.eulerAngles = new Vector3(transform.eulerAngles.x, camera.transform.eulerAngles.y, transform.eulerAngles.z);
        }

        void Awake()
        {
            animator = GetComponent<Animator>();
            spriteRenderer = GetComponent<SpriteRenderer>();
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

            mixer = AnimationMixerPlayable.Create(graph, 1, true);
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
