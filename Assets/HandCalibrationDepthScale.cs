using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

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
    public bool requireCalibrationBeforeTracking = true;
    public bool useRecordedLengthAsThreshold = true;
    public float minimumThresholdDistance = 0.2f;
    
    [Header("Exponential Scaling")]
    public float exponentialPower = 2.0f;            // Exponential curve power (2 = quadratic, 3 = cubic)
    public float maxScalingFactor = 10.0f;          // Maximum multiplier (10x at threshold)
    
    [Header("References")]
    public Transform hmdTransform;                   // Camera/HMD transform
    public Transform controllerTransform;            // Right hand controller
    public VirtualHandAttach virtualHandAttach;     // Reference to grab script
    public InputActionProperty calibrationTriggerAction; // Trigger to confirm full arm extension

    [Header("Debug UI")]
    public bool showCalibrationOnScreen = true;
    public bool useXRTriggerFallback = true;

    private float currentDistanceBeyondThreshold;    // Current controller distance beyond threshold
    private float depthScalingFactor = 1.0f;        // Current scaling multiplier
    private bool isControllerBeyondThreshold = false;
    private bool armLengthRecorded = false;
    private float recordedArmLength = 0f;
    private bool previousXRTriggerPressed = false;
    private Text _vrLabel;  // World-space label visible inside the HMD
    private float _labelHideTime = -1f;  // Time.time when the label should disappear

    void Start()
    {
        if (virtualHandAttach == null)
        {
            virtualHandAttach = FindFirstObjectByType<VirtualHandAttach>();
        }

        // Auto-find HMD if not assigned
        if (hmdTransform == null)
        {
            if (Camera.main != null)
            {
                hmdTransform = Camera.main.transform;
            }
        }

        // Auto-find controller if not assigned
        if (controllerTransform == null && virtualHandAttach != null)
        {
            controllerTransform = virtualHandAttach.controllerTransform;
        }

        if (calibrationTriggerAction.action == null && virtualHandAttach != null)
        {
            calibrationTriggerAction = virtualHandAttach.triggerAction;
        }

        if (calibrationTriggerAction.action != null)
        {
            calibrationTriggerAction.action.Enable();
        }
        else
        {
            Debug.LogWarning("HandCalibrationDepthScale: No InputAction trigger assigned. XR trigger fallback will be used if enabled.");
        }

        if (hmdTransform == null)
        {
            Debug.LogError("HandCalibrationDepthScale: HMD transform not found!");
        }

        if (controllerTransform == null)
        {
            Debug.LogError("HandCalibrationDepthScale: Controller transform not found!");
        }

        if (showCalibrationOnScreen && hmdTransform != null)
        {
            CreateVRLabel();
        }
    }

    void Update()
    {
        if (hmdTransform == null || controllerTransform == null)
            return;

        if (!armLengthRecorded)
        {
            TryRecordArmLength();
        }

        UpdateVRLabel();

        if (requireCalibrationBeforeTracking && !armLengthRecorded)
        {
            isControllerBeyondThreshold = false;
            currentDistanceBeyondThreshold = 0f;
            depthScalingFactor = 1f;
            return;
        }

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

    private void TryRecordArmLength()
    {
        bool triggerPressedThisFrame = false;

        if (calibrationTriggerAction.action != null && calibrationTriggerAction.action.WasPressedThisFrame())
        {
            triggerPressedThisFrame = true;
        }

        if (!triggerPressedThisFrame && useXRTriggerFallback)
        {
            UnityEngine.XR.InputDevice rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool triggerHeld = false;
            if (rightController.isValid && rightController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out triggerHeld))
            {
                triggerPressedThisFrame = triggerHeld && !previousXRTriggerPressed;
                previousXRTriggerPressed = triggerHeld;
            }
        }

        if (!triggerPressedThisFrame)
            return;

        recordedArmLength = Vector3.Distance(hmdTransform.position, controllerTransform.position);
        armLengthRecorded = true;

        if (useRecordedLengthAsThreshold)
        {
            thresholdDistance = Mathf.Max(minimumThresholdDistance, recordedArmLength);
        }

        Debug.Log($"[Arm Calibration] Trigger pressed. Recorded arm length: {recordedArmLength:F3}m | Active threshold: {thresholdDistance:F3}m");
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

    public bool IsArmLengthRecorded()
    {
        return armLengthRecorded;
    }

    // Backward-compatible alias used by other scripts.
    public bool IsCalibrationComplete()
    {
        return !requireCalibrationBeforeTracking || armLengthRecorded;
    }

    public float GetRecordedArmLength()
    {
        return recordedArmLength;
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

    // ── World-space VR label (visible inside HMD) ─────────────────────────

    private void CreateVRLabel()
    {
        // Canvas parented to the HMD — moves with the player's head
        GameObject canvasGO = new GameObject("CalibrationCanvas");
        canvasGO.transform.SetParent(hmdTransform, false);

        // Position upper-middle in the player's field of view
        canvasGO.transform.localPosition = new Vector3(0f, 0.10f, 0.65f);
        canvasGO.transform.localRotation = Quaternion.identity;

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(440f, 60f);
        canvasGO.transform.localScale = Vector3.one * 0.001f; // 0.44m × 0.06m world size

        // Background panel
        GameObject panelGO = new GameObject("Background");
        panelGO.transform.SetParent(canvasGO.transform, false);
        Image bg = panelGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.65f);
        RectTransform panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;

        // Text label
        GameObject textGO = new GameObject("Label");
        textGO.transform.SetParent(canvasGO.transform, false);
        _vrLabel = textGO.AddComponent<Text>();
        _vrLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _vrLabel.fontSize = 26;
        _vrLabel.fontStyle = FontStyle.Bold;
        _vrLabel.alignment = TextAnchor.MiddleCenter;
        _vrLabel.color = new Color(1f, 0.85f, 0.1f); // yellow until calibrated
        _vrLabel.supportRichText = true;

        RectTransform labelRT = textGO.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = new Vector2(8f, 0f);
        labelRT.offsetMax = new Vector2(-8f, 0f);
    }

    private void UpdateVRLabel()
    {
        if (_vrLabel == null)
            return;

        // Hide the label once the hide timer has elapsed
        if (_labelHideTime >= 0f && Time.time >= _labelHideTime)
        {
            _vrLabel.transform.parent.gameObject.SetActive(false);
            return;
        }

        if (!armLengthRecorded)
        {
            _vrLabel.color = new Color(1f, 0.85f, 0.1f);
            _vrLabel.text = "Extend arm fully & press Trigger to calibrate";
        }
        else
        {
            // First frame after calibration — start the 3-second countdown
            if (_labelHideTime < 0f)
                _labelHideTime = Time.time + 3f;

            _vrLabel.color = new Color(0.2f, 1f, 0.4f);
            _vrLabel.text = $"Arm Length: {recordedArmLength:F2} m";
        }
    }

    void OnGUI()
    {
        if (!showCalibrationOnScreen)
            return;

        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 22;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleLeft;
        style.padding = new RectOffset(14, 14, 8, 8);

        string text;
        if (!armLengthRecorded)
        {
            style.normal.textColor = new Color(1f, 0.85f, 0.1f); // yellow — waiting
            text = "⚠  Extend arm fully & press Trigger to calibrate";
        }
        else
        {
            style.normal.textColor = new Color(0.2f, 1f, 0.4f); // green — done
            text = $"✓  Arm Length: {recordedArmLength:F2} m";
        }

        GUI.Box(new Rect(16, 16, 400, 48), text, style);
    }
}
