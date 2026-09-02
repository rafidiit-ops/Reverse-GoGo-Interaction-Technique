using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

public class VirtualHandAttach : MonoBehaviour
{
    public Transform virtualHand;                 // The ghost hand visual
    public Transform controllerTransform;         // The actual right-hand controller
    public RaycastObjectSelector selector;        // The raycast script
    public InputActionProperty triggerAction;     // XRI RightHand Activate (attach hand)
    public InputActionProperty gripAction;        // XRI RightHand Grip (remote pull)
    public HandCalibrationDepthScale depthScale;  // Hand calibration system

    [Header("Smoothing")]
    public float controllerDeltaSmoothing = 32f;  // Increased smoothing for finer micro-movements
    public float gainSmoothing = 16f;             // Increased gain smoothing for stability
    public float maxLinearSpeed = 50f;            // Caps velocity (m/s)

    [Header("Forward Smoothing")]
    public float forwardDirectionDeadzone = 0.0005f; // Smaller deadzone for more responsive micro-movements
    public float forwardDeltaSmoothing = 18f;       // Extra smoothing for outward motion
    public float forwardGainSmoothing = 12f;        // Gain smoothing for outward motion

    [Header("Calibration Range")]
    public float directGrabDistance = 0.1f;       // Below this, object behaves like direct hand grab

    [Header("Go-Go Mapping (standard nonlinear Go-Go)")]
    [Tooltip("Body/shoulder/torso reference (O). Falls back to the HMD if left unassigned.")]
    public Transform bodyReferenceTransform;
    [Tooltip("D — comfortable reach threshold (meters). 1:1 mapping when hand distance <= D.")]
    public float goGoThreshold = 0.5f;
    [Tooltip("k — Go-Go extension coefficient controlling how rapidly the virtual hand extends.")]
    public float goGoScalingFactor = 2.0f;

    [Header("Direct Grab Targeting")]
    public float directGrabSelectionRadius = 0.12f; // Fallback selection radius when ray is hidden near hand

    [Header("Near-Hand Assist")]
    public float nearHandResponsivenessMultiplier = 2.5f; // Higher response when object is near threshold
    public float nearHandConvergenceSpeed = 8f;           // Pulls object toward controller near threshold

    [Header("Controller Visuals")]
    public bool hideControllerWhileAttached = true;

    private bool isAttached = false;              // Trigger mode (hand attached)
    private bool isRemotePulling = false;         // Grip mode (remote pull with scaling)
    private GameObject currentlyGrabbedObject;
    private float handOffset = 0.6f;
    
    private Vector3 controllerStartPos;           // Controller position when grab started
    private Vector3 cubeStartPos;                 // Cube position when grab started
    private Vector3 controllerPullStartPos;       // Controller position when grip started (for exponential pull calculation)
    private float initialDistanceToController;   // Initial distance from cube to controller when grip started
    private float initialVirtualHandDistance;    // Initial virtual hand distance when grab started (Go-Go calculation)
    private Vector3 initialHMDPosition;          // HMD position when grab started
    private Quaternion initialControllerRotation; // Controller rotation when grab started
    private Quaternion initialObjectRotation;     // Object rotation when grab started
    private Quaternion centerDirectionOffset = Quaternion.identity; // Keeps initial center-relative direction to avoid snap on attach
    private float grabDistanceRatio = 1f;         // objectDistAtGrab / virtualHandDistAtGrab; ratio=1 at grab means zero snap
    private bool grabbedRigidbodyOriginalGravity;
    private Vector3 previousControllerPosition;   // Controller position from previous frame (for delta mapping)
    private Vector3 smoothedControllerDelta;      // Low-pass filtered controller delta
    private float smoothedSpatialGain = 1f;       // Low-pass filtered gain for stable transition
    private bool isForwardModeLatched = false;    // Prevents rapid forward/pull mode toggling
    private bool wasMovingForwardLastFrame = false;
    private float forwardRecoveryStartRadius = 0f;
    private float forwardRecoveryStartControllerRadius = 0f;
    private Renderer[] controllerRenderers;

    // Public accessors for UserStudyManager
    public bool IsAttached() { return isAttached; }
    public bool IsRemotePulling() { return isRemotePulling; }
    public GameObject GetCurrentObject() { return currentlyGrabbedObject; }
    public RaycastObjectSelector GetSelector() { return selector; }

    private InputAction _returnToUIAction;

    // A/B-to-UI: initialized true so first frame is never treated as a new press (carryover guard)
    private bool _prevGripForReturn = true;

    // Returns true on the rising edge of the right A or B button (primaryButton / secondaryButton).
    private bool GripReturnPressed()
    {
        UnityEngine.XR.InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        bool a = false, b = false;
        if (right.isValid)
        {
            right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out a);   // A
            right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out b); // B
        }
        bool abNow = a || b;
        bool pressed = abNow && !_prevGripForReturn;
        _prevGripForReturn = abNow;
        return pressed;
    }

    void Start()
    {
        if (virtualHand == null)
        {
            Debug.LogError("VirtualHandAttach: virtualHand is not assigned!");
        }

        if (controllerTransform == null)
        {
            Debug.LogError("VirtualHandAttach: controllerTransform is not assigned!");
        }

        if (selector == null)
        {
            Debug.LogError("VirtualHandAttach: selector (RaycastObjectSelector) is not assigned!");
        }
        
        // Enable input actions
        triggerAction.action.Enable();
        gripAction.action.Enable();

        CacheControllerRenderers();
    }

    void Update()
    {
        if (GripReturnPressed())
        {
            SceneManager.LoadScene("UI");
            return;
        }

        if (virtualHand == null || selector == null)
            return;

        // ===== TRIGGER BUTTON: Attach hand and activate Go-Go mode =====
        if (triggerAction.action.WasPressedThisFrame() && !isAttached)
        {
            if (depthScale != null && depthScale.requireCalibrationBeforeTracking && !depthScale.IsArmLengthRecorded())
            {
                Debug.Log("[Go-Go] Waiting for arm-length calibration. Extend arm and press trigger to record.");
                return;
            }

            GameObject selected = selector.GetCurrentTarget();
            if (selected == null)
            {
                // When the ray is hidden in near-hand range, allow direct proximity re-grab.
                selected = FindClosestDirectGrabTarget();
            }

            if (selected != null)
            {
                StartGoGoMode(selected);
            }
        }

        // Release on trigger release
        if (isAttached && triggerAction.action.WasReleasedThisFrame())
        {
            ReleaseHand();
        }

        // Apply Go-Go movement (exponential gain based on distance)
        if (isAttached && currentlyGrabbedObject != null)
        {
            ApplyGoGoMovement();
        }
    }

    private void ApplyHandAttachmentMovement()
    {
        // Calculate how much controller has moved (using actual controller, not virtual hand)
        Vector3 controllerDelta = controllerTransform.position - controllerStartPos;
        
        // Calculate target position (follow controller movement; positive mapping)
        Vector3 targetPos = cubeStartPos + controllerDelta;
        
        // Use Rigidbody for physics-based movement if available (and not kinematic)
        Rigidbody rb = currentlyGrabbedObject.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            // Calculate velocity needed to reach target position
            Vector3 positionDiff = targetPos - currentlyGrabbedObject.transform.position;
            rb.linearVelocity = positionDiff / Time.deltaTime;
            
            // Prevent rotation from physics
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }
        else
        {
            // Fallback: direct position if no Rigidbody or if kinematic
            currentlyGrabbedObject.transform.position = targetPos;
        }
        
        // Keep virtual hand visible and positioned with object during trigger mode
        // (virtualHand is parented to the object — position is maintained automatically)
        virtualHand.rotation = controllerTransform.rotation;
    }

    // Standard Go-Go scalar distance mapping (Poupyrev et al., 1996):
    //   d = |physicalHandPos - bodyReferencePos|
    //   virtualDist = d               , d <= D
    //   virtualDist = d + k * (d-D)^2 , d > D
    private float CalculateVirtualHandDistance(float d)
    {
        if (d <= goGoThreshold)
        {
            return d;
        }

        float beyond = d - goGoThreshold;
        return d + goGoScalingFactor * beyond * beyond;
    }

    // Hvirtual = physicalHandPos + k*(d-D)^2 * v, where v is the unit direction body->hand.
    private Vector3 CalculateVirtualHandPosition(Vector3 physicalHandPos, Vector3 bodyReferencePos)
    {
        Vector3 offset = physicalHandPos - bodyReferencePos;
        float d = offset.magnitude;
        if (d <= 0.0001f)
        {
            return physicalHandPos;
        }

        Vector3 v = offset / d;
        return physicalHandPos + (CalculateVirtualHandDistance(d) - d) * v;
    }

    private void ApplyGoGoMovement()
    {
        // Body reference (O): dedicated shoulder/torso transform if assigned, else the HMD.
        Vector3 bodyReferencePos = bodyReferenceTransform != null
            ? bodyReferenceTransform.position
            : Camera.main.transform.position;

        // Direction follows the hand's CURRENT aim every frame (full x/y/z movement), rotated by
        // the constant angular offset captured at grab time (centerDirectionOffset) so it starts
        // pointing exactly at the object's real bearing — combined with the distance ratio below,
        // this guarantees zero error at the moment of grab (no snap) with no lock to a fixed axis.
        Vector3 offset = controllerTransform.position - bodyReferencePos;
        float d = offset.magnitude;
        Vector3 handDirection = d > 0.0001f ? offset / d : Vector3.forward;
        Vector3 direction = (centerDirectionOffset * handDirection).normalized;

        float virtualHandDist = CalculateVirtualHandDistance(d);
        float objectDist = virtualHandDist * grabDistanceRatio;
        Vector3 targetPos = bodyReferencePos + direction * objectDist;

        // Apply rotation based on controller rotation changes
        Quaternion currentControllerRotation = controllerTransform.rotation;
        Quaternion rotationDelta = currentControllerRotation * Quaternion.Inverse(initialControllerRotation);
        Quaternion targetRotation = rotationDelta * initialObjectRotation;

        // Move object directly to the mapped position every frame — deterministic, no lag.
        Rigidbody rb = currentlyGrabbedObject.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            rb.MovePosition(targetPos);
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            currentlyGrabbedObject.transform.rotation = targetRotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        else
        {
            currentlyGrabbedObject.transform.position = targetPos;
            currentlyGrabbedObject.transform.rotation = targetRotation;
        }

        // virtualHand is parented to the object; only rotation needs manual sync.
        virtualHand.rotation = controllerTransform.rotation;
    }

    private GameObject FindClosestDirectGrabTarget()
    {
        if (controllerTransform == null || selector == null)
            return null;

        float searchRadius = Mathf.Max(0.01f, directGrabSelectionRadius);
        Collider[] hits = Physics.OverlapSphere(controllerTransform.position, searchRadius, selector.selectableLayers);
        if (hits == null || hits.Length == 0)
            return null;

        GameObject bestTarget = null;
        float bestSqrDistance = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
                continue;

            // Ignore trigger colliders so only real object colliders are considered.
            if (hit.isTrigger)
                continue;

            GameObject candidate = hit.attachedRigidbody != null ? hit.attachedRigidbody.gameObject : hit.gameObject;
            if (candidate == null)
                continue;

            float sqrDistance = (candidate.transform.position - controllerTransform.position).sqrMagnitude;
            if (sqrDistance < bestSqrDistance)
            {
                bestSqrDistance = sqrDistance;
                bestTarget = candidate;
            }
        }

        return bestTarget;
    }

    // Returns the point on the object's bounding-sphere surface closest to referencePoint.
    private Vector3 GetSurfacePoint(GameObject obj, Vector3 referencePoint)
    {
        Renderer rend = obj.GetComponent<Renderer>();
        float radius = rend != null ? rend.bounds.extents.magnitude : 0.05f;
        Vector3 dir = referencePoint - obj.transform.position;
        if (dir.sqrMagnitude < 0.000001f) dir = Vector3.forward;
        return obj.transform.position + dir.normalized * radius;
    }

    private float CalculateSpatialGain(float objectDistance, float threshold)
    {
        // Gain decreases smoothly as object comes closer to threshold.
        float safeThreshold = Mathf.Max(0.001f, threshold);
        return Mathf.Max(1f, objectDistance / safeThreshold);
    }


    private void ReleaseHand()
    {
        if (currentlyGrabbedObject != null)
        {
            // Notify selector that object is released (re-enables highlighting)
            if (selector != null)
            {
                selector.SetGrabbedState(false);
            }
            
            // Restore normal physics when released
            Rigidbody rb = currentlyGrabbedObject.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.constraints = RigidbodyConstraints.None;
                rb.useGravity = grabbedRigidbodyOriginalGravity;
            }
            
            Debug.Log("🔓 Released: " + currentlyGrabbedObject.name);
            currentlyGrabbedObject = null;
        }

        // Move virtual hand off-screen and make it visible again for next grab
        if (virtualHand != null)
        {
            virtualHand.SetParent(null); // detach from object before repositioning
            virtualHand.position = new Vector3(1000f, 1000f, 1000f);
            
            // Re-enable all renderers in hierarchy for next grab
            Renderer[] allRenderers = virtualHand.GetComponentsInChildren<Renderer>();
            foreach (Renderer rend in allRenderers)
            {
                rend.enabled = true;
            }
        }

        SetControllerVisualVisible(true);

        isAttached = false;
        smoothedControllerDelta = Vector3.zero;
        smoothedSpatialGain = 1f;
        wasMovingForwardLastFrame = false;
        forwardRecoveryStartRadius = 0f;
        forwardRecoveryStartControllerRadius = 0f;
    }

    /// <summary>
    /// Public method to force-release the hand and reposition it at the controller (ready for next grab).
    /// Called when an object is successfully placed and completes a pair.
    /// </summary>
    public void ReleaseAndRepositionToController()
    {
        ReleaseHand();
        
        // Position virtual hand at the controller, hidden until next grab
        if (virtualHand != null && controllerTransform != null)
        {
            virtualHand.position = controllerTransform.position;
            virtualHand.rotation = controllerTransform.rotation;
            
            // Hide renderers — they will be re-enabled when the next grab starts
            Renderer[] allRenderers = virtualHand.GetComponentsInChildren<Renderer>();
            foreach (Renderer rend in allRenderers)
            {
                rend.enabled = false;
            }
        }
    }

    private void StartGoGoMode(GameObject objectToGrab)
    {
        currentlyGrabbedObject = objectToGrab;
        isAttached = true;
        SetControllerVisualVisible(false);

        // Notify selector that object is grabbed (disables highlighting)
        if (selector != null)
        {
            selector.SetGrabbedState(true);
        }

        // Store starting positions for Go-Go calculation
        controllerPullStartPos = controllerTransform.position;  // Where interaction starts
        previousControllerPosition = controllerTransform.position;
        smoothedControllerDelta = Vector3.zero;
        smoothedSpatialGain = 1f;
        wasMovingForwardLastFrame = false;
        cubeStartPos = objectToGrab.transform.position;         // Object's starting position
        initialHMDPosition = Camera.main.transform.position;    // HMD position at grab
        
        // Store rotations for rotation tracking
        initialControllerRotation = controllerTransform.rotation;
        initialObjectRotation = objectToGrab.transform.rotation;
        
        // Calculate initial distance from object to HMD (for exponential gain)
        initialDistanceToController = Vector3.Distance(cubeStartPos, initialHMDPosition);
        forwardRecoveryStartRadius = initialDistanceToController;
        forwardRecoveryStartControllerRadius = Vector3.Distance(controllerPullStartPos, initialHMDPosition);

        // Zero-snap setup: capture the ratio between the object's real distance from the body
        // reference and the hand's mapped Go-Go distance at grab time. Applying this same ratio
        // every frame (see ApplyGoGoMovement) guarantees objectDist == its real grab-time distance
        // at t=0 exactly, so there is nothing to catch up on — no snap, no decay needed.
        Vector3 bodyReferencePosAtGrab = bodyReferenceTransform != null
            ? bodyReferenceTransform.position
            : initialHMDPosition;
        Vector3 objectOffsetAtGrab = cubeStartPos - bodyReferencePosAtGrab;
        float objectDistAtGrab = objectOffsetAtGrab.magnitude;

        float handDistAtGrab = Vector3.Distance(controllerPullStartPos, bodyReferencePosAtGrab);
        float virtualHandDistAtGrab = Mathf.Max(0.0001f, CalculateVirtualHandDistance(handDistAtGrab));
        grabDistanceRatio = objectDistAtGrab / virtualHandDistAtGrab;

        // Preserve the initial angular difference between the hand's direction and the object's
        // direction (both relative to the body reference) so movement still tracks the hand's
        // CURRENT aim in every frame (full x/y/z control) while starting exactly on-target.
        Vector3 initialControllerDir = controllerPullStartPos - bodyReferencePosAtGrab;
        Vector3 initialObjectDir = objectOffsetAtGrab;
        if (initialControllerDir.sqrMagnitude > 0.000001f && initialObjectDir.sqrMagnitude > 0.000001f)
        {
            centerDirectionOffset = Quaternion.FromToRotation(initialControllerDir.normalized, initialObjectDir.normalized);
        }
        else
        {
            centerDirectionOffset = Quaternion.identity;
        }

        // Freeze rotation via Rigidbody constraints so physics doesn't spin the object, and disable
        // gravity so it can't fall between our position updates (was causing a downward jump on grab).
        Rigidbody rbGrab = currentlyGrabbedObject.GetComponent<Rigidbody>();
        if (rbGrab != null && !rbGrab.isKinematic)
        {
            rbGrab.linearVelocity = Vector3.zero;
            rbGrab.angularVelocity = Vector3.zero;
            rbGrab.constraints = RigidbodyConstraints.FreezeRotation;
            grabbedRigidbodyOriginalGravity = rbGrab.useGravity;
            rbGrab.useGravity = false;
        }

        // Re-enable virtual hand renderers (may have been hidden after last successful placement)
        if (virtualHand != null)
        {
            Renderer[] handRenderers = virtualHand.GetComponentsInChildren<Renderer>();
            foreach (Renderer rend in handRenderers)
                rend.enabled = true;
        }

        // Position virtual hand on the center of the surface facing the controller.
        // Raycast from controller toward object center — this hits the middle of the facing face.
        // If the controller is already inside the collider, reverse the ray from center outward.
        Collider grabCol = objectToGrab.GetComponent<Collider>();
        Vector3 surfacePoint = objectToGrab.transform.position; // fallback: object center
        if (grabCol != null)
        {
            Vector3 toObject = objectToGrab.transform.position - controllerTransform.position;
            Vector3 dir = toObject.normalized;
            Ray ray = new Ray(controllerTransform.position, dir);
            RaycastHit hit;
            if (grabCol.Raycast(ray, out hit, 10f))
            {
                surfacePoint = hit.point;
            }
            else
            {
                // Controller is inside the collider — shoot outward from center toward controller
                Vector3 reverseDir = -dir;
                Ray reverseRay = new Ray(objectToGrab.transform.position, reverseDir);
                if (grabCol.Raycast(reverseRay, out hit, 10f))
                    surfacePoint = hit.point;
            }
        }
        virtualHand.SetParent(objectToGrab.transform);
        virtualHand.position = surfacePoint;

        Debug.Log($"✋ [TRIGGER] Go-Go mode activated on: {objectToGrab.name}");
        Debug.Log($"   Initial distance to HMD: {initialDistanceToController:F2}m");
        Debug.Log($"   Object will follow hand with exponential gain");
    }

    private void CacheControllerRenderers()
    {
        if (controllerTransform == null)
        {
            controllerRenderers = new Renderer[0];
            return;
        }

        controllerRenderers = controllerTransform.GetComponentsInChildren<Renderer>(true);
    }

    private void SetControllerVisualVisible(bool isVisible)
    {
        if (!hideControllerWhileAttached)
        {
            return;
        }

        if (controllerRenderers == null || controllerRenderers.Length == 0)
        {
            CacheControllerRenderers();
        }

        if (controllerRenderers == null)
        {
            return;
        }

        foreach (Renderer controllerRenderer in controllerRenderers)
        {
            if (controllerRenderer != null)
            {
                controllerRenderer.enabled = isVisible;
            }
        }
    }
}
