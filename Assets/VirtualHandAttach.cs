using UnityEngine;
using UnityEngine.InputSystem;

public class VirtualHandAttach : MonoBehaviour
{
    public Transform virtualHand;                 // The ghost hand visual
    public Transform controllerTransform;         // The actual right-hand controller
    public RaycastObjectSelector selector;        // The raycast script
    public InputActionProperty triggerAction;     // XRI RightHand Activate (attach hand)
    public InputActionProperty gripAction;        // XRI RightHand Grip (remote pull)
    public HandCalibrationDepthScale depthScale;  // Hand calibration system

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
    private Vector3 grabOffset;                  // Offset from virtual hand to object (constant during grab)
    private Quaternion initialControllerRotation; // Controller rotation when grab started
    private Quaternion initialObjectRotation;     // Object rotation when grab started

    // Public accessors for UserStudyManager
    public bool IsAttached() { return isAttached; }
    public bool IsRemotePulling() { return isRemotePulling; }
    public GameObject GetCurrentObject() { return currentlyGrabbedObject; }
    public RaycastObjectSelector GetSelector() { return selector; }

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
    }

    void Update()
    {
        if (virtualHand == null || selector == null)
            return;

        // ===== TRIGGER BUTTON: Attach hand and activate Go-Go mode =====
        if (triggerAction.action.WasPressedThisFrame() && !isAttached)
        {
            GameObject selected = selector.GetCurrentTarget();

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
        Vector3 cubePos = currentlyGrabbedObject.transform.position;
        Vector3 cameraPos = Camera.main.transform.position;
        Vector3 dirToCamera = (cameraPos - cubePos).normalized;
        virtualHand.position = cubePos + dirToCamera * handOffset;
        virtualHand.LookAt(cubePos);
    }

    private void ApplyGoGoMovement()
    {
        if (depthScale == null)
            return;

        // TRADITIONAL GO-GO TECHNIQUE
        // Virtual hand distance scales exponentially, object follows with proportional offset
        
        // Get current real controller distance from HMD (chest)
        float realHandDistance = Vector3.Distance(controllerTransform.position, Camera.main.transform.position);
        
        // Apply Go-Go formula to get virtual hand distance
        float virtualHandDistance = CalculateGoGoDistance(realHandDistance);
        
        // Get direction from HMD to controller (where user is pointing)
        Vector3 directionFromHMD = (controllerTransform.position - Camera.main.transform.position).normalized;
        
        // Calculate virtual hand position in world space
        Vector3 virtualHandWorldPos = Camera.main.transform.position + directionFromHMD * virtualHandDistance;
        
        // Scale the grab offset proportionally to virtual hand distance change
        // This allows object to come close when hand comes close
        float distanceRatio = virtualHandDistance / Mathf.Max(initialVirtualHandDistance, 0.001f);
        Vector3 scaledOffset = grabOffset * distanceRatio;
        
        // Object position = virtual hand + scaled offset
        Vector3 targetPos = virtualHandWorldPos + scaledOffset;
        
        // Apply rotation based on controller rotation changes
        Quaternion currentControllerRotation = controllerTransform.rotation;
        Quaternion rotationDelta = currentControllerRotation * Quaternion.Inverse(initialControllerRotation);
        Quaternion targetRotation = rotationDelta * initialObjectRotation;
        
        // Move object toward target using physics
        Rigidbody rb = currentlyGrabbedObject.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            // Calculate velocity to reach target
            Vector3 positionDiff = targetPos - currentlyGrabbedObject.transform.position;
            rb.linearVelocity = positionDiff / Time.deltaTime;
            
            // Apply rotation
            currentlyGrabbedObject.transform.rotation = targetRotation;
            
            // Prevent rotation from physics interfering
            rb.angularVelocity = Vector3.zero;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }
        else
        {
            // Fallback: direct position and rotation if no Rigidbody or if kinematic
            currentlyGrabbedObject.transform.position = targetPos;
            currentlyGrabbedObject.transform.rotation = targetRotation;
        }
        
        // Position virtual hand visual at object location
        Vector3 cubePos = currentlyGrabbedObject.transform.position;
        Vector3 cameraPos = Camera.main.transform.position;
        Vector3 dirToCamera = (cameraPos - cubePos).normalized;
        virtualHand.position = cubePos + dirToCamera * handOffset;
        virtualHand.LookAt(cubePos);
        
        // Match virtual hand rotation to controller rotation
        virtualHand.rotation = controllerTransform.rotation;
        
        // Hide virtual hand when object reaches the real controller (below threshold)
        float distanceToController = Vector3.Distance(currentlyGrabbedObject.transform.position, controllerTransform.position);
        if (virtualHand != null)
        {
            bool shouldBeVisible = distanceToController > 0.2f;
            
            // Disable all renderers in virtual hand hierarchy
            Renderer[] allRenderers = virtualHand.GetComponentsInChildren<Renderer>();
            foreach (Renderer rend in allRenderers)
            {
                rend.enabled = shouldBeVisible;
            }
        }
        
        // Debug log every 30 frames
        if (Time.frameCount % 30 == 0)
        {
            float gain = virtualHandDistance / Mathf.Max(realHandDistance, 0.001f);
            float objectDistance = Vector3.Distance(currentlyGrabbedObject.transform.position, Camera.main.transform.position);
            Debug.Log($"🎯 Go-Go | Real Hand: {realHandDistance:F3}m → Virtual Hand: {virtualHandDistance:F3}m | Gain: {gain:F2}x | Object: {objectDistance:F3}m | To Controller: {distanceToController:F3}m");
        }
    }
    
    private float CalculateGoGoDistance(float realDistance)
    {
        // Safety check: if controller is too close to HMD, use 1:1 mapping
        if (realDistance < 0.05f)
        {
            return realDistance; // Keep 1:1 mapping even when very close
        }
        
        if (realDistance <= depthScale.thresholdDistance)
        {
            // Within threshold: 1:1 mapping
            return realDistance;
        }
        else
        {
            // Beyond threshold: LINEAR GAIN for dramatic exponential effect
            // Formula: virtual_distance = threshold + k * (real_distance - threshold)
            // With k=10: 10cm real movement beyond threshold = 1m virtual movement
            float distanceBeyondThreshold = realDistance - depthScale.thresholdDistance;
            float amplifiedDistance = depthScale.maxScalingFactor * distanceBeyondThreshold;
            return depthScale.thresholdDistance + amplifiedDistance;
        }
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
                rb.constraints = RigidbodyConstraints.None;
            }
            
            Debug.Log("🔓 Released: " + currentlyGrabbedObject.name);
            currentlyGrabbedObject = null;
        }

        // Move virtual hand off-screen and make it visible again for next grab
        if (virtualHand != null)
        {
            virtualHand.position = new Vector3(1000f, 1000f, 1000f);
            
            // Re-enable all renderers in hierarchy for next grab
            Renderer[] allRenderers = virtualHand.GetComponentsInChildren<Renderer>();
            foreach (Renderer rend in allRenderers)
            {
                rend.enabled = true;
            }
        }

        isAttached = false;
    }

    private void StartGoGoMode(GameObject objectToGrab)
    {
        currentlyGrabbedObject = objectToGrab;
        isAttached = true;

        // Notify selector that object is grabbed (disables highlighting)
        if (selector != null)
        {
            selector.SetGrabbedState(true);
        }

        // Get cube size for offset
        Renderer renderer = objectToGrab.GetComponent<Renderer>();
        handOffset = 0.6f;
        if (renderer != null)
        {
            handOffset = renderer.bounds.extents.magnitude * 1.2f;
        }

        // Store starting positions for Go-Go calculation
        controllerPullStartPos = controllerTransform.position;  // Where interaction starts
        cubeStartPos = objectToGrab.transform.position;         // Object's starting position
        initialHMDPosition = Camera.main.transform.position;    // HMD position at grab
        
        // Store rotations for rotation tracking
        initialControllerRotation = controllerTransform.rotation;
        initialObjectRotation = objectToGrab.transform.rotation;
        
        // Calculate initial distance from object to HMD (for exponential gain)
        initialDistanceToController = Vector3.Distance(cubeStartPos, initialHMDPosition);
        
        // Calculate initial virtual hand distance and position using Go-Go formula
        float initialRealDistance = Vector3.Distance(controllerPullStartPos, initialHMDPosition);
        initialVirtualHandDistance = CalculateGoGoDistance(initialRealDistance);
        
        // Calculate initial virtual hand position
        Vector3 initialDirection = (controllerPullStartPos - initialHMDPosition).normalized;
        Vector3 initialVirtualHandPos = initialHMDPosition + initialDirection * initialVirtualHandDistance;
        
        // Calculate and store grab offset (constant during entire grab)
        grabOffset = cubeStartPos - initialVirtualHandPos;

        // Position virtual hand with object
        Vector3 cubePos = objectToGrab.transform.position;
        Vector3 cameraPos = Camera.main.transform.position;
        Vector3 dirToCamera = (cameraPos - cubePos).normalized;
        
        virtualHand.position = cubePos + dirToCamera * handOffset;
        virtualHand.LookAt(cubePos);

        Debug.Log($"✋ [TRIGGER] Go-Go mode activated on: {objectToGrab.name}");
        Debug.Log($"   Initial distance to HMD: {initialDistanceToController:F2}m");
        Debug.Log($"   Object will follow hand with exponential gain");
    }
}
