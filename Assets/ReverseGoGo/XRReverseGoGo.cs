using UnityEngine;

public class XRReverseGoGo : MonoBehaviour
{
    public Transform rightHand;
    public Transform targetObject;

    [Header("Calibration Reference")]
    public HandCalibrationDepthScale handCalibration;

    [Header("Go-Go Mapping Parameters (Fallback)")]
    public float thresholdDistance = 0.5f; // Used if no calibration
    public float scalingFactor = 20.0f;    // Used if no calibration
    public float maxExtensionDistance = 10.0f;

    [Header("Smoothing")]
    public float positionSmoothing = 24f; // Higher = less lag (increased for more smoothing)
    [Header("Micro-movement Filtering")]
    public float deadzone = 0.002f; // Ignore hand movements smaller than this (meters)

    [Header("Precision Placement")]
    public float precisionRange = 0.15f; // Within this (meters) of target, reduce gain
    public float minGainNearTarget = 1.2f; // Minimum gain for fine placement

    private Vector3 smoothedTargetPosition;
    private bool initialized = false;

    void Update()
    {
        if (rightHand == null || targetObject == null) return;

        Vector3 hmdPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        float handDistance = Vector3.Distance(hmdPos, rightHand.position);

        // Use calibration if available
        float usedThreshold = handCalibration != null ? handCalibration.thresholdDistance : thresholdDistance;
        float usedScaling = handCalibration != null ? handCalibration.maxScalingFactor : scalingFactor;
        float usedMaxExtension = maxExtensionDistance;

        // Go-Go mapping: 1:1 inside threshold, exponential beyond
        float mappedDistance = CalculateGoGoMappedDistance(handDistance, usedThreshold, usedScaling, usedMaxExtension);
        Vector3 handDirection = (rightHand.position - hmdPos).normalized;
        Vector3 mappedPosition = hmdPos + handDirection * mappedDistance;

        // --- Adaptive Gain for Precision Placement ---
        // Adaptive gain for precision placement (kept for completeness, but not used in smoothing logic below)
        float nearestTargetDistance = GetNearestTargetDistance();
        if (nearestTargetDistance >= 0f && nearestTargetDistance < precisionRange)
        {
            // Linearly reduce gain as object approaches target (optional, can be removed if not needed)
            float t = Mathf.Clamp01(nearestTargetDistance / precisionRange);
            float adaptiveGain = Mathf.Lerp(minGainNearTarget, 1f, t);
            mappedPosition = Vector3.Lerp(targetObject.position, mappedPosition, adaptiveGain);
        }

        // Adaptive smoothing and deadzone for precision placement
        float smoothing = 0f;
        float deadzone = 0f;
        if (nearestTargetDistance >= 0f && nearestTargetDistance < precisionRange)
        {
            // Apply smoothing only near the target, no deadzone for maximum sensitivity
            smoothing = 18f; // Moderate smoothing for precision
            deadzone = 0f; // No deadzone
        }

        if (smoothing > 0f)
        {
            if (!initialized)
            {
                smoothedTargetPosition = mappedPosition;
                initialized = true;
            }
            else
            {
                float dt = Mathf.Max(Time.deltaTime, 0.0001f);
                float blend = 1f - Mathf.Exp(-smoothing * dt);
                Vector3 delta = mappedPosition - smoothedTargetPosition;
                if (delta.magnitude < deadzone)
                {
                    // Ignore micro-movements
                    // Do nothing, keep previous smoothedTargetPosition
                }
                else
                {
                    smoothedTargetPosition = Vector3.Lerp(smoothedTargetPosition, mappedPosition, blend);
                }
            }
            targetObject.position = smoothedTargetPosition;
        }
        else
        {
            // Pure Go-Go mapping elsewhere
            targetObject.position = mappedPosition;
        }
    }

    float CalculateGoGoMappedDistance(float handDistance, float safeThreshold, float scaling, float maxExtension)
    {
        float virtualDistance;
        if (handDistance <= safeThreshold)
        {
            virtualDistance = handDistance;
        }
        else
        {
            float beyondThreshold = handDistance - safeThreshold;
            virtualDistance = handDistance + Mathf.Max(0f, scaling) * beyondThreshold * beyondThreshold;
            virtualDistance = Mathf.Min(virtualDistance, Mathf.Max(safeThreshold, maxExtension));
        }
        return virtualDistance;
    }

    // Finds the closest target (with tag "Target" or similar) to the object, returns distance or -1 if not found
    float GetNearestTargetDistance()
    {
        GameObject[] targets = GameObject.FindGameObjectsWithTag("Target");
        if (targets == null || targets.Length == 0) return -1f;
        float minDist = float.MaxValue;
        Vector3 objPos = targetObject.position;
        foreach (var t in targets)
        {
            float d = Vector3.Distance(objPos, t.transform.position);
            if (d < minDist) minDist = d;
        }
        return minDist;
    }
}
