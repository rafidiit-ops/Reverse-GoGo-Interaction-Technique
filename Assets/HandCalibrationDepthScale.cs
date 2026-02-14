using UnityEngine;

/// <summary>
/// Hand Calibration and Depth Scaling for ReverseGoGo
/// 
/// Implements exponential depth scaling based on controller distance from HMD:
/// - Threshold: 0.3m from HMD (Meta Quest 2)
/// - Full extension (>0.3m): 1:1 mapping
/// - Closer to threshold: Exponential acceleration toward hand
/// - Example: 0.1m retract = 1m object movement (10x multiplier)
/// </summary>
public class HandCalibrationDepthScale : MonoBehaviour
{
    [Header("Threshold Configuration")]
    public float thresholdDistance = 0.3f;           // Distance from HMD beyond which grabbing is allowed
    
    [Header("Exponential Scaling")]
    public float exponentialPower = 2.0f;            // Exponential curve power (2 = quadratic, 3 = cubic)
    public float maxScalingFactor = 10.0f;          // Maximum multiplier (10x at threshold)
    
    [Header("References")]
    public Transform hmdTransform;                   // Camera/HMD transform
    public Transform controllerTransform;            // Right hand controller
    public VirtualHandAttach virtualHandAttach;     // Reference to grab script

    private float currentDistanceBeyondThreshold;    // Current controller distance beyond threshold
    private float depthScalingFactor = 1.0f;        // Current scaling multiplier
    private bool isControllerBeyondThreshold = false;

    void Start()
    {
        // Auto-find HMD if not assigned
        if (hmdTransform == null)
        {
            hmdTransform = Camera.main.transform;
        }

        // Auto-find controller if not assigned
        if (controllerTransform == null && virtualHandAttach != null)
        {
            // Try to get from VirtualHandAttach
            controllerTransform = virtualHandAttach.GetComponent<Transform>();
        }

        if (hmdTransform == null)
        {
            Debug.LogError("HandCalibrationDepthScale: HMD transform not found!");
        }

        if (controllerTransform == null)
        {
            Debug.LogError("HandCalibrationDepthScale: Controller transform not found!");
        }
    }

    void Update()
    {
        if (hmdTransform == null || controllerTransform == null)
            return;

        // Calculate distance from HMD to controller
        float distanceFromHMD = Vector3.Distance(hmdTransform.position, controllerTransform.position);

        // Check if controller is beyond threshold
        currentDistanceBeyondThreshold = distanceFromHMD - thresholdDistance;
        isControllerBeyondThreshold = currentDistanceBeyondThreshold > 0;

        // Debug log every 60 frames (once per second at 60fps)
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"[HandCalibration] Distance from HMD: {distanceFromHMD:F3}m | Threshold: {thresholdDistance}m | Beyond: {isControllerBeyondThreshold}");
        }

        // Calculate depth scaling factor based on exponential curve
        if (isControllerBeyondThreshold)
        {
            // Calculate scaling factor using exponential function
            // At full extension (far from threshold): factor = 1.0
            // As hand retracts toward threshold: factor increases exponentially
            
            // Normalized distance (0 at threshold, increases as hand extends)
            float normalizedDistance = currentDistanceBeyondThreshold / thresholdDistance;
            
            // Inverse exponential curve (higher when closer to threshold)
            // Using (1 / normalized distance)^exponentialPower to create acceleration effect
            depthScalingFactor = Mathf.Pow(1.0f / (normalizedDistance + 0.1f), exponentialPower);
            
            // Clamp to max scaling factor
            depthScalingFactor = Mathf.Min(depthScalingFactor, maxScalingFactor);
        }
        else
        {
            depthScalingFactor = 1.0f;
        }
    }

    /// <summary>
    /// Get the current depth scaling factor for object movement
    /// </summary>
    public float GetDepthScalingFactor()
    {
        return depthScalingFactor;
    }

    /// <summary>
    /// Check if controller is beyond grab threshold
    /// </summary>
    public bool IsControllerBeyondThreshold()
    {
        return isControllerBeyondThreshold;
    }

    /// <summary>
    /// Get current distance beyond threshold (in meters)
    /// </summary>
    public float GetDistanceBeyondThreshold()
    {
        return currentDistanceBeyondThreshold;
    }

    /// <summary>
    /// Calculate exponentially scaled movement delta
    /// </summary>
    public Vector3 ApplyDepthScaling(Vector3 inputDelta)
    {
        return inputDelta * depthScalingFactor;
    }

    /// <summary>
    /// Debug visualization
    /// </summary>
    public void DrawDebugInfo()
    {
        if (!isControllerBeyondThreshold)
        {
            Debug.DrawLine(hmdTransform.position, hmdTransform.position + hmdTransform.forward * thresholdDistance, Color.red);
        }
        else
        {
            Debug.DrawLine(hmdTransform.position, controllerTransform.position, Color.green);
        }
    }
}
