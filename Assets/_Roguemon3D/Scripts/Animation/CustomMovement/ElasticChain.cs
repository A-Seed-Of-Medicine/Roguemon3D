using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Top-down 3D scrunching worm / caterpillar chain for Unity 6.3.
/// 
/// • segments[0] is the head, driven externally (player/AI).
/// • Remaining segments follow with elastic, scrunching motion.
/// • Y (height) of segments stays tied to the head's height (with per-segment offset),
///   and then rises into an arch based on the scrunch wave.
/// • Includes a hard max distance constraint between consecutive segments on XZ.
/// 
/// Assumes movement is on XZ plane, Y is up.
/// </summary>
public class ElasticChain : MonoBehaviour
{
    [Header("Segments (index 0 is the head)")]
    [Tooltip("Ordered from head (0) to tail (last).")]
    [SerializeField] private List<Transform> segments = new List<Transform>();

    [Header("Distance / Follow")]
    [Tooltip("Base rest distance between segments in world units.")]
    [SerializeField] private float baseSpacing = 0.5f;

    [Tooltip("Multiplier for spacing when fully scrunched (compressed). < 1.")]
    [SerializeField] private float scrunchSpacingMultiplier = 0.5f;

    [Tooltip("Multiplier for spacing when fully stretched. > 1.")]
    [SerializeField] private float stretchSpacingMultiplier = 1.3f;

    [Tooltip("How tightly segments move toward their target position (higher = snappier).")]
    [SerializeField] private float followTightness = 12f;

    [Tooltip("Maximum allowed distance between consecutive segments on XZ. 0 = no cap.")]
    [SerializeField] private float maxSegmentDistance = 1.2f;

    [Header("Scrunch Wave")]
    [Tooltip("Speed of the scrunching wave travelling down the body.")]
    [SerializeField] private float waveSpeed = 3f;

    [Tooltip("Phase offset between each segment in the wave.")]
    [SerializeField] private float waveSegmentOffset = 0.8f;

    [Tooltip("0 = no scrunching; 1 = full scrunching as defined by multipliers.")]
    [Range(0f, 1f)]
    [SerializeField] private float scrunchAmount = 1f;

    [Header("Vertical Rise (Y)")]
    [Tooltip("Maximum vertical rise (arch) at peak scrunch.")]
    [SerializeField] private float riseAmplitude = 0.25f;

    [Tooltip("Map scrunch amount [0..1] → rise factor [0..1]. If null, scrunch value is used directly.")]
    [SerializeField] private AnimationCurve riseByScrunch =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Rotation")]
    [Tooltip("If true, segments rotate to face the previous segment on the XZ plane.")]
    [SerializeField] private bool rotateSegments = true;

    [Tooltip("How fast segments rotate toward the follow direction.")]
    [SerializeField] private float rotationSpeed = 10f;

    // Per-segment Y offset relative to the head when the chain is at rest.
    // This keeps each segment's base Y following the head's Y.
    private float[] yOffsetRelativeToHead;

    private const float EPSILON = 0.0001f;

    private void Start()
    {
        CacheBaseOffsets();
    }

    private void OnValidate()
    {
        CacheBaseOffsets();
    }

    private void LateUpdate()
    {
        if (segments == null || segments.Count <= 1)
            return;

        if (yOffsetRelativeToHead == null || yOffsetRelativeToHead.Length != segments.Count)
            CacheBaseOffsets();

        // Use the head's current Y as the base height plane for the whole chain.
        float headPlaneY = segments[0].position.y;
        float time = Time.time;

        for (int i = 1; i < segments.Count; i++)
        {
            Transform prev = segments[i - 1];
            Transform current = segments[i];

            if (prev == null || current == null)
                continue;

            // --- Direction & target spacing on XZ plane ---
            Vector3 prevFlat = prev.position;
            prevFlat.y = 0f;

            Vector3 currFlat = current.position;
            currFlat.y = 0f;

            Vector3 flatDiff = currFlat - prevFlat;
            float currentDist = flatDiff.magnitude;

            if (currentDist < EPSILON)
            {
                // Fallback direction if overlapping
                Vector3 fallbackDir = prev.forward;
                if (fallbackDir.sqrMagnitude < EPSILON)
                    fallbackDir = Vector3.forward;

                flatDiff = fallbackDir.normalized;
                currentDist = 1f;
            }

            Vector3 dirFromPrevToCurrent = flatDiff / currentDist;

            // --- Scrunch wave for this segment ---
            float phase = time * waveSpeed - i * waveSegmentOffset;

            // Wave in [0..1]
            float waveRaw = 0.5f + 0.5f * Mathf.Sin(phase);

            // Blend around 0.5, controlled by scrunchAmount
            float scrunch = Mathf.Lerp(0.5f, waveRaw, scrunchAmount);

            // Compute target spacing based on scrunch
            float minSpacing = baseSpacing * scrunchSpacingMultiplier;
            float maxSpacingWave = baseSpacing * stretchSpacingMultiplier;
            float targetSpacing = Mathf.Lerp(maxSpacingWave, minSpacing, scrunch);

            // Apply hard maximum segment distance if configured
            if (maxSegmentDistance > 0f)
                targetSpacing = Mathf.Min(targetSpacing, maxSegmentDistance);

            Vector3 targetFlat = prevFlat + dirFromPrevToCurrent * targetSpacing;

            // --- Vertical arching (Y) tied to head height ---
            float riseFactor = (riseByScrunch != null)
                ? riseByScrunch.Evaluate(scrunch)
                : scrunch;

            float baseOffsetY = (yOffsetRelativeToHead != null && i < yOffsetRelativeToHead.Length)
                ? yOffsetRelativeToHead[i]
                : 0f;

            float targetY = headPlaneY + baseOffsetY + riseAmplitude * riseFactor;

            Vector3 desiredPos = new Vector3(targetFlat.x, targetY, targetFlat.z);

            // Smooth follow for elastic feel
            float lerpT = 1f - Mathf.Exp(-followTightness * Time.deltaTime);
            current.position = Vector3.Lerp(current.position, desiredPos, lerpT);

            // --- Hard clamp max distance on XZ after the smooth follow (safety net) ---
            if (maxSegmentDistance > 0f)
            {
                Vector3 fromPrevToCurr = current.position - prev.position;
                float currY = current.position.y;
                fromPrevToCurr.y = 0f;

                float flatSegDist = fromPrevToCurr.magnitude;
                if (flatSegDist > maxSegmentDistance)
                {
                    Vector3 clampedPos = prev.position + fromPrevToCurr.normalized * maxSegmentDistance;
                    clampedPos.y = currY; // keep the Y we just computed
                    current.position = clampedPos;
                }
            }

            // --- Optional rotation to face previous segment on XZ plane ---
            if (rotateSegments)
            {
                Vector3 lookDir = prev.position - current.position;
                lookDir.y = 0f;

                if (lookDir.sqrMagnitude > EPSILON)
                {
                    Quaternion targetRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
                    current.rotation = Quaternion.Slerp(
                        current.rotation,
                        targetRot,
                        rotationSpeed * Time.deltaTime
                    );
                }
            }
        }
    }

    private void CacheBaseOffsets()
    {
        if (segments == null || segments.Count == 0)
        {
            yOffsetRelativeToHead = null;
            return;
        }

        if (yOffsetRelativeToHead == null || yOffsetRelativeToHead.Length != segments.Count)
            yOffsetRelativeToHead = new float[segments.Count];

        Transform head = segments[0];
        if (head == null)
            return;

        float headY = head.position.y;

        for (int i = 0; i < segments.Count; i++)
        {
            Transform seg = segments[i];
            if (seg != null)
                yOffsetRelativeToHead[i] = seg.position.y - headY;
        }
    }
}
