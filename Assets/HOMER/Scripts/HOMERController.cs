using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

/// <summary>
/// Standalone HOMER interaction controller for HOMERStarterScene.
///
/// HOMER algorithm:
///   1. User presses trigger -> ray-cast selects the target object.
///   2. Distance scale is computed: scale = objectDist / controllerDist (HMD-based).
///   3. While held, controller motion is amplified by that scale so the user can
///      reach objects far away with smaller hand movements.
///
/// This script is completely independent of VirtualHandAttach / ReverseGoGo.
/// Add it to a GameObject in HOMERStarterScene and wire the Inspector references.
/// It will automatically disable conflicting Reverse-GoGo scripts at Start.
/// </summary>
public class HOMERController : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // Inspector fields — wire these up in the Unity Editor
    // -----------------------------------------------------------------------

    [Header("Required References")]
    [Tooltip("The right-hand controller transform (XRI RightHand Controller).")]
    public Transform controllerTransform;

    [Tooltip("HMD / Camera transform. Leave empty to use Camera.main automatically.")]
    public Transform hmdTransform;

    [Tooltip("Optional remote-hand visual that travels to the grabbed object and follows it while holding.")]
    public Transform virtualHand;

    [Header("Input Actions")]
    [Tooltip("XRI RightHand / Activate Value — pressed to select and hold an object.")]
    public InputActionProperty triggerAction;

    [Tooltip("XRI RightHand / Grip — held to clutch (pause tracking and re-centre).")]
    public InputActionProperty gripAction;

    private InputAction _returnToUIAction;

    // Grip-to-UI: initialized true so first frame is never treated as a new press (carryover guard)
    private bool _prevGripForReturn = true;

    [Header("Selection")]
    [Tooltip("Physics layers that can be grabbed.")]
    public LayerMask grabbableLayers = Physics.DefaultRaycastLayers;

    [Tooltip("If true, the visual ray selector uses the same layer mask as HOMER grabbing.")]
    public bool forceSelectorToGrabbableLayers = true;

    [Tooltip("If Grabbable Layers is still default, auto-switch to this layer if it exists.")]
    public string preferredGrabbableLayerName = "Interactable";

    [Tooltip("Automatically narrow default mask to the preferred layer for HOMER scenes.")]
    public bool autoUsePreferredLayerWhenMaskIsDefault = true;

    [Tooltip("If true, force HOMER to target only the preferred layer (recommended for phantom-only scenes).")]
    public bool strictPreferredLayerOnly = true;

    [Tooltip("Maximum ray length for object selection (metres). 0 = infinite.")]
    public float selectionRayLength = 0f;

    [Tooltip("Optional: existing RaycastObjectSelector for visual ray / highlight only.")]
    public RaycastObjectSelector raycastSelector;

    [Header("HOMER Parameters")]
    [Tooltip("Minimum controller distance from HMD used in the scale divisor (prevents divide-by-zero).")]
    public float minControllerDistance = 0.05f;

    [Tooltip("Upper limit on the ratio-based HOMER scale (objectDist/controllerDist).")]
    public float maxDistanceScale = 15f;

    [Tooltip("Maximum physical hand movement (metres) allowed from the grab-anchor before the object movement is clamped. "
           + "The actual object displacement cap = maxHandReach × scaleFactor, so it varies naturally with each grab. 0 = no cap.")]
    public float maxHandReach = 0.5f;

    [Tooltip("Keep object rotation fixed during grab. Disable this to let the object follow hand/controller rotation.")]
    public bool keepInitialObjectRotation = false;

    [Tooltip("How quickly the virtual hand position transitions to the grab point on attach (seconds). 0 = instant.")]
    public float grabTransitionDuration = 0.15f;

    [Header("Clutch")]
    [Tooltip("Hold grip to freeze object and re-centre the controller mapping without dropping.")]
    public bool enableClutch = true;

    [Header("Held Visuals")]
    [Tooltip("Hide controller/hand renderers while an object is held.")]
    public bool hideControllerVisualsWhileHolding = true;

    [Tooltip("Animate the remote hand from real controller pose to the grabbed object on attach.")]
    public bool shootRemoteHandOnGrab = true;

    [Tooltip("Keep remote hand anchored on the grabbed object's outer surface.")]
    public bool attachRemoteHandOnSurface = true;

    [Tooltip("Small outward offset from the surface to prevent visual clipping.")]
    public float remoteHandSurfaceOffset = 0.015f;

    [Header("Conflict Resolution")]
    [Tooltip("Disable VirtualHandAttach, XRReverseGoGo and ReverseGoGoGrab found in the scene.")]
    public bool disableConflictingScripts = true;

    [Header("Debug")]
    [Tooltip("Print trigger value and grab state every frame to the console.")]
    public bool debugMode = true;

    // -----------------------------------------------------------------------
    // Runtime state
    // -----------------------------------------------------------------------

    // Enforce conflicting-script disable for the first N frames after Start,
    // so any script that runs after us (e.g. GoGoModeToggle) cannot re-enable
    // VirtualHandAttach or other Reverse-GoGo scripts.
    private int _startupFramesLeft = 10;

    private GameObject _heldObject;
    private Rigidbody _heldRigidbody;
    private bool _wasKinematic;
    private bool _hadGravity;

    private bool _isHolding;
    private bool _isClutching;

    // Manual edge-detection for trigger (more reliable than WasPressedThisFrame in XR).
    private float _prevTriggerValue;
    private float _prevGripValue;
    private const float TriggerPressThreshold = 0.55f;
    private const float TriggerReleaseThreshold = 0.20f;

    // Scene-level selector cache so we can suppress all ray visuals while trigger is held.
    private RaycastObjectSelector[] _sceneRaySelectors;

    // Cache the last highlighted target so exec-order gaps don't cause a missed grab.
    private GameObject _lastHighlightedTarget;

    // HOMER 1:1 mapping: frame-by-frame delta tracking
    private Vector3 _prevControllerPos;
    private Quaternion _prevControllerRot;

    // Still needed for clutch re-anchor and transition start
    private Vector3 _controllerAnchorPos;
    private Quaternion _controllerAnchorRot;
    private Vector3 _objectAnchorPos;
    private Quaternion _objectAnchorRot;
    private float _distanceScale = 1f;
    private Vector3 _handStartLocalOffset;

    // Grab-start transition (virtualHand snap)
    private bool _isTransitioning;
    private float _transitionElapsed;
    private Vector3 _transitionStartPos;
    private Quaternion _transitionStartRot;

    // Controller visuals cache for hide/restore during hold.
    private Renderer[] _controllerRenderers;
    private bool[] _controllerRendererInitialEnabled;
    private bool _warnedMissingVirtualHand;

    // Surface-attach cache for remote hand visual.
    private bool _hasSurfaceAttach;
    private Vector3 _grabLocalPoint;
    private Vector3 _grabLocalNormal;
    private Vector3 _lastSelectionHitPoint;
    private Vector3 _lastSelectionHitNormal;
    private bool _hasLastSelectionHit;

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>Whether an object is currently being held.</summary>
    public bool IsAttached() => _isHolding;

    /// <summary>Whether clutch mode is active.</summary>
    public bool IsClutching() => _isClutching;

    /// <summary>The object currently held (null if none).</summary>
    public GameObject GetCurrentObject() => _heldObject;

    /// <summary>
    /// Release any held object immediately (called by SequentialPairVisibility on pair complete).
    /// </summary>
    public void ForceRelease()
    {
        if (!_isHolding)
        {
            return;
        }

        EndGrab();
    }

    // -----------------------------------------------------------------------
    // Unity lifecycle
    // -----------------------------------------------------------------------

    private void Start()
    {
        AutoSetup();
        ConfigureSelectionMasks();
        CacheSceneRaySelectors();
        CacheControllerRenderers();
        EnableInputActions();

        if (disableConflictingScripts)
        {
            DisableConflictingTechniqueScripts();
        }

        LogSetupStatus();
    }

    private void OnDisable()
    {
        // Safety: never leave controller visuals hidden if this component is disabled.
        SetControllerVisualsHidden(false);
    }

    private void CacheSceneRaySelectors()
    {
        _sceneRaySelectors = FindObjectsByType<RaycastObjectSelector>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
    }

    private void CacheControllerRenderers()
    {
        if (!hideControllerVisualsWhileHolding)
        {
            return;
        }

        Transform source = GetTrackingTransform();
        if (source == null)
        {
            source = controllerTransform;
        }

        if (source == null)
        {
            return;
        }

        _controllerRenderers = source.GetComponentsInChildren<Renderer>(true);
        _controllerRendererInitialEnabled = new bool[_controllerRenderers.Length];
        for (int i = 0; i < _controllerRenderers.Length; i++)
        {
            _controllerRendererInitialEnabled[i] = _controllerRenderers[i] != null && _controllerRenderers[i].enabled;
        }
    }

    private void SetControllerVisualsHidden(bool hidden)
    {
        if (!hideControllerVisualsWhileHolding)
        {
            return;
        }

        if (_controllerRenderers == null || _controllerRendererInitialEnabled == null)
        {
            CacheControllerRenderers();
        }

        if (_controllerRenderers == null || _controllerRendererInitialEnabled == null)
        {
            return;
        }

        int count = Mathf.Min(_controllerRenderers.Length, _controllerRendererInitialEnabled.Length);
        for (int i = 0; i < count; i++)
        {
            Renderer r = _controllerRenderers[i];
            if (r == null)
            {
                continue;
            }

            // Never hide the remote hand visual itself.
            if (virtualHand != null && r.transform.IsChildOf(virtualHand))
            {
                continue;
            }

            r.enabled = hidden ? false : _controllerRendererInitialEnabled[i];
        }
    }

    private void SetRaySuppressed(bool suppressed)
    {
        if (_sceneRaySelectors == null || _sceneRaySelectors.Length == 0)
        {
            CacheSceneRaySelectors();
        }

        if (_sceneRaySelectors == null)
        {
            return;
        }

        for (int i = 0; i < _sceneRaySelectors.Length; i++)
        {
            RaycastObjectSelector selector = _sceneRaySelectors[i];
            if (selector == null)
            {
                continue;
            }

            if (forceSelectorToGrabbableLayers)
            {
                selector.selectableLayers = grabbableLayers;
            }

            selector.SetGrabbedState(suppressed);

            // Hard-disable line renderer while suppressed so no stale frame remains visible.
            if (suppressed && selector.lineRenderer != null)
            {
                selector.lineRenderer.enabled = false;
            }
        }
    }

    private void ConfigureSelectionMasks()
    {
        if (strictPreferredLayerOnly)
        {
            int strictLayer = LayerMask.NameToLayer(preferredGrabbableLayerName);
            if (strictLayer != -1)
            {
                grabbableLayers = LayerMask.GetMask(preferredGrabbableLayerName);
                Debug.Log($"[HOMERController] Strict layer mode enabled. Targeting only '{preferredGrabbableLayerName}'.");
            }
            else
            {
                Debug.LogWarning($"[HOMERController] Preferred layer '{preferredGrabbableLayerName}' not found. Using current Grabbable Layers mask.");
            }
        }

        if (autoUsePreferredLayerWhenMaskIsDefault && grabbableLayers.value == Physics.DefaultRaycastLayers)
        {
            int preferredLayer = LayerMask.NameToLayer(preferredGrabbableLayerName);
            if (preferredLayer != -1)
            {
                grabbableLayers = LayerMask.GetMask(preferredGrabbableLayerName);
                Debug.Log($"[HOMERController] Grabbable Layers auto-set to '{preferredGrabbableLayerName}'.");
            }
        }

        if (forceSelectorToGrabbableLayers && raycastSelector != null)
        {
            raycastSelector.selectableLayers = grabbableLayers;
        }
    }

    private void AutoSetup()
    {
        // Auto-resolve HMD.
        if (hmdTransform == null && Camera.main != null)
        {
            hmdTransform = Camera.main.transform;
        }

        // Auto-resolve remote hand visual if not explicitly assigned.
        if (virtualHand == null)
        {
            if (raycastSelector != null)
            {
                Transform fromSelector = raycastSelector.transform.Find("VirtualHand");
                if (fromSelector != null)
                {
                    virtualHand = fromSelector;
                }
            }

            if (virtualHand == null)
            {
                GameObject vh = GameObject.Find("VirtualHand");
                if (vh != null)
                {
                    virtualHand = vh.transform;
                }
            }
        }

        // IMPORTANT: keep HOMER mapping source independent from selector ray origin.
        // Do not auto-assign controllerTransform from raycastSelector.rayOrigin,
        // because rayOrigin may be driven by Reverse GoGo logic in duplicated scenes.

        // Last-resort: use this GameObject's own transform.
        if (controllerTransform == null)
        {
            controllerTransform = transform;
            Debug.LogWarning("[HOMERController] controllerTransform was not set. Falling back to this GameObject's transform. Please assign the right-hand controller transform.");
        }
    }

    private void LogSetupStatus()
    {
        if (triggerAction.action == null)
        {
            Debug.LogError("[HOMERController] Trigger Action is not assigned! Grabbing will not work. Assign XRI Right Interaction / Activate Value.");
        }

        if (raycastSelector == null)
        {
            Debug.LogWarning("[HOMERController] RaycastSelector is not assigned. Objects must be on Grabbable Layers for fallback raycast to work.");
        }

        if (shootRemoteHandOnGrab && virtualHand == null && !_warnedMissingVirtualHand)
        {
            _warnedMissingVirtualHand = true;
            Debug.LogWarning("[HOMERController] shootRemoteHandOnGrab is enabled but VirtualHand is not assigned/found. Remote hand travel will not be visible.");
        }
    }

    private void Update()
    {
        if (GripReturnPressed())
        {
            SceneManager.LoadScene("UI");
            return;
        }

        // Re-enforce on first few frames so nothing can re-enable conflicting scripts.
        if (_startupFramesLeft > 0)
        {
            _startupFramesLeft--;
            if (disableConflictingScripts)
                DisableConflictingTechniqueScripts();
        }

        Transform tracking = GetTrackingTransform();
        if (tracking == null)
        {
            return;
        }

        UpdateTargetCache();
        HandleSelectionInput();
        HandleClutchInput();
        DriveHeldObject();
    }

    private Transform GetTrackingTransform()
    {
        // HOMER motion mapping must use the physical controller transform.
        if (controllerTransform != null)
        {
            return controllerTransform;
        }

        // Fallback if controller was not wired.
        if (raycastSelector != null && raycastSelector.rayOrigin != null)
        {
            return raycastSelector.rayOrigin;
        }

        return null;
    }

    private Transform GetSelectionTransform()
    {
        // Selection should follow the visible ray origin when available.
        if (raycastSelector != null && raycastSelector.rayOrigin != null)
        {
            return raycastSelector.rayOrigin;
        }

        return GetTrackingTransform();
    }

    private void UpdateTargetCache()
    {
        if (raycastSelector == null) return;

        if (forceSelectorToGrabbableLayers && raycastSelector.selectableLayers.value != grabbableLayers.value)
        {
            raycastSelector.selectableLayers = grabbableLayers;
        }
        
        GameObject t = raycastSelector.GetCurrentTarget();
        if (t != null && IsOnGrabbableLayer(t))
        {
            _lastHighlightedTarget = t;
            if (debugMode && !_isHolding)
            {
                Debug.Log($"[HOMER] Cache updated: {t.name}");
            }
        }
    }

    // -----------------------------------------------------------------------
    // Input handling
    // -----------------------------------------------------------------------

    private void HandleSelectionInput()
    {
        if (triggerAction.action == null)
        {
            return;
        }

        float triggerValue = 0f;
        try { triggerValue = triggerAction.action.ReadValue<float>(); }
        catch { triggerValue = 0f; }

        // Hysteresis: press high, release low, to avoid analog jitter dropping grabs.
        bool pressedThisFrame = triggerValue >= TriggerPressThreshold && _prevTriggerValue < TriggerPressThreshold;
        bool releasedThisFrame = triggerValue <= TriggerReleaseThreshold && _prevTriggerValue > TriggerReleaseThreshold;
        _prevTriggerValue = triggerValue;

        // Keep ray visible only while trigger is not pressed.
        // This matches HOMER behavior requested for this scene.
        bool triggerHeld = triggerValue > TriggerReleaseThreshold;
        SetRaySuppressed(triggerHeld);

        if (debugMode)
        {
            Debug.Log($"[HOMER] trigger={triggerValue:F2} holding={_isHolding} pressed={pressedThisFrame} target={(raycastSelector != null ? raycastSelector.GetCurrentTarget()?.name ?? "none" : "no selector")}");
        }

        if (!_isHolding && pressedThisFrame)
        {
            TryGrab();
        }
        else if (_isHolding && releasedThisFrame)
        {
            EndGrab();
        }
    }

    private void HandleClutchInput()
    {
        if (!enableClutch || gripAction.action == null || !_isHolding)
        {
            return;
        }

        float gripValue = 0f;
        try { gripValue = gripAction.action.ReadValue<float>(); }
        catch { gripValue = 0f; }

        bool gripPressedThisFrame = gripValue >= TriggerPressThreshold && _prevGripValue < TriggerPressThreshold;
        bool gripReleasedThisFrame = gripValue <= TriggerReleaseThreshold && _prevGripValue > TriggerReleaseThreshold;
        _prevGripValue = gripValue;

        if (!_isClutching && gripPressedThisFrame)
        {
            BeginClutch();
        }
        else if (_isClutching && gripReleasedThisFrame)
        {
            EndClutch();
        }
    }

    // -----------------------------------------------------------------------
    // Grab lifecycle
    // -----------------------------------------------------------------------

    private void TryGrab()
    {
        // 1. Raycast to find a target.
        GameObject target = FindTarget();
        if (target == null)
        {
            Debug.Log("[HOMERController] Trigger pressed but no target found. Make sure the ray is pointing at a grabbable object.");
            return;
        }

        Debug.Log($"[HOMERController] Grabbing '{target.name}'");

        _heldObject = target;
        _isHolding = true;
        _isClutching = false;
        _lastHighlightedTarget = null;  // clear cache while holding
        _hasSurfaceAttach = false;
        SetControllerVisualsHidden(true);

        // Tell the visual ray selector we have grabbed so it stops showing.
        if (raycastSelector != null)
        {
            raycastSelector.SetGrabbedState(true);
        }

        // 2. Record mapping anchors.
        Transform tracking = GetTrackingTransform();
        _controllerAnchorPos = tracking.position;
        _controllerAnchorRot = tracking.rotation;
        _objectAnchorPos = _heldObject.transform.position;
        _objectAnchorRot = _heldObject.transform.rotation;
        _handStartLocalOffset = hmdTransform != null
            ? (_controllerAnchorPos - hmdTransform.position)
            : Vector3.zero;

        CacheSurfaceAttachData(tracking);

        // 3. 1:1 mapping — physical hand movement equals object movement.
        _distanceScale = 1f;
        _prevControllerPos = tracking.position;
        _prevControllerRot = tracking.rotation;

        Debug.Log($"[HOMER Grab] 1:1 mapping active | object={_objectAnchorPos} | controller={tracking.position}");

        // 4. Disable physics while held.
        _heldRigidbody = _heldObject.GetComponent<Rigidbody>();
        if (_heldRigidbody != null)
        {
            _wasKinematic = _heldRigidbody.isKinematic;
            _hadGravity = _heldRigidbody.useGravity;
            _heldRigidbody.isKinematic = true;
            _heldRigidbody.useGravity = false;
            _heldRigidbody.linearVelocity = Vector3.zero;
            _heldRigidbody.angularVelocity = Vector3.zero;
        }

        // 5. Start snap transition for the virtual hand.
        _transitionElapsed = 0f;
        _isTransitioning = grabTransitionDuration > 0.0001f;

        if (virtualHand != null)
        {
            Transform visualStartTracking = GetTrackingTransform();
            if (shootRemoteHandOnGrab && visualStartTracking != null)
            {
                // Start from real hand/controller pose so travel is visible.
                _transitionStartPos = visualStartTracking.position;
                _transitionStartRot = visualStartTracking.rotation;
            }
            else
            {
                _transitionStartPos = virtualHand.position;
                _transitionStartRot = virtualHand.rotation;
            }
            virtualHand.gameObject.SetActive(true);
        }
        else
        {
            _transitionStartPos = _objectAnchorPos;
            _transitionStartRot = _objectAnchorRot;
        }

        Debug.Log($"[HOMER] Grabbed '{target.name}' | distanceScale={_distanceScale:F2}");
    }

    private void EndGrab()
    {
        if (raycastSelector != null)
        {
            raycastSelector.SetGrabbedState(false);
        }

        if (_heldRigidbody != null)
        {
            _heldRigidbody.isKinematic = _wasKinematic;
            _heldRigidbody.useGravity = _hadGravity;
            _heldRigidbody = null;
        }

        _heldObject = null;
        _isHolding = false;
        _isClutching = false;
        _isTransitioning = false;
        _lastHighlightedTarget = null;  // clear cache on release
        _hasSurfaceAttach = false;
        _hasLastSelectionHit = false;
        SetControllerVisualsHidden(false);

        if (virtualHand != null)
        {
            virtualHand.gameObject.SetActive(false);
        }
    }

    // -----------------------------------------------------------------------
    // Clutch
    // -----------------------------------------------------------------------

    private void BeginClutch()
    {
        _isClutching = true;
        // Object stops moving; controller is free to re-centre.
    }

    private void EndClutch()
    {
        if (!_isHolding || _heldObject == null)
        {
            _isClutching = false;
            return;
        }

        // Re-anchor at the current controller position without snapping the object.
        Transform tracking = GetTrackingTransform();
        _controllerAnchorPos = tracking.position;
        _controllerAnchorRot = tracking.rotation;
        _objectAnchorPos = _heldObject.transform.position;
        _objectAnchorRot = _heldObject.transform.rotation;
        _prevControllerPos = tracking.position;
        _prevControllerRot = tracking.rotation;

        _isTransitioning = false;
        _isClutching = false;
    }

    // -----------------------------------------------------------------------
    // Per-frame movement
    // -----------------------------------------------------------------------

    private void DriveHeldObject()
    {
        if (!_isHolding || _heldObject == null)
        {
            return;
        }

        if (_isClutching)
        {
            // Keep object frozen; optionally drive virtual hand to controller.
            Transform tracking = GetTrackingTransform();
            if (virtualHand != null)
            {
                virtualHand.position = tracking.position;
                virtualHand.rotation = tracking.rotation;
            }

            return;
        }

        Transform movementSource = GetTrackingTransform();

        // 1:1 incremental mapping: object moves by exactly the same world-space
        // delta as the physical controller moved this frame.
        Vector3 frameDelta = movementSource.position - _prevControllerPos;
        Vector3 targetObjectPos = _heldObject.transform.position + frameDelta;
        _prevControllerPos = movementSource.position;

        Quaternion frameRotDelta = movementSource.rotation * Quaternion.Inverse(_prevControllerRot);
        Quaternion targetObjectRot = frameRotDelta * _heldObject.transform.rotation;
        _prevControllerRot = movementSource.rotation;

        if (debugMode)
        {
            Debug.Log(
                $"[HOMER Hold] frameΔ={frameDelta.magnitude:F4}m | controller={movementSource.position} | object={targetObjectPos}");
        }

        // Grab-start transition blend.
        float blend = 1f;
        if (_isTransitioning)
        {
            _transitionElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_transitionElapsed / Mathf.Max(0.0001f, grabTransitionDuration));
            blend = t * t * (3f - 2f * t); // SmoothStep
            if (blend >= 0.9999f)
            {
                _isTransitioning = false;
            }
        }

        Vector3 handPos = _isTransitioning
            ? Vector3.Lerp(_transitionStartPos, targetObjectPos, blend)
            : targetObjectPos;

        // Keep remote hand orientation fixed while it travels outward;
        // this avoids visible bending/twisting during the attach flight.
        Quaternion handRot = _isTransitioning
            ? _transitionStartRot
            : targetObjectRot;

        // Drive the held object directly to HOMER target each frame for stability.
        // Only the virtual hand visual uses transition blending.
        _heldObject.transform.position = targetObjectPos;
        _heldObject.transform.rotation = targetObjectRot;

        if (virtualHand != null)
        {
            // Preserve visible "shoot out" travel first, then lock to surface once transition is complete.
            if (!_isTransitioning && attachRemoteHandOnSurface && _hasSurfaceAttach)
            {
                Vector3 surfacePos = _heldObject.transform.TransformPoint(_grabLocalPoint);
                Vector3 surfaceNormal = _heldObject.transform.TransformDirection(_grabLocalNormal).normalized;
                handPos = surfacePos + surfaceNormal * Mathf.Max(0f, remoteHandSurfaceOffset);
            }

            virtualHand.position = handPos;
            virtualHand.rotation = handRot;
        }
    }

    // -----------------------------------------------------------------------
    // Raycast selection
    // -----------------------------------------------------------------------

    private GameObject FindTarget()
    {
        _hasLastSelectionHit = false;

        // Primary: always-on physics raycast from controller forward (bulletproof, no selector dependency).
        Transform selectionSource = GetSelectionTransform();
        if (selectionSource != null && grabbableLayers.value != 0)
        {
            float maxDist = selectionRayLength > 0f ? selectionRayLength : Mathf.Infinity;
            Ray ray = new Ray(selectionSource.position, selectionSource.forward);

            if (Physics.Raycast(ray, out RaycastHit hit, maxDist, grabbableLayers))
            {
                GameObject hitObject = hit.collider.gameObject;
                if (IsOnGrabbableLayer(hitObject))
                {
                    _hasLastSelectionHit = true;
                    _lastSelectionHitPoint = hit.point;
                    _lastSelectionHitNormal = hit.normal;
                    return hitObject;
                }
            }
        }

        // Fallback: use cached target from visual selector (may be stale but better than nothing).
        if (_lastHighlightedTarget != null && _lastHighlightedTarget.activeSelf && IsOnGrabbableLayer(_lastHighlightedTarget))
        {
            return _lastHighlightedTarget;
        }

        return null;
    }

    private void CacheSurfaceAttachData(Transform tracking)
    {
        if (_heldObject == null)
        {
            return;
        }

        Vector3 worldPoint;
        Vector3 worldNormal;

        if (_hasLastSelectionHit && _lastSelectionHitNormal.sqrMagnitude > 0.000001f)
        {
            worldPoint = _lastSelectionHitPoint;
            worldNormal = _lastSelectionHitNormal.normalized;
        }
        else
        {
            Vector3 towardsController = tracking != null
                ? (tracking.position - _heldObject.transform.position)
                : Vector3.up;

            worldNormal = towardsController.sqrMagnitude > 0.000001f
                ? towardsController.normalized
                : Vector3.up;

            Collider c = _heldObject.GetComponent<Collider>();
            if (c != null)
            {
                worldPoint = c.ClosestPoint(_heldObject.transform.position + worldNormal * 10f);
            }
            else
            {
                worldPoint = _heldObject.transform.position;
            }
        }

        _grabLocalPoint = _heldObject.transform.InverseTransformPoint(worldPoint);
        _grabLocalNormal = _heldObject.transform.InverseTransformDirection(worldNormal).normalized;
        _hasSurfaceAttach = _grabLocalNormal.sqrMagnitude > 0.000001f;
    }

    private bool IsOnGrabbableLayer(GameObject obj)
    {
        return obj != null && ((1 << obj.layer) & grabbableLayers.value) != 0;
    }

    // -----------------------------------------------------------------------
    // Conflict resolution
    // -----------------------------------------------------------------------

    private void DisableConflictingTechniqueScripts()
    {
        DisableAll<VirtualHandAttach>();
        DisableAll<XRReverseGoGo>();
        DisableAll<ReverseGoGoGrab>();
        DisableAll<TraditionalGoGoInteraction>();
        DisableAll<HOMERInteraction>();

        // Disable arm-length calibration — HOMER does not use it.
        // Also clear the depthScale reference on RaycastObjectSelector so its
        // IsRayExtensionActive() no longer waits for calibration to complete.
        HandCalibrationDepthScale[] calibrations = FindObjectsByType<HandCalibrationDepthScale>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < calibrations.Length; i++)
        {
            if (calibrations[i] != null)
            {
                calibrations[i].enabled = false;
            }
        }

        // Sever the depthScale link on every RaycastObjectSelector so the ray
        // activates purely by distance from HMD (no calibration gate).
        RaycastObjectSelector[] selectors = FindObjectsByType<RaycastObjectSelector>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < selectors.Length; i++)
        {
            if (selectors[i] != null)
            {
                selectors[i].depthScale = null;
            }
        }
    }

    private static void DisableAll<T>() where T : MonoBehaviour
    {
        T[] found = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null)
            {
                found[i].enabled = false;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Input action helpers
    // -----------------------------------------------------------------------

    private void EnableInputActions()
    {
        if (triggerAction.action != null)
        {
            // Enable both the action map and the individual action.
            triggerAction.action.actionMap?.Enable();
            triggerAction.action.Enable();
        }

        if (gripAction.action != null)
        {
            gripAction.action.actionMap?.Enable();
            gripAction.action.Enable();
        }
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

    private void OnDestroy()
    {
        if (triggerAction.action != null)
        {
            triggerAction.action.Disable();
        }

        if (gripAction.action != null)
        {
            gripAction.action.Disable();
        }
    }
}
