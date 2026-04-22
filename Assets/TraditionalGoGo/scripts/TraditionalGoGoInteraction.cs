using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

// Version: 1.0 - Traditional GoGo Implementation

/// <summary>
/// Traditional GoGo Interaction Technique (Poupyrev et al., 1996)
/// 
/// How it works:
/// 1. Extend hand beyond threshold (0.3m from HMD)
/// 2. Virtual hand extends exponentially farther into VR space
/// 3. Press grip to grab objects at the extended virtual hand position
/// 4. Object follows the extended virtual hand position
/// 
/// Formula: virtual_distance = real_distance (if < threshold)
///          virtual_distance = threshold + k × (real_distance - threshold)²  (if >= threshold)
/// </summary>
public class TraditionalGoGoInteraction : MonoBehaviour
{
    [Header("References")]
    public Transform virtualHand;                 // The ghost hand visual
    public Transform controllerTransform;         // The actual right-hand controller
    public Transform hmdTransform;                // Camera/HMD transform
    public InputActionProperty gripAction;        // XRI RightHand Grip (grab)
    public LayerMask selectableLayers;            // Which layers can be grabbed

    [Header("GoGo Parameters")]
    public float threshold = 0.3f;                // Distance from HMD where scaling begins
    public float scalingFactor = 20.0f;           // Exponential scaling coefficient (k) - higher = more aggressive reach
    public float maxExtension = 10.0f;            // Maximum virtual hand extension distance

    [Header("Surface Contact")]
    public float surfaceSnapSmoothing = 28f;      // Higher = tighter follow with less visible jitter

    private GameObject currentlyGrabbedObject;
    private GameObject touchingObject;            // Object that virtual hand is currently touching
    private bool isGrabbing = false;
    private Vector3 grabOffset;                   // Offset from virtual hand to object when grabbed
    private Quaternion grabRotationOffset;        // Rotation offset when grabbed
    private Quaternion virtualHandRotationOffset = Quaternion.identity;
    private bool hasLockedSurfaceLocalPoint = false;
    private Vector3 lockedSurfaceLocalPoint;
    private bool hasGrabRigidbodySettings = false;
    private RigidbodyInterpolation preGrabInterpolation;
    private CollisionDetectionMode preGrabCollisionMode;

    private bool TryGetSurfacePointFromReference(Collider objCollider, Vector3 referencePosition, out Vector3 surfacePoint)
    {
        surfacePoint = Vector3.zero;
        if (objCollider == null)
        {
            return false;
        }

        Vector3 objectCenter = objCollider.bounds.center;
        Vector3 dir = referencePosition - objectCenter;
        if (dir.sqrMagnitude < 0.00001f)
        {
            dir = Vector3.forward;
        }
        dir.Normalize();

        float castOffset = objCollider.bounds.extents.magnitude + 0.5f;
        Ray ray = new Ray(objectCenter + dir * castOffset, -dir);
        RaycastHit hit;
        if (objCollider.Raycast(ray, out hit, castOffset * 2f))
        {
            surfacePoint = hit.point;
            return true;
        }

        surfacePoint = objCollider.ClosestPoint(referencePosition);
        return true;
    }

    private void SetTouchingObject(GameObject obj)
    {
        if (touchingObject == obj)
            return;

        if (touchingObject != null)
        {
            HoverHighlight prevHighlight = touchingObject.GetComponent<HoverHighlight>();
            if (prevHighlight != null)
            {
                prevHighlight.ClearHighlight();
            }
        }

        touchingObject = obj;

        if (touchingObject != null && touchingObject != currentlyGrabbedObject)
        {
            HoverHighlight highlight = touchingObject.GetComponent<HoverHighlight>();
            if (highlight != null)
            {
                highlight.ApplyHighlight();
            }
        }
    }

    // Public accessors
    public bool IsGrabbing() { return isGrabbing; }
    public GameObject GetCurrentObject() { return currentlyGrabbedObject; }
    public GameObject GetTouchingObject() { return touchingObject; }
    public Vector3 GetVirtualHandPosition() { return CalculateVirtualHandPosition(); }

    private InputAction _returnToUIAction;

    // Grip-to-UI: initialized true so first frame is never treated as a new press (carryover guard)
    private bool _prevGripForReturn = true;

    void Start()
    {
        // Auto-find HMD if not assigned
        if (hmdTransform == null)
        {
            hmdTransform = Camera.main.transform;
        }

        if (virtualHand == null)
        {
            Debug.LogError("TraditionalGoGo: virtualHand is not assigned!");
        }

        if (controllerTransform == null)
        {
            Debug.LogError("TraditionalGoGo: controllerTransform is not assigned!");
        }

        if (virtualHand != null && controllerTransform != null)
        {
            // Preserve model alignment (prevents 180-degree flips from differing forward axes).
            virtualHandRotationOffset = Quaternion.Inverse(controllerTransform.rotation) * virtualHand.rotation;
        }

        // Setup virtual hand for collision detection
        if (virtualHand != null)
        {
            // Ensure virtual hand has a collider
            Collider virtualHandCollider = virtualHand.GetComponent<Collider>();
            if (virtualHandCollider == null)
            {
                // Add a sphere collider if none exists
                SphereCollider col = virtualHand.gameObject.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = 0.1f;
                Debug.Log("✅ Added trigger collider to virtual hand");
            }
            else
            {
                virtualHandCollider.isTrigger = true;
            }
            
            // Add collision detector component to virtual hand
            VirtualHandCollisionDetector detector = virtualHand.GetComponent<VirtualHandCollisionDetector>();
            if (detector == null)
            {
                detector = virtualHand.gameObject.AddComponent<VirtualHandCollisionDetector>();
                Debug.Log("✅ Added collision detector to virtual hand");
            }
            detector.gogoController = this;
        }

        // Hide physical controller hand model to prevent occlusion
        if (controllerTransform != null)
        {
            HideControllerVisuals(controllerTransform);
        }

        // Enable input action
        gripAction.action.Enable();

        Debug.Log("✅ Traditional GoGo Interaction initialized");
    }

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

    void Update()
    {
        if (GripReturnPressed())
        {
            SceneManager.LoadScene("UI");
            return;
        }

        if (virtualHand == null || controllerTransform == null || hmdTransform == null)
        {
            if (Time.frameCount % 120 == 0) // Log every 2 seconds
            {
                Debug.LogError($"❌ Missing references! VirtualHand: {(virtualHand == null ? "NULL" : "OK")}, Controller: {(controllerTransform == null ? "NULL" : "OK")}, HMD: {(hmdTransform == null ? "NULL" : "OK")}");
            }
            return;
        }

        // Sequential progression disables objects when a pair completes.
        // If the currently grabbed object was disabled externally, force-release
        // local grab state so the next object can be grabbed immediately.
        if (isGrabbing && currentlyGrabbedObject != null && !currentlyGrabbedObject.activeInHierarchy)
        {
            ForceReleaseAfterExternalDeactivate();
        }

        if (touchingObject != null && !touchingObject.activeInHierarchy)
        {
            SetTouchingObject(null);
        }

        // Calculate virtual hand position with GoGo scaling
        Vector3 virtualHandPos = CalculateVirtualHandPosition();
        if (!isGrabbing)
        {
            // When not grabbing, drive hand directly with GoGo mapping.
            virtualHand.position = virtualHandPos;
        }
        virtualHand.rotation = controllerTransform.rotation * virtualHandRotationOffset;
        
        // Debug log position every 2 seconds
        if (Time.frameCount % 120 == 0)
        {
            float realDist = Vector3.Distance(hmdTransform.position, controllerTransform.position);
            float virtualDist = Vector3.Distance(hmdTransform.position, virtualHandPos);
            Debug.Log($"🤚 GoGo Update | Real hand: {realDist:F2}m from HMD | Virtual hand: {virtualDist:F2}m from HMD | VirtualHandPos: {virtualHandPos}");
        }

        // Handle grabbing - grab whatever virtual hand is touching
        if (!isGrabbing && gripAction.action.WasPressedThisFrame())
        {
            if (touchingObject != null)
            {
                GrabObject(touchingObject, virtualHandPos);
            }
        }

        if (isGrabbing && gripAction.action.WasReleasedThisFrame())
        {
            ReleaseObject();
        }

        // Update grabbed object position
        if (isGrabbing && currentlyGrabbedObject != null)
        {
            MoveGrabbedObject(virtualHandPos);
        }
    }

    /// <summary>
    /// Calculate virtual hand position using Traditional GoGo formula
    /// </summary>
    private Vector3 CalculateVirtualHandPosition()
    {
        // Get real controller distance from HMD
        float realDistance = Vector3.Distance(hmdTransform.position, controllerTransform.position);

        // Safety check: if too close to HMD, place virtual hand at a safe distance
        if (realDistance < 0.05f)
        {
            // Place virtual hand 0.3m in front of HMD when controller is too close
            return hmdTransform.position + hmdTransform.forward * 0.3f;
        }

        // Direction from HMD to controller
        Vector3 directionFromHMD = (controllerTransform.position - hmdTransform.position).normalized;

        float virtualDistance;

        if (realDistance <= threshold)
        {
            // Inside threshold: 1:1 mapping
            virtualDistance = realDistance;
        }
        else
        {
            // Beyond threshold: Exponential scaling
            // Formula: D_virtual = D_real + k × (D_real - D_threshold)²
            // This ensures virtual is ALWAYS >= real
            float beyondThreshold = realDistance - threshold;
            float amplification = scalingFactor * Mathf.Pow(beyondThreshold, 2.0f);
            virtualDistance = realDistance + amplification;  // ADD to real distance, not replace

            // Clamp to maximum extension
            virtualDistance = Mathf.Min(virtualDistance, maxExtension);
        }

        // Calculate virtual hand position
        Vector3 virtualHandPosition = hmdTransform.position + directionFromHMD * virtualDistance;

        return virtualHandPosition;
    }

    /// <summary>
    /// Called by VirtualHandCollisionDetector when virtual hand touches an object
    /// </summary>
    public void OnVirtualHandTouchObject(GameObject obj)
    {
        // Check if object is on selectable layer
        if (((1 << obj.layer) & selectableLayers) != 0)
        {
            SetTouchingObject(obj);
            Debug.Log($"👆 Virtual hand touching: {touchingObject.name}");
        }
    }

    /// <summary>
    /// Called by VirtualHandCollisionDetector when virtual hand stops touching an object
    /// </summary>
    public void OnVirtualHandLeaveObject(GameObject obj)
    {
        if (obj == touchingObject)
        {
            SetTouchingObject(null);
            Debug.Log($"Virtual hand left: {obj.name}");
        }
    }

    /// <summary>
    /// Grab the specified object
    /// </summary>
    private void GrabObject(GameObject obj, Vector3 virtualHandPos)
    {
        HoverHighlight highlight = obj.GetComponent<HoverHighlight>();
        if (highlight != null)
        {
            highlight.ClearHighlight();
        }

        currentlyGrabbedObject = obj;
        isGrabbing = true;
        hasLockedSurfaceLocalPoint = false;

        // Calculate offset from virtual hand to object
        grabOffset = obj.transform.position - virtualHandPos;
        grabRotationOffset = Quaternion.Inverse(virtualHand.rotation) * obj.transform.rotation;

        // Disable physics during grab
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            hasGrabRigidbodySettings = true;
            preGrabInterpolation = rb.interpolation;
            preGrabCollisionMode = rb.collisionDetectionMode;

            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
        else
        {
            hasGrabRigidbodySettings = false;
        }

        Collider objCollider = obj.GetComponent<Collider>();
        if (objCollider != null)
        {
            Vector3 referencePos = controllerTransform != null ? controllerTransform.position : virtualHandPos;
            Vector3 surfacePoint;
            if (TryGetSurfacePointFromReference(objCollider, referencePos, out surfacePoint))
            {
                lockedSurfaceLocalPoint = obj.transform.InverseTransformPoint(surfacePoint);
                hasLockedSurfaceLocalPoint = true;
            }
        }

        Debug.Log($"✅ [GoGo] Grabbed: {obj.name} at virtual distance: {Vector3.Distance(hmdTransform.position, virtualHandPos):F2}m");
    }

    /// <summary>
    /// Move the grabbed object to follow virtual hand with exponential mapping
    /// Small real hand movements result in large virtual movements when beyond threshold
    /// </summary>
    private void MoveGrabbedObject(Vector3 virtualHandPos)
    {
        if (currentlyGrabbedObject == null)
            return;

        // Calculate target position maintaining offset
        Vector3 targetPosition = virtualHandPos + grabOffset;
        Quaternion targetRotation = virtualHand.rotation * grabRotationOffset;

        // Use Rigidbody for physics-based movement if available and not kinematic
        Rigidbody rb = currentlyGrabbedObject.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            // Use deterministic pose updates instead of velocity chasing to reduce visible jitter.
            rb.MovePosition(targetPosition);
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            currentlyGrabbedObject.transform.rotation = targetRotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        else
        {
            // Direct position for kinematic or no Rigidbody
            currentlyGrabbedObject.transform.position = targetPosition;
            currentlyGrabbedObject.transform.rotation = targetRotation;
        }

        // Keep hand rigidly attached to one surface point on the object during grab.
        // This prevents drift when GoGo amplification moves the object faster than the real controller.
        if (hasLockedSurfaceLocalPoint)
        {
            virtualHand.position = currentlyGrabbedObject.transform.TransformPoint(lockedSurfaceLocalPoint);
        }
        else
        {
            Collider objCollider = currentlyGrabbedObject.GetComponent<Collider>();
            if (objCollider != null)
            {
                Vector3 referencePos = controllerTransform != null ? controllerTransform.position : virtualHandPos;
                Vector3 surfacePoint;
                if (TryGetSurfacePointFromReference(objCollider, referencePos, out surfacePoint))
                {
                    virtualHand.position = surfacePoint;
                }
            }
        }

        // Log movement amplification every 30 frames
        if (Time.frameCount % 30 == 0)
        {
            float realDist = Vector3.Distance(hmdTransform.position, controllerTransform.position);
            float virtualDist = Vector3.Distance(hmdTransform.position, virtualHandPos);
            float amplification = realDist > 0 ? virtualDist / realDist : 1.0f;
            Debug.Log($"🤚 GoGo Manipulation | Real hand: {realDist:F2}m | Virtual reach: {virtualDist:F2}m | Amplification: {amplification:F1}x | 10cm real = {(amplification * 0.1f):F2}m virtual");
        }
    }

    /// <summary>
    /// Release the currently grabbed object
    /// </summary>
    private void ReleaseObject()
    {
        if (currentlyGrabbedObject != null)
        {
            // Re-enable physics (only for non-kinematic)
            Rigidbody rb = currentlyGrabbedObject.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.useGravity = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.constraints = RigidbodyConstraints.None;

                if (hasGrabRigidbodySettings)
                {
                    rb.interpolation = preGrabInterpolation;
                    rb.collisionDetectionMode = preGrabCollisionMode;
                }
            }

            Debug.Log($"🔓 [GoGo] Released: {currentlyGrabbedObject.name}");
            currentlyGrabbedObject = null;

            // Re-apply hover if virtual hand is still touching something.
            if (touchingObject != null)
            {
                HoverHighlight highlight = touchingObject.GetComponent<HoverHighlight>();
                if (highlight != null)
                {
                    highlight.ApplyHighlight();
                }
            }
        }

        isGrabbing = false;
        hasLockedSurfaceLocalPoint = false;
        hasGrabRigidbodySettings = false;
    }

    public void ForceReleaseForSequenceTransition()
    {
        if (isGrabbing)
        {
            ReleaseObject();
        }

        // Ensure references are clean even if release happened externally.
        if (touchingObject != null && !touchingObject.activeInHierarchy)
        {
            SetTouchingObject(null);
        }
    }

    private void ForceReleaseAfterExternalDeactivate()
    {
        currentlyGrabbedObject = null;
        isGrabbing = false;
        hasLockedSurfaceLocalPoint = false;
        hasGrabRigidbodySettings = false;

        if (touchingObject != null && !touchingObject.activeInHierarchy)
        {
            SetTouchingObject(null);
        }

        Debug.Log("[GoGo] Auto-released because grabbed object was deactivated by sequence progression.");
    }

    /// <summary>
    /// Hide all visual renderers on the physical controller to prevent occlusion of virtual hand
    /// </summary>
    private void HideControllerVisuals(Transform controller)
    {
        // Find all renderers on controller and its children
        Renderer[] renderers = controller.GetComponentsInChildren<Renderer>();
        int hiddenCount = 0;
        
        foreach (Renderer renderer in renderers)
        {
            // Don't hide the virtual hand itself!
            if (virtualHand != null && renderer.transform.IsChildOf(virtualHand))
            {
                continue;
            }
            
            renderer.enabled = false;
            hiddenCount++;
        }
        
        if (hiddenCount > 0)
        {
            Debug.Log($"👻 Hidden {hiddenCount} controller visual renderer(s) to show virtual hand clearly");
        }
    }

    /// <summary>
    /// Visualize GoGo mechanics in Scene view (Editor only)
    /// </summary>
    void OnDrawGizmos()
    {
        if (virtualHand != null && hmdTransform != null && controllerTransform != null)
        {
            Vector3 virtualHandPos = CalculateVirtualHandPosition();
            
            // Draw virtual hand position
            Gizmos.color = isGrabbing ? Color.green : (touchingObject != null ? Color.yellow : Color.cyan);
            Gizmos.DrawWireSphere(virtualHandPos, 0.05f);

            // Draw threshold sphere around HMD (shows 1:1 mapping zone)
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(hmdTransform.position, threshold);

            // Draw line from real controller to virtual hand (showing amplification)
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(controllerTransform.position, virtualHandPos);
            
            // Draw line from HMD to controller (real distance)
            Gizmos.color = Color.white;
            Gizmos.DrawLine(hmdTransform.position, controllerTransform.position);
        }
    }

    void OnDestroy()
    {
        if (isGrabbing)
        {
            ReleaseObject();
        }
    }
}