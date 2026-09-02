using UnityEngine;
using UnityEngine.InputSystem;

public class ReverseGoGoGrab : MonoBehaviour
{
    [Header("References")]
    public Transform rightHand;
    public Transform targetObject;

    [Header("Input")]
    public InputActionProperty grabAction; // Assign XRI RightHand / Activate

    [Header("Distance Scaling")]
    public float minGrabDistance = 0.3f;     // Auto-release threshold
    public float minScaleDistance = 0.5f;    // Prevents scaling blow-up

    [Header("Traditional GoGo Exponential Mapping")]
    public float thresholdDistance = 0.3f;
    public float scalingFactor = 20.0f;
    public float maxExtensionDistance = 10.0f;

    private float initialDistance;
    private bool isGrabbing = false;

    void Update()
    {
        if (rightHand == null || targetObject == null)
            return;

        float handDistance = Vector3.Distance(Camera.main.transform.position, rightHand.position);

        // Start grab
        if (!isGrabbing && grabAction.action.WasPressedThisFrame())
        {
            StartGrab(handDistance);
        }

        // Update while grabbing
        if (isGrabbing)
        {
            HandleObjectMovement(handDistance);

            // Release conditions
            if (grabAction.action.WasReleasedThisFrame() || handDistance < minGrabDistance)
            {
                EndGrab();
            }
        }
    }

    void StartGrab(float handDistance)
    {
        initialDistance = Vector3.Distance(Camera.main.transform.position, targetObject.position);
        isGrabbing = true;
    }

    void HandleObjectMovement(float currentHandDistance)
    {
        if (targetObject == null) return;

        Vector3 hmdPos = Camera.main.transform.position;
        Vector3 handOffset = rightHand.position - hmdPos;

        // X/Y stay 1:1
        Vector3 lateral = new Vector3(handOffset.x, handOffset.y, 0f);

        // Apply Traditional GoGo exponential mapping to depth
        float mappedDistance = CalculateTraditionalGoGoMappedDistance(currentHandDistance);
        
        // Scale the forward movement by the mapping
        float zDirection = handOffset.z >= 0 ? 1f : -1f;
        float absHandZ = Mathf.Abs(handOffset.z);
        float scaledZ = zDirection * mappedDistance;

        Vector3 newPosition = hmdPos + lateral + Camera.main.transform.forward * scaledZ;

        targetObject.position = newPosition;
    }

    private float CalculateTraditionalGoGoMappedDistance(float handDistance)
    {
        // Traditional GoGo mapping:
        // - Inside threshold: 1:1 (linear)
        // - Beyond threshold: quadratic amplification
        float safeThreshold = Mathf.Max(0.001f, thresholdDistance);
        float virtualDistance;

        if (handDistance <= safeThreshold)
        {
            virtualDistance = handDistance;
        }
        else
        {
            float beyondThreshold = handDistance - safeThreshold;
            // Classic GoGo-style amplified extension: D_virtual = D_real + k * (D_real - threshold)^2
            virtualDistance = handDistance + Mathf.Max(0f, scalingFactor) * beyondThreshold * beyondThreshold;
            virtualDistance = Mathf.Min(virtualDistance, Mathf.Max(safeThreshold, maxExtensionDistance));
        }

        return virtualDistance;
    }

    void EndGrab()
    {
        isGrabbing = false;
    }
}
