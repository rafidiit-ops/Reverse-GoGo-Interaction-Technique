using UnityEngine;
using UnityEngine.InputSystem;
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
    private Vector3 grabbedChestPos;              // Chest origin locked at grab time — does not shift with head movement
    private bool hasGrabRigidbodySettings = false;
    private RigidbodyInterpolation preGrabInterpolation;
    private CollisionDetectionMode preGrabCollisionMode;
    private float _virtualHandRadius = 0.1f; // radius used for OverlapSphere hover detection
    private float _releaseTime = -999f;       // Time.time when last object was released
    private const float RegrabGraceDuration = 0.5f; // seconds to keep hover alive after release
    private Vector3 _chestTransitionFromPos;  // chest pos locked at grab time, lerped back to live after release
    private GameObject _lastReleasedObject;   // object most recently released; used for unlimited re-grab grace

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
        // Auto-find HMD if not assigned OR if the assigned camera is in an inactive hierarchy
        // (scene-assigned objects in the disabled XR Origin never get TrackedPoseDriver updates)
        if (hmdTransform == null || !hmdTransform.gameObject.activeInHierarchy)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
                hmdTransform = mainCam.transform;
            else
                Debug.LogError("TraditionalGoGo: Could not find Camera.main for hmdTransform!");
        }

        if (virtualHand == null)
        {
            Debug.LogError("TraditionalGoGo: virtualHand is not assigned!");
        }

        // Auto-find controller if not assigned OR if it's in an inactive hierarchy
        // (scene-assigned controller in the disabled XR Origin is never updated by TrackedPoseDriver)
        if (controllerTransform == null || !controllerTransform.gameObject.activeInHierarchy)
        {
            // Find the live Right Hand on the persistent XR rig (DontDestroyOnLoad)
            string[] xrOriginNames = { "XR Origin (VR)", "XR Origin", "XROrigin" };
            foreach (string originName in xrOriginNames)
            {
                GameObject origin = GameObject.Find(originName);
                if (origin == null) continue;
                Transform camOffset = origin.transform.Find("Camera Offset");
                if (camOffset == null) continue;
                Transform rh = camOffset.Find("Right Hand");
                if (rh != null && rh.gameObject.activeInHierarchy) { controllerTransform = rh; break; }
            }
            if (controllerTransform == null)
                Debug.LogError("TraditionalGoGo: controllerTransform is not assigned and could not be auto-found!");
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
                _virtualHandRadius = col.radius;
                Debug.Log("✅ Added trigger collider to virtual hand");
            }
            else
            {
                virtualHandCollider.isTrigger = true;
                if (virtualHandCollider is SphereCollider sc)
                    _virtualHandRadius = sc.radius;
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
            SceneAdditiveManager.SwitchTo("UI");
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

        // Hover detection: OverlapSphere is used instead of OnTriggerEnter because the virtual
        // hand is a static trigger (no Rigidbody) and objects are kinematic Rigidbodies — Unity's
        // physics matrix does not fire trigger events between static triggers and kinematic Rigidbodies.
        if (!isGrabbing)
        {
            float detectionRadius = Mathf.Max(_virtualHandRadius * virtualHand.lossyScale.x, 0.1f);
            Collider[] hits = Physics.OverlapSphere(virtualHandPos, detectionRadius, selectableLayers);
            GameObject hovered = hits.Length > 0 ? hits[0].gameObject : null;

            // Re-grab grace: keep the last released object hoverable for the full grace window.
            // OverlapSphere can miss after release because the GoGo chest origin switches from
            // locked→live, shifting virtualHandPos by 50 cm+ at high amplification (k=80).
            // Using the stored _lastReleasedObject (not touchingObject) makes this reliable
            // for unlimited consecutive re-grabs without requiring a distance check.
            if (hovered == null
                && _lastReleasedObject != null
                && _lastReleasedObject.activeInHierarchy
                && Time.time - _releaseTime < RegrabGraceDuration)
            {
                hovered = _lastReleasedObject;
            }

            SetTouchingObject(hovered);
        }

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
    /// Uses torso-based distance (chest origin) instead of HMD, so head movement doesn't affect reach.
    /// When grabbing, uses locked chest position from grab time; when free, uses current chest position.
    /// </summary>
    private Vector3 CalculateVirtualHandPosition()
    {
        // Torso origin: 0.2 m below HMD
        // When grabbing, use the chest position locked at grab time so head movement doesn't shift the object.
        // After releasing, smoothly interpolate back to the live chest over RegrabGraceDuration.
        // Without the lerp, the instant locked→live switch amplifies any HMD drift by the GoGo factor
        // (50+ cm at k=80), causing virtualHandPos to jump away from the dropped object.
        Vector3 chestPos;
        if (isGrabbing)
        {
            chestPos = grabbedChestPos;
        }
        else
        {
            Vector3 liveChest = hmdTransform.position + Vector3.down * 0.2f;
            if (_releaseTime > 0f && Time.time - _releaseTime < RegrabGraceDuration)
            {
                float t = Mathf.Clamp01((Time.time - _releaseTime) / RegrabGraceDuration);
                chestPos = Vector3.Lerp(_chestTransitionFromPos, liveChest, t);
            }
            else
            {
                chestPos = liveChest;
            }
        }

        // Get real controller distance from chest (torso)
        float realDistance = Vector3.Distance(chestPos, controllerTransform.position);

        // Safety check: if too close to chest, place virtual hand at a safe distance
        if (realDistance < 0.05f)
        {
            // Place virtual hand 0.3m in front of chest when controller is too close
            return chestPos + (controllerTransform.position - chestPos).normalized * 0.3f;
        }

        // Direction from chest to controller
        Vector3 directionFromChest = (controllerTransform.position - chestPos).normalized;

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
            float beyondThreshold = realDistance - threshold;
            float amplification = scalingFactor * Mathf.Pow(beyondThreshold, 2.0f);
            virtualDistance = realDistance + amplification;

            // Clamp to maximum extension
            virtualDistance = Mathf.Min(virtualDistance, maxExtension);
        }

        // Calculate virtual hand position from chest origin
        Vector3 virtualHandPosition = chestPos + directionFromChest * virtualDistance;

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
        _releaseTime = -999f; // cancel any active grace period

        // Lock chest position at grab time so head movement doesn't affect the held object.
        grabbedChestPos = hmdTransform.position + Vector3.down * 0.2f;

        // Zero grabOffset so the object center stays exactly at VP during drag.
        // A non-zero offset accumulates errors across multiple re-grabs and causes
        // the object to drift away from the virtual hand ("controller not attached" bug).
        grabOffset = Vector3.zero;
        grabRotationOffset = Quaternion.Inverse(virtualHand.rotation) * obj.transform.rotation;

        // Disable physics during grab
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        { rb.angularVelocity = Vector3.zero;
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
            // Use controllerTransform.position (real hand) as the surface-point reference.
            // The real hand is always far from the distant object, so the direction vector is
            // never near-zero. With grabOffset=0, VP == object center exactly, making VP a
            // bad reference (dir ≈ 0 → wrong face fallback). The real hand points to the NEAR
            // face of the object (the face facing the user), so the virtual hand model appears
            // correctly on that near face during the drag.
            Vector3 referencePos = controllerTransform != null ? controllerTransform.position : hmdTransform.position;
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

        // Virtual hand: place on the near face of the object (the face facing the user's real hand).
        // IMPORTANT: do NOT use Collider.bounds, Collider.Raycast, or Collider.ClosestPoint here.
        // Those APIs read from Unity's physics-engine state, which is only synchronized at FixedUpdate.
        // When the object is moved via transform.position inside Update (kinematic objects), the
        // physics state lags one physics step behind — so all Collider-based methods still return
        // the PREVIOUS frame's position, leaving the virtual hand frozen while the object moves.
        // Using transform.position directly is always frame-current.
        {
            Vector3 objCenter = currentlyGrabbedObject.transform.position; // always current
            Vector3 toUser = (controllerTransform != null ? controllerTransform.position : hmdTransform.position)
                             - objCenter;
            if (toUser.sqrMagnitude > 0.0001f)
            {
                toUser.Normalize();
                // Half-extent of the object in the toward-user direction (works for uniform cube scale).
                float nearFaceOffset = currentlyGrabbedObject.transform.lossyScale.x * 0.5f;
                virtualHand.position = objCenter + toUser * nearFaceOffset;
            }
            else
            {
                virtualHand.position = objCenter; // user is right at the object — put hand at center
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
            _lastReleasedObject = currentlyGrabbedObject; // store for unlimited re-grab grace
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
        _chestTransitionFromPos = grabbedChestPos; // start smooth chest blend from here
        _releaseTime = Time.time; // start re-grab grace period
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