using UnityEngine;
using UnityEngine.SceneManagement;

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
    [Header("Per-User Calibration")]
    public bool requireCalibrationAtStart = false; // Disabled by default
    public bool showCalibrationOverlay = false;    // Disabled by default
    public float minValidArmLength = 0.25f;        // Reject accidental very short measurements
    public float maxValidArmLength = 1.20f;        // Safety cap for outlier tracking noise
    public float stableHoldSeconds = 3.0f;         // How long user must hold extension steady
    public float stabilityTolerance = 0.015f;      // Allowed movement while holding full extension
    public float extensionDeltaFromRest = 0.20f;   // Required distance increase from resting hand pose
    public float restSamplingSeconds = 1.0f;       // Time window to estimate resting hand distance
    public float activationMargin = 0.005f;        // Small lead-in margin before exact extension
    public float releaseHysteresis = 0.03f;        // Prevent flicker when hovering around threshold
    public float captureMessageDuration = 2.5f;    // One-time confirmation display time after capture
    public bool showWorldSpaceCalibrationHint = false;
    public Vector3 worldHintLocalOffset = new Vector3(0f, -0.12f, 0.85f);
    public bool applyCalibrationOnlyInReverseScene = true;
    public string reverseSceneName = "ReverseGoGo SampleScene";

    [Header("Threshold Configuration")]
    public float thresholdDistance = 0.3f;           // Distance from HMD beyond which grabbing is allowed
    [Range(0.1f, 0.95f)] public float thresholdReachRatio = 0.75f; // Fraction of calibrated reach used as threshold
    public float minThresholdGapFromFullExtension = 0.08f; // Keep threshold below full extension so exponential zone exists
    
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
    private bool isCalibrated;
    private float calibratedFullExtensionDistance;
    private float observedMaxDistance;
    private float stableTimer;
    private float restDistanceAccum;
    private int restDistanceSamples;
    private float restSamplingEndTime;
    private float extensionTargetDistance;
    private string calibrationStatus = "";
    private bool showCaptureMessage;
    private float captureMessageUntilTime;
    private string captureMessage = "";
    private GameObject worldHintObject;
    private TextMesh worldHintText;

    void Start()
    {
        ResolveReferences();

        if (!ShouldRunReverseCalibration())
        {
            isCalibrated = true;
            calibrationStatus = "";
        }

        if (hmdTransform == null)
        {
            Debug.LogError("HandCalibrationDepthScale: HMD transform not found!");
        }

        if (controllerTransform == null)
        {
            Debug.LogError("HandCalibrationDepthScale: Controller transform not found!");
        }

        if (requireCalibrationAtStart)
        {
            RestartCalibration();
        }
        else
        {
            isCalibrated = true;
            calibratedFullExtensionDistance = thresholdDistance;
        }
    }

    void Update()
    {
        ResolveReferences();

        if (hmdTransform == null || controllerTransform == null)
            return;

        if (!ShouldRunReverseCalibration())
        {
            isCalibrated = true;
            calibrationStatus = "";
            UpdateWorldSpaceHint();
            return;
        }

        // Calculate distance from HMD to controller
        float distanceFromHMD = Vector3.Distance(hmdTransform.position, controllerTransform.position);
        observedMaxDistance = Mathf.Max(observedMaxDistance, Mathf.Clamp(distanceFromHMD, 0f, maxValidArmLength));

        UpdateCalibration(distanceFromHMD);

        UpdateWorldSpaceHint();

        float activationDistance = GetActivationDistance();

        // Check if controller is beyond threshold
        currentDistanceBeyondThreshold = distanceFromHMD - activationDistance;

        if (!IsReadyForInteraction())
        {
            isControllerBeyondThreshold = false;
            depthScalingFactor = 1.0f;
            return;
        }

        if (isControllerBeyondThreshold)
        {
            isControllerBeyondThreshold = distanceFromHMD >= (activationDistance - releaseHysteresis);
        }
        else
        {
            isControllerBeyondThreshold = distanceFromHMD >= (activationDistance - activationMargin);
        }

        // Calculate depth scaling factor based on exponential curve
        if (isControllerBeyondThreshold)
        {
            // Calculate scaling factor using exponential function
            // At full extension (far from threshold): factor = 1.0
            // As hand retracts toward threshold: factor increases exponentially
            
            // Normalized distance (0 at threshold, increases as hand extends)
            float normalizedDistance = currentDistanceBeyondThreshold / Mathf.Max(activationDistance, 0.001f);
            
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

    private void UpdateCalibration(float distanceFromHMD)
    {
        if (!requireCalibrationAtStart || isCalibrated)
            return;

        float measured = Mathf.Clamp(distanceFromHMD, 0f, maxValidArmLength);
        observedMaxDistance = Mathf.Max(observedMaxDistance, measured);

        if (Time.time <= restSamplingEndTime)
        {
            restDistanceAccum += measured;
            restDistanceSamples++;
            calibrationStatus = "";
            return;
        }

        float restDistance = restDistanceSamples > 0
            ? (restDistanceAccum / restDistanceSamples)
            : Mathf.Max(0.1f, thresholdDistance * 0.5f);

        float baseTarget = restDistance + Mathf.Max(0.05f, extensionDeltaFromRest);
        float adaptiveTarget = observedMaxDistance - 0.015f;
        extensionTargetDistance = Mathf.Clamp(Mathf.Max(baseTarget, adaptiveTarget), minValidArmLength, maxValidArmLength);

        bool inExtensionHoldZone = measured >= (extensionTargetDistance - stabilityTolerance);
        if (inExtensionHoldZone)
        {
            stableTimer += Time.deltaTime;
        }
        else
        {
            stableTimer = 0f;
        }

        if (!inExtensionHoldZone)
        {
            calibrationStatus = "";
            return;
        }

        float remaining = Mathf.Max(0f, stableHoldSeconds - stableTimer);
        int countdownValue = Mathf.Max(1, Mathf.CeilToInt(remaining));
        calibrationStatus = $"Hold steady: {countdownValue}...";

        if (stableTimer >= stableHoldSeconds)
        {
            calibratedFullExtensionDistance = Mathf.Clamp(measured, minValidArmLength, maxValidArmLength);

            float derivedRestDistance = restDistanceSamples > 0
                ? (restDistanceAccum / restDistanceSamples)
                : Mathf.Max(0.1f, calibratedFullExtensionDistance * 0.5f);

            float ratio = Mathf.Clamp01(thresholdReachRatio);
            float ratioThreshold = Mathf.Lerp(derivedRestDistance, calibratedFullExtensionDistance, ratio);
            float maxThreshold = calibratedFullExtensionDistance - Mathf.Max(0.02f, minThresholdGapFromFullExtension);
            thresholdDistance = Mathf.Clamp(ratioThreshold, minValidArmLength, Mathf.Max(minValidArmLength, maxThreshold));

            isCalibrated = true;
            calibrationStatus = $"Calibration complete ({calibratedFullExtensionDistance:F2}m).";
            captureMessage = $"Calibration distance captured: {calibratedFullExtensionDistance:F2}m";
            showCaptureMessage = true;
            captureMessageUntilTime = Time.time + Mathf.Max(0.5f, captureMessageDuration);
            Debug.Log($"[Calibration] Arm length captured: {calibratedFullExtensionDistance:F3}m | Threshold set to: {thresholdDistance:F3}m (ratio={ratio:F2})");
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

    public bool IsCalibrated()
    {
        return isCalibrated;
    }

    public bool IsReadyForInteraction()
    {
        return !requireCalibrationAtStart || isCalibrated;
    }

    public float GetActivationDistance()
    {
        return IsReadyForInteraction() ? thresholdDistance : Mathf.Max(thresholdDistance, observedMaxDistance);
    }

    public float GetFullExtensionDistance()
    {
        if (requireCalibrationAtStart && IsReadyForInteraction())
            return Mathf.Max(thresholdDistance, calibratedFullExtensionDistance);

        // Without formal calibration, treat the user's farthest observed reach this session
        // as the full-extension target, while never allowing activation below the base threshold.
        return Mathf.Max(0f, observedMaxDistance);
    }

    public void RestartCalibration()
    {
        isCalibrated = false;
        observedMaxDistance = 0f;
        stableTimer = 0f;
        restDistanceAccum = 0f;
        restDistanceSamples = 0;
        restSamplingEndTime = Time.time + Mathf.Max(0.2f, restSamplingSeconds);
        extensionTargetDistance = minValidArmLength;
        calibratedFullExtensionDistance = thresholdDistance;
        calibrationStatus = "";
        showCaptureMessage = false;
        captureMessageUntilTime = 0f;
        captureMessage = "";
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
        float activationDistance = GetActivationDistance();
        if (!isControllerBeyondThreshold)
        {
            Debug.DrawLine(hmdTransform.position, hmdTransform.position + hmdTransform.forward * activationDistance, Color.red);
        }
        else
        {
            Debug.DrawLine(hmdTransform.position, controllerTransform.position, Color.green);
        }
    }

    private void UpdateWorldSpaceHint()
    {
        if (!showWorldSpaceCalibrationHint || hmdTransform == null)
            return;

        if (worldHintObject == null)
        {
            worldHintObject = new GameObject("CalibrationWorldHint");
            worldHintObject.transform.SetParent(hmdTransform, false);
            worldHintObject.transform.localPosition = worldHintLocalOffset;
            worldHintObject.transform.localRotation = Quaternion.identity;

            worldHintText = worldHintObject.AddComponent<TextMesh>();
            worldHintText.fontSize = 56;
            worldHintText.characterSize = 0.01f;
            worldHintText.anchor = TextAnchor.MiddleCenter;
            worldHintText.alignment = TextAlignment.Center;
            worldHintText.color = Color.white;
            worldHintText.text = "";
        }

        worldHintObject.transform.localPosition = worldHintLocalOffset;
        worldHintObject.transform.localRotation = Quaternion.identity;

        if (!IsReadyForInteraction())
        {
            worldHintText.text = calibrationStatus;
        }
        else
        {
            worldHintText.text = "";
        }

        if (!ShouldRunReverseCalibration())
        {
            worldHintText.text = "";
        }
    }

    private bool ShouldRunReverseCalibration()
    {
        if (!applyCalibrationOnlyInReverseScene)
            return true;

        Scene active = SceneManager.GetActiveScene();
        return active.IsValid() && active.name == reverseSceneName;
    }

    private void ResolveReferences()
    {
        if (hmdTransform == null)
        {
            if (Camera.main != null)
            {
                hmdTransform = Camera.main.transform;
            }
            else if (Camera.allCamerasCount > 0)
            {
                hmdTransform = Camera.allCameras[0].transform;
            }
        }

        if (controllerTransform == null && virtualHandAttach != null)
        {
            controllerTransform = virtualHandAttach.controllerTransform;
        }

        if (controllerTransform == null)
        {
            GameObject rightHand = GameObject.Find("Right Hand");
            if (rightHand != null)
            {
                controllerTransform = rightHand.transform;
            }
        }
    }

    private void OnDestroy()
    {
        if (worldHintObject != null)
        {
            Destroy(worldHintObject);
        }
    }

    private void OnGUI()
    {
        if (showCaptureMessage)
        {
            if (Time.time <= captureMessageUntilTime)
            {
                float msgWidth = Mathf.Min(620f, Screen.width - 40f);
                float msgX = (Screen.width - msgWidth) * 0.5f;
                float msgY = 20f;
                GUI.Box(new Rect(msgX, msgY, msgWidth, 66f), "Reverse GoGo Calibration");
                GUI.Label(new Rect(msgX + 16f, msgY + 30f, msgWidth - 32f, 24f), captureMessage);
            }
            else
            {
                showCaptureMessage = false;
            }
        }

        if (!showCalibrationOverlay || IsReadyForInteraction())
            return;

        if (hmdTransform == null || controllerTransform == null)
        {
            GUI.Box(new Rect(20f, 20f, Mathf.Min(720f, Screen.width - 40f), 80f), "Reverse GoGo Calibration");
            GUI.Label(new Rect(36f, 52f, Screen.width - 72f, 24f), "Calibration waiting for references: assign HMD and right controller transforms.");
            return;
        }

        float width = Mathf.Min(560f, Screen.width - 40f);
        float x = (Screen.width - width) * 0.5f;
        float y = 20f;
        float h = 120f;

        GUI.Box(new Rect(x, y, width, h), "Reverse GoGo Calibration");
        GUI.Label(new Rect(x + 16f, y + 30f, width - 32f, 24f), calibrationStatus);
        GUI.Label(new Rect(x + 16f, y + 54f, width - 32f, 22f), $"Current distance: {Vector3.Distance(hmdTransform.position, controllerTransform.position):F2}m");
        GUI.Label(new Rect(x + 16f, y + 76f, width - 32f, 22f), $"Observed max: {observedMaxDistance:F2}m");
        GUI.Label(new Rect(x + 16f, y + 98f, width - 32f, 22f), "Ray activates at derived threshold below full extension.");
    }
}
