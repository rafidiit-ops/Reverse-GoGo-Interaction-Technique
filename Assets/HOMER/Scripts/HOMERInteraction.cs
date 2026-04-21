using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// HOMER interaction: ray-based remote selection + seamless hand-centric manipulation.
/// This script is isolated from Reverse Go-Go so you can iterate safely.
/// </summary>
public class HOMERInteraction : MonoBehaviour
{
    [Header("References")]
    public Transform controllerTransform;
    public Transform virtualHand;
    public Transform hmdTransform;
    public InputActionProperty triggerAction;
    public InputActionProperty clutchAction;
    public RaycastObjectSelector selector;

    [Header("Setup")]
    public bool autoConfigureReferences = true;
    public bool disableConflictingTechniques = true;

    [Header("Behavior")]
    public bool keepObjectPhysicsDisabledWhileGrabbed = true;
    public float transitionDuration = 0.15f;
    public float minControllerDistanceForScaling = 0.08f;
    public float maxDistanceScale = 12f;
    public bool holdToClutch = true;

    private GameObject currentlyGrabbedObject;
    private bool isAttached;

    private Vector3 grabPositionOffset;
    private Quaternion grabRotationOffset;

    private Vector3 controllerGrabPosition;
    private Quaternion controllerGrabRotation;
    private Vector3 objectGrabPosition;
    private Quaternion objectGrabRotation;
    private float distanceScale = 1f;

    // 1:1 incremental tracking
    private Vector3 _prevControllerPos;
    private Quaternion _prevControllerRot;

    private bool isTransitioning;
    private float transitionElapsed;
    private Vector3 transitionStartHandPosition;
    private Quaternion transitionStartHandRotation;
    private bool isClutching;

    private Rigidbody grabbedRigidbody;
    private bool previousIsKinematic;
    private bool previousUseGravity;

    public bool IsAttached() { return isAttached; }
    public bool IsClutching() { return isClutching; }
    public GameObject GetCurrentObject() { return currentlyGrabbedObject; }
    public RaycastObjectSelector GetSelector() { return selector; }

    private static bool IsActionAssigned(InputActionProperty actionProperty)
    {
        return actionProperty.action != null;
    }

    private void AutoConfigureFromExistingScene()
    {
        if (!autoConfigureReferences)
        {
            return;
        }

        VirtualHandAttach existingReverse = FindFirstObjectByType<VirtualHandAttach>(FindObjectsInactive.Include);
        if (existingReverse != null)
        {
            if (controllerTransform == null)
            {
                controllerTransform = existingReverse.controllerTransform;
            }

            if (virtualHand == null)
            {
                virtualHand = existingReverse.virtualHand;
            }

            if (selector == null)
            {
                selector = existingReverse.selector;
            }

            if (!IsActionAssigned(triggerAction) && IsActionAssigned(existingReverse.triggerAction))
            {
                triggerAction = existingReverse.triggerAction;
            }

            if (!IsActionAssigned(clutchAction) && IsActionAssigned(existingReverse.gripAction))
            {
                clutchAction = existingReverse.gripAction;
            }
        }

        if (hmdTransform == null && Camera.main != null)
        {
            hmdTransform = Camera.main.transform;
        }

        if (selector == null)
        {
            selector = FindFirstObjectByType<RaycastObjectSelector>(FindObjectsInactive.Include);
        }

        if (controllerTransform == null && selector != null)
        {
            controllerTransform = selector.rayOrigin;
        }
    }

    private void DisableConflictingTechniqueScripts()
    {
        if (!disableConflictingTechniques)
        {
            return;
        }

        VirtualHandAttach[] reverseControllers = FindObjectsByType<VirtualHandAttach>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < reverseControllers.Length; i++)
        {
            if (reverseControllers[i] != null)
            {
                reverseControllers[i].enabled = false;
            }
        }

        XRReverseGoGo[] xrReverseControllers = FindObjectsByType<XRReverseGoGo>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < xrReverseControllers.Length; i++)
        {
            if (xrReverseControllers[i] != null)
            {
                xrReverseControllers[i].enabled = false;
            }
        }

        ReverseGoGoGrab[] reverseGrabbers = FindObjectsByType<ReverseGoGoGrab>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < reverseGrabbers.Length; i++)
        {
            if (reverseGrabbers[i] != null)
            {
                reverseGrabbers[i].enabled = false;
            }
        }

        TraditionalGoGoInteraction[] traditionalControllers = FindObjectsByType<TraditionalGoGoInteraction>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < traditionalControllers.Length; i++)
        {
            if (traditionalControllers[i] != null)
            {
                traditionalControllers[i].enabled = false;
            }
        }
    }

    private void Start()
    {
        AutoConfigureFromExistingScene();
        DisableConflictingTechniqueScripts();

        if (hmdTransform == null && Camera.main != null)
        {
            hmdTransform = Camera.main.transform;
        }

        if (triggerAction.action != null)
        {
            triggerAction.action.Enable();
        }

        if (clutchAction.action != null)
        {
            clutchAction.action.Enable();
        }
    }

    private void Update()
    {
        if (controllerTransform == null)
        {
            return;
        }

        if (!isAttached && triggerAction.action != null && triggerAction.action.WasPressedThisFrame())
        {
            GameObject target = selector != null ? selector.GetCurrentTarget() : null;
            if (target != null)
            {
                StartGrab(target);
            }
        }

        if (isAttached && triggerAction.action != null && triggerAction.action.WasReleasedThisFrame())
        {
            EndGrab();
        }

        if (isAttached && holdToClutch && clutchAction.action != null)
        {
            if (!isClutching && clutchAction.action.WasPressedThisFrame())
            {
                BeginClutch();
            }
            else if (isClutching && clutchAction.action.WasReleasedThisFrame())
            {
                EndClutch();
            }
        }

        if (isAttached && currentlyGrabbedObject != null)
        {
            if (!isClutching)
            {
                ApplyMovement(Time.deltaTime);
            }
            else if (virtualHand != null)
            {
                // During clutch, keep object frozen and let hand recentre with the real controller.
                virtualHand.position = controllerTransform.position;
                virtualHand.rotation = controllerTransform.rotation;
            }
        }
    }

    private void BeginClutch()
    {
        isClutching = true;
    }

    private void EndClutch()
    {
        if (!isAttached || currentlyGrabbedObject == null)
        {
            isClutching = false;
            return;
        }

        // Reset incremental tracking anchor so movement resumes cleanly after clutch.
        _prevControllerPos = controllerTransform.position;
        _prevControllerRot = controllerTransform.rotation;

        isTransitioning = false;
        isClutching = false;
    }

    private void StartGrab(GameObject target)
    {
        currentlyGrabbedObject = target;
        isAttached = true;

        if (selector != null)
        {
            selector.SetGrabbedState(true);
        }

        controllerGrabPosition = controllerTransform.position;
        controllerGrabRotation = controllerTransform.rotation;

        objectGrabPosition = currentlyGrabbedObject.transform.position;
        objectGrabRotation = currentlyGrabbedObject.transform.rotation;

        // 1:1 mapping — physical hand movement equals object movement.
        distanceScale = 1f;
        _prevControllerPos = controllerTransform.position;
        _prevControllerRot = controllerTransform.rotation;

        // At grab begin, mapped hand equals object position. Keep offset for robustness.
        grabPositionOffset = currentlyGrabbedObject.transform.position - objectGrabPosition;
        grabRotationOffset = Quaternion.Inverse(objectGrabRotation) * currentlyGrabbedObject.transform.rotation;

        transitionElapsed = 0f;
        isTransitioning = transitionDuration > 0.0001f;

        if (virtualHand != null)
        {
            transitionStartHandPosition = virtualHand.position;
            transitionStartHandRotation = virtualHand.rotation;
            virtualHand.gameObject.SetActive(true);
        }
        else
        {
            transitionStartHandPosition = objectGrabPosition;
            transitionStartHandRotation = objectGrabRotation;
        }

        grabbedRigidbody = currentlyGrabbedObject.GetComponent<Rigidbody>();
        if (grabbedRigidbody != null)
        {
            previousIsKinematic = grabbedRigidbody.isKinematic;
            previousUseGravity = grabbedRigidbody.useGravity;

            if (keepObjectPhysicsDisabledWhileGrabbed)
            {
                grabbedRigidbody.isKinematic = true;
                grabbedRigidbody.useGravity = false;
                grabbedRigidbody.linearVelocity = Vector3.zero;
                grabbedRigidbody.angularVelocity = Vector3.zero;
            }
        }

        Debug.Log($"[HOMER] Grabbed '{target.name}' | scale={distanceScale:F2}");
    }

    private void ApplyMovement(float deltaTime)
    {
        // 1:1 incremental mapping: object moves by exactly the same world-space
        // delta as the physical controller moved this frame.
        Vector3 frameDelta = controllerTransform.position - _prevControllerPos;
        Vector3 targetPosition = currentlyGrabbedObject.transform.position + frameDelta;
        _prevControllerPos = controllerTransform.position;

        Quaternion frameRotDelta = controllerTransform.rotation * Quaternion.Inverse(_prevControllerRot);
        Quaternion targetRotation = frameRotDelta * currentlyGrabbedObject.transform.rotation;
        _prevControllerRot = controllerTransform.rotation;

        currentlyGrabbedObject.transform.position = targetPosition;
        currentlyGrabbedObject.transform.rotation = targetRotation;

        if (virtualHand != null)
        {
            virtualHand.position = targetPosition;
            virtualHand.rotation = targetRotation;
        }
    }

    private void EndGrab()
    {
        if (grabbedRigidbody != null)
        {
            grabbedRigidbody.isKinematic = previousIsKinematic;
            grabbedRigidbody.useGravity = previousUseGravity;
            grabbedRigidbody = null;
        }

        if (selector != null)
        {
            selector.SetGrabbedState(false);
        }

        currentlyGrabbedObject = null;
        isAttached = false;
        isTransitioning = false;
        isClutching = false;
    }

    private void OnDestroy()
    {
        if (isAttached)
        {
            EndGrab();
        }
    }
}
