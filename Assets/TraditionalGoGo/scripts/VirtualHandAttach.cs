using UnityEngine;
using UnityEngine.InputSystem;

public class VirtualHandAttach : MonoBehaviour
{
    [Header("References")]
    public Transform virtualHand;
    public Transform controllerTransform;
    public RaycastObjectSelector selector;
    public InputActionProperty triggerAction;
    public InputActionProperty gripAction;
    public HandCalibrationDepthScale depthScale;

    [Header("Traditional GoGo Mapping")]
    public float thresholdDistance = 0.3f;
    public float scalingFactor = 20.0f;
    public float maxExtensionDistance = 10.0f;

    [Header("Visual")]
    public bool hideControllerWhileAttached = true;
    public float attachMoveDeadzone = 0.015f;
    public float immediateGrabDistance = 0.12f;

    [Header("Pull Stability")]
    public float pullActivationRampTime = 0.08f;
    public float pullStartTransitionBlendTime = 0.18f;
    public float hmdSafetyRadius = 0.12f;
    public float closeRegrabPullExponent = 2.4f;
    public float farPullExponent = 1.0f;
    public float closeObjectDistance = 1.0f;
    public float farObjectDistance = 3.0f;
    public float closeObjectPullRangeMultiplier = 3.0f;
    public float farObjectPullRangeMultiplier = 1.0f;
    public float closePullDeadzone = 0.01f;
    public float nearOneToOneObjectDistance = 1.2f;
    public float closeRayPullRangeMultiplier = 1.35f;
    public float closeRayPullExponent = 1.35f;
    public float pullStartCorrectionMaxSpeed = 0.5f;

    [Header("Near Grab Fallback")]
    public float nearGrabRadius = 0.15f;
    public LayerMask nearGrabLayers;

    [Header("Debug")]
    public bool showAttachPointGizmo = false;
    public float attachPointGizmoRadius = 0.015f;

    private bool isAttached = false;
    private bool isRemotePulling = false;
    private bool isDirectNearAttach = false;
    private bool attachedFromNearGrab = false;
    private GameObject currentlyGrabbedObject;

    private Vector3 grabOffset;
    private Quaternion attachRotationOffset = Quaternion.identity;
    private Vector3 attachObjectHoldPos;
    private float attachRealHandDistance;
    private bool pullMappingActive;
    private float pullStartRealHandDistance;
    private float pullStartObjectDistance;
    private float pullStartControllerObjectDistance;
    private Vector3 pullStartVirtualHandPos;
    private Vector3 pullStartAttachPointWorld;
    private Vector3 pullStartObjectPosition;
    private Vector3 pullStartAttachDirectionFromHmd = Vector3.forward;
    private float pullStartAttachDistanceFromHmd;
    private Vector3 pullStartOffset;
    private Vector3 pullStartAttachOffset;
    private Vector3 pullEntryError;
    private Vector3 pullCurrentError;
    private Vector3 attachControllerOffset;
    private Vector3 pullStartControllerAttachOffset;
    private Vector3 directAttachLocalObjectOffset;
    private Quaternion directAttachLocalObjectRotation = Quaternion.identity;
    private Vector3 pullClampDirection = Vector3.forward;
    private float pullActivatedTime;
    private bool pullActivationQueued;
    private bool pullLockFirstPullFrame;
    private bool pullFirstPullFrameJustReleased;
    private Vector3 attachControllerStartPos;

    private Rigidbody attachedRigidbody;
    private bool attachedOriginalIsKinematic;
    private bool attachedOriginalUseGravity;
    private RigidbodyConstraints attachedOriginalConstraints;
    private RigidbodyInterpolation attachedOriginalInterpolation;
    private CollisionDetectionMode attachedOriginalCollisionDetectionMode;

    private Renderer[] controllerRenderers;
    private Renderer[] virtualHandRenderers;

    private bool wasTriggerPressed;

    public bool IsAttached() { return isAttached; }
    public bool IsRemotePulling() { return isRemotePulling; }
    public GameObject GetCurrentObject() { return currentlyGrabbedObject; }
    public RaycastObjectSelector GetSelector() { return selector; }

    void Start()
    {
        if (virtualHand == null)
            Debug.LogError("VirtualHandAttach: virtualHand is not assigned!");

        if (controllerTransform == null)
            Debug.LogError("VirtualHandAttach: controllerTransform is not assigned!");

        if (selector == null)
            Debug.LogError("VirtualHandAttach: selector is not assigned!");

        if (triggerAction.action != null)
            triggerAction.action.Enable();

        wasTriggerPressed = IsTriggerPressed();

        if (controllerTransform != null)
            controllerRenderers = controllerTransform.GetComponentsInChildren<Renderer>(true);

        if (virtualHand != null)
            virtualHandRenderers = virtualHand.GetComponentsInChildren<Renderer>(true);

        SetVirtualHandVisible(false);
    }

    void Update()
    {
        if (virtualHand == null || controllerTransform == null || Camera.main == null)
            return;

        bool isTriggerPressed = IsTriggerPressed();
        bool pressedThisFrame = !wasTriggerPressed && isTriggerPressed;
        bool releasedThisFrame = wasTriggerPressed && !isTriggerPressed;

        if (!isAttached && pressedThisFrame)
        {
            GameObject selected = selector != null ? selector.GetCurrentTarget() : null;
            bool usedNearGrabFallback = false;
            if (selected == null)
            {
                selected = FindNearbyGrabCandidate();
                usedNearGrabFallback = selected != null;
            }

            if (selected != null)
                StartAttach(selected, usedNearGrabFallback);
        }

        if (isAttached && currentlyGrabbedObject != null)
            ApplyStableGoGoMovement();

        if (isAttached && releasedThisFrame)
            ReleaseHand();

        wasTriggerPressed = isTriggerPressed;
    }

    private bool IsTriggerPressed()
    {
        InputAction action = triggerAction.action;
        if (action == null)
            return false;

        return action.IsPressed();
    }

    private GameObject FindNearbyGrabCandidate()
    {
        if (controllerTransform == null)
            return null;

        float radius = Mathf.Max(0.01f, nearGrabRadius);
        int mask = nearGrabLayers.value != 0
            ? nearGrabLayers.value
            : (selector != null ? selector.selectableLayers.value : ~0);

        Collider[] hits = Physics.OverlapSphere(controllerTransform.position, radius, mask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return null;

        Transform origin = controllerTransform;
        float bestSqrDistance = float.PositiveInfinity;
        GameObject best = null;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i];
            if (c == null)
                continue;

            Rigidbody rb = c.attachedRigidbody;
            GameObject candidate = rb != null ? rb.gameObject : c.gameObject;
            if (candidate == null)
                continue;

            Vector3 nearest = c.ClosestPoint(origin.position);
            float sqrDist = (nearest - origin.position).sqrMagnitude;
            if (sqrDist < bestSqrDistance)
            {
                bestSqrDistance = sqrDist;
                best = candidate;
            }
        }

        return best;
    }

    private void StartAttach(GameObject objectToGrab, bool fromNearGrab)
    {
        GameObject grabRoot = objectToGrab;
        Rigidbody rootRb = objectToGrab != null ? objectToGrab.GetComponentInParent<Rigidbody>() : null;
        if (rootRb != null)
            grabRoot = rootRb.gameObject;

        currentlyGrabbedObject = grabRoot;
        isAttached = true;

        if (selector != null)
            selector.SetGrabbedState(true);

        if (hideControllerWhileAttached)
            SetControllerVisible(false);

        SetVirtualHandVisible(true);

        Vector3 virtualHandPos = CalculateVirtualHandPosition();
        Vector3 attachPoint = GetAttachPoint(grabRoot, virtualHandPos);
        float attachGap = Vector3.Distance(attachPoint, virtualHandPos);
        // Keep a visual surface anchor so the virtual hand stays on the touched side.
        grabOffset = Quaternion.Inverse(grabRoot.transform.rotation) * (attachPoint - grabRoot.transform.position);
        attachRotationOffset = Quaternion.Inverse(controllerTransform.rotation) * grabRoot.transform.rotation;
        attachObjectHoldPos = grabRoot.transform.position;
        attachRealHandDistance = Vector3.Distance(Camera.main.transform.position, controllerTransform.position);
        // Never start in pull mode on attach. Even close re-grabs must wait for actual retract,
        // otherwise the object can snap into an unstable pull branch near the HMD.
        pullMappingActive = false;
        isRemotePulling = false;
        pullStartRealHandDistance = attachRealHandDistance;
        pullStartObjectDistance = Vector3.Distance(Camera.main.transform.position, attachPoint);
        pullStartControllerObjectDistance = Vector3.Distance(controllerTransform.position, attachPoint);
        pullStartVirtualHandPos = virtualHandPos;
        pullStartAttachPointWorld = attachPoint;
        pullStartObjectPosition = grabRoot.transform.position;
        Vector3 fromHmd = attachPoint - Camera.main.transform.position;
        pullStartAttachDistanceFromHmd = fromHmd.magnitude;
        if (fromHmd.sqrMagnitude > 0.000001f)
            pullStartAttachDirectionFromHmd = fromHmd.normalized;
        else
            pullStartAttachDirectionFromHmd = Camera.main.transform.forward;
        pullStartOffset = grabRoot.transform.position - virtualHandPos;
        pullStartAttachOffset = attachPoint - virtualHandPos;
        attachControllerOffset = attachPoint - controllerTransform.position;
        pullStartControllerAttachOffset = attachControllerOffset;
        attachedFromNearGrab = fromNearGrab;
        isDirectNearAttach = attachedFromNearGrab && pullStartControllerObjectDistance <= Mathf.Max(0.05f, nearOneToOneObjectDistance);
        directAttachLocalObjectOffset = Quaternion.Inverse(controllerTransform.rotation) * (grabRoot.transform.position - controllerTransform.position);
        directAttachLocalObjectRotation = Quaternion.Inverse(controllerTransform.rotation) * grabRoot.transform.rotation;
        if (Camera.main != null)
        {
            Vector3 initialClampDir = attachPoint - Camera.main.transform.position;
            if (initialClampDir.sqrMagnitude > 0.000001f)
                pullClampDirection = initialClampDir.normalized;
            else
                pullClampDirection = Camera.main.transform.forward;
        }
        pullActivatedTime = -1f;
        pullActivationQueued = false;
        pullLockFirstPullFrame = false;
        attachControllerStartPos = controllerTransform.position;

        attachedRigidbody = grabRoot.GetComponent<Rigidbody>();
        if (attachedRigidbody != null)
        {
            attachedOriginalIsKinematic = attachedRigidbody.isKinematic;
            attachedOriginalUseGravity = attachedRigidbody.useGravity;
            attachedOriginalConstraints = attachedRigidbody.constraints;
            attachedOriginalInterpolation = attachedRigidbody.interpolation;
            attachedOriginalCollisionDetectionMode = attachedRigidbody.collisionDetectionMode;

            attachedRigidbody.isKinematic = true;
            attachedRigidbody.useGravity = false;
            attachedRigidbody.constraints = attachedOriginalConstraints & ~RigidbodyConstraints.FreezeRotation;
            attachedRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            attachedRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            attachedRigidbody.linearVelocity = Vector3.zero;
            attachedRigidbody.angularVelocity = Vector3.zero;
        }

        virtualHand.position = attachPoint;
        virtualHand.rotation = controllerTransform.rotation;

        Debug.Log("ReverseGoGo attached: " + grabRoot.name);
    }

    private void ApplyStableGoGoMovement()
    {
        Vector3 virtualHandPos = CalculateVirtualHandPosition();
        Quaternion targetRotation = controllerTransform.rotation * attachRotationOffset;
        bool activatePullThisFrame = false;

        if (isDirectNearAttach)
        {
            currentlyGrabbedObject.transform.position = controllerTransform.position + controllerTransform.rotation * directAttachLocalObjectOffset;
            currentlyGrabbedObject.transform.rotation = controllerTransform.rotation * directAttachLocalObjectRotation;
            virtualHand.position = GetCurrentAttachPoint();
            virtualHand.rotation = controllerTransform.rotation;
            return;
        }

        if (!pullMappingActive)
        {
            float currentRealHandDistance = Vector3.Distance(Camera.main.transform.position, controllerTransform.position);
            float retractAmount = attachRealHandDistance - currentRealHandDistance;
            if (retractAmount >= Mathf.Max(0f, attachMoveDeadzone))
            {
                activatePullThisFrame = true;
                pullStartRealHandDistance = currentRealHandDistance;
                Vector3 startAttachPoint = GetCurrentAttachPoint();
                pullStartObjectDistance = Vector3.Distance(Camera.main.transform.position, startAttachPoint);
                pullStartControllerObjectDistance = Vector3.Distance(controllerTransform.position, startAttachPoint);
                pullStartVirtualHandPos = virtualHandPos;
                pullStartAttachPointWorld = startAttachPoint;
                pullStartObjectPosition = currentlyGrabbedObject.transform.position;
                Vector3 fromHmd = startAttachPoint - Camera.main.transform.position;
                pullStartAttachDistanceFromHmd = fromHmd.magnitude;
                if (fromHmd.sqrMagnitude > 0.000001f)
                    pullStartAttachDirectionFromHmd = fromHmd.normalized;
                else
                    pullStartAttachDirectionFromHmd = Camera.main.transform.forward;
                pullStartOffset = currentlyGrabbedObject.transform.position - virtualHandPos;
                pullStartAttachOffset = startAttachPoint - virtualHandPos;
                pullStartControllerAttachOffset = startAttachPoint - controllerTransform.position;
                // Error between where object IS and where the pull formula WOULD place it.
                // Fading this out lets arm motion respond immediately with no startup jump.
                pullEntryError = currentlyGrabbedObject.transform.position - (virtualHandPos - targetRotation * grabOffset);
                if (Camera.main != null)
                {
                    Vector3 startClampDir = startAttachPoint - Camera.main.transform.position;
                    if (startClampDir.sqrMagnitude > 0.000001f)
                        pullClampDirection = startClampDir.normalized;
                    else
                        pullClampDirection = Camera.main.transform.forward;
                }
            }
        }

        Vector3 targetPos = attachObjectHoldPos;
        if (!pullMappingActive)
        {
            // Check if object is close to controller to avoid threshold jitter.
            float currentObjectControllerDist = Vector3.Distance(controllerTransform.position, currentlyGrabbedObject.transform.position);
            
            if (currentObjectControllerDist < closeObjectDistance)
            {
                // Close object: Lock to controller to avoid GoGo threshold jitter.
                // This keeps position stable during threshold transitions.
                targetPos = controllerTransform.position + pullStartControllerAttachOffset;
            }
            else
            {
                // Far object: Use sphere follow offset.
                // This avoids initial snap when attaching far objects.
                targetPos = virtualHandPos + pullStartOffset;
            }
        }

        if (activatePullThisFrame)
        {
            // Activate pull and preserve continuity with startup error fade.
            pullStartObjectPosition = targetPos;
            Vector3 mappedTargetAtActivation = virtualHandPos - targetRotation * grabOffset;
            pullEntryError = pullStartObjectPosition - mappedTargetAtActivation;
            pullCurrentError = pullEntryError;

            pullMappingActive = true;
            isRemotePulling = true;
            pullActivatedTime = Time.time;
            pullActivationQueued = false;
            pullLockFirstPullFrame = false;
            pullFirstPullFrameJustReleased = false;
        }

        if (pullMappingActive)
        {
            float activationBlend = 1f;
            if (pullActivatedTime >= 0f)
            {
                float transitionTime = Mathf.Max(
                    Mathf.Max(0.0001f, pullActivationRampTime),
                    Mathf.Max(0.0001f, pullStartTransitionBlendTime));
                float rawBlend = Mathf.Clamp01((Time.time - pullActivatedTime) / transitionTime);
                activationBlend = rawBlend * rawBlend * (3f - 2f * rawBlend);
            }

            Vector3 desiredErrorOffset = Vector3.Lerp(pullEntryError, Vector3.zero, activationBlend);
            float maxCorrectionStep = Mathf.Max(0.01f, pullStartCorrectionMaxSpeed) * Time.deltaTime;
            pullCurrentError = Vector3.MoveTowards(pullCurrentError, desiredErrorOffset, maxCorrectionStep);
            targetPos = (virtualHandPos - targetRotation * grabOffset) + pullCurrentError;
        }

        currentlyGrabbedObject.transform.position = targetPos;
        currentlyGrabbedObject.transform.rotation = targetRotation;

        virtualHand.position = GetCurrentAttachPoint();
        virtualHand.rotation = controllerTransform.rotation;
    }

    private Vector3 GetCurrentAttachPoint()
    {
        if (currentlyGrabbedObject == null)
            return CalculateVirtualHandPosition();

        return currentlyGrabbedObject.transform.position + currentlyGrabbedObject.transform.rotation * grabOffset;
    }

    private Vector3 GetAttachPoint(GameObject targetObject, Vector3 fallbackPoint)
    {
        Collider[] colliders = targetObject.GetComponentsInChildren<Collider>();

        // Prefer the actual controller ray hit on the selected object so the hand anchors
        // on the user-facing/front contact point rather than an arbitrary nearest corner.
        if (selector != null && selector.rayOrigin != null)
        {
            Ray selectionRay = new Ray(selector.rayOrigin.position, selector.rayOrigin.forward);
            float bestRayDistance = float.PositiveInfinity;
            Vector3 bestRayPoint = fallbackPoint;
            bool hasRayHit = false;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null || !c.enabled)
                    continue;

                if (c.Raycast(selectionRay, out RaycastHit hit, Mathf.Infinity) && hit.distance < bestRayDistance)
                {
                    bestRayDistance = hit.distance;
                    bestRayPoint = hit.point;
                    hasRayHit = true;
                }
            }

            if (hasRayHit)
                return bestRayPoint;
        }

        // For near grabs (no ray hit), anchor to the hand/controller vicinity.
        // Using HMD here can pick a point too close to the head and cause pull snap.
        Vector3 referencePoint = controllerTransform != null ? controllerTransform.position : fallbackPoint;
        Vector3 bestPoint = fallbackPoint;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider c = colliders[i];
            if (c == null || !c.enabled)
                continue;

            Vector3 candidate = c.ClosestPoint(referencePoint);
            float distance = (candidate - referencePoint).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestPoint = candidate;
            }
        }

        return bestPoint;
    }

    private Vector3 CalculateVirtualHandPosition()
    {
        Vector3 hmdPos = Camera.main.transform.position;
        Vector3 handPos = controllerTransform.position;
        float safeThreshold = Mathf.Max(0.001f, thresholdDistance);

        float realDistance = Vector3.Distance(hmdPos, handPos);
        if (realDistance < 0.05f)
            return hmdPos + Camera.main.transform.forward * Mathf.Max(0.05f, safeThreshold);

        Vector3 direction = (handPos - hmdPos).normalized;

        float virtualDistance;
        if (realDistance <= safeThreshold)
        {
            virtualDistance = realDistance;
        }
        else
        {
            float beyondThreshold = realDistance - safeThreshold;
            virtualDistance = realDistance + Mathf.Max(0f, scalingFactor) * beyondThreshold * beyondThreshold;
            virtualDistance = Mathf.Min(virtualDistance, Mathf.Max(safeThreshold, maxExtensionDistance));
        }

        return hmdPos + direction * virtualDistance;
    }

    private void ReleaseHand()
    {
        if (selector != null)
            selector.SetGrabbedState(false);

        if (currentlyGrabbedObject != null && attachedRigidbody != null)
        {
            attachedRigidbody.isKinematic = attachedOriginalIsKinematic;
            attachedRigidbody.useGravity = attachedOriginalUseGravity;
            attachedRigidbody.constraints = attachedOriginalConstraints;
            attachedRigidbody.interpolation = attachedOriginalInterpolation;
            attachedRigidbody.collisionDetectionMode = attachedOriginalCollisionDetectionMode;
            attachedRigidbody.linearVelocity = Vector3.zero;
            attachedRigidbody.angularVelocity = Vector3.zero;
        }

        currentlyGrabbedObject = null;
        attachedRigidbody = null;
        isAttached = false;
        isRemotePulling = false;
        isDirectNearAttach = false;
        attachedFromNearGrab = false;
        grabOffset = Vector3.zero;
        attachObjectHoldPos = Vector3.zero;
        attachRealHandDistance = 0f;
        pullMappingActive = false;
        pullStartRealHandDistance = 0f;
        pullStartObjectDistance = 0f;
        pullStartControllerObjectDistance = 0f;
        pullStartVirtualHandPos = Vector3.zero;
        pullStartAttachPointWorld = Vector3.zero;
        pullStartObjectPosition = Vector3.zero;
        pullStartAttachDirectionFromHmd = Vector3.forward;
        pullStartAttachDistanceFromHmd = 0f;
        pullStartOffset = Vector3.zero;
        pullStartAttachOffset = Vector3.zero;
        pullEntryError = Vector3.zero;
        pullCurrentError = Vector3.zero;
        attachControllerOffset = Vector3.zero;
        pullStartControllerAttachOffset = Vector3.zero;
        directAttachLocalObjectOffset = Vector3.zero;
        directAttachLocalObjectRotation = Quaternion.identity;
        pullClampDirection = Vector3.forward;
        pullActivatedTime = -1f;
        pullActivationQueued = false;
        pullFirstPullFrameJustReleased = false;
        pullLockFirstPullFrame = false;
        attachControllerStartPos = Vector3.zero;
        attachRotationOffset = Quaternion.identity;

        SetControllerVisible(true);
        SetVirtualHandVisible(false);
    }

    private Vector3 ClampAttachPointAwayFromHmd(Vector3 targetAttachPoint, Vector3 virtualHandPos)
    {
        if (Camera.main == null)
            return targetAttachPoint;

        Vector3 hmdPos = Camera.main.transform.position;
        float minRadius = Mathf.Max(0.03f, hmdSafetyRadius);
        Vector3 toAttach = targetAttachPoint - hmdPos;

        if (toAttach.sqrMagnitude >= minRadius * minRadius)
            return targetAttachPoint;

        Vector3 safeDir = toAttach;
        if (safeDir.sqrMagnitude < 0.000001f)
            safeDir = pullClampDirection;
        if (safeDir.sqrMagnitude < 0.000001f)
            safeDir = virtualHandPos - hmdPos;
        if (safeDir.sqrMagnitude < 0.000001f)
            safeDir = Camera.main.transform.forward;

        pullClampDirection = safeDir.normalized;

        return hmdPos + pullClampDirection * minRadius;
    }

    private void SetVirtualHandVisible(bool visible)
    {
        if (virtualHandRenderers == null || virtualHandRenderers.Length == 0)
            return;

        for (int i = 0; i < virtualHandRenderers.Length; i++)
        {
            Renderer r = virtualHandRenderers[i];
            if (r != null)
                r.enabled = visible;
        }
    }

    private void SetControllerVisible(bool visible)
    {
        if (controllerRenderers == null || controllerRenderers.Length == 0)
            return;

        for (int i = 0; i < controllerRenderers.Length; i++)
        {
            Renderer r = controllerRenderers[i];
            if (r == null)
                continue;

            if (virtualHand != null && r.transform.IsChildOf(virtualHand))
                continue;

            r.enabled = visible;
        }
    }

    void OnDestroy()
    {
        if (isAttached)
            ReleaseHand();
    }

    void OnDrawGizmosSelected()
    {
        if (!showAttachPointGizmo || !isAttached || currentlyGrabbedObject == null)
            return;

        Gizmos.color = Color.yellow;
        Vector3 attachPoint = currentlyGrabbedObject.transform.position + currentlyGrabbedObject.transform.rotation * grabOffset;
        Gizmos.DrawWireSphere(attachPoint, Mathf.Max(0.001f, attachPointGizmoRadius));
    }
}
