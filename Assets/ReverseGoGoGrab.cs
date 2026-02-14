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

        Vector3 handOffset = rightHand.position - Camera.main.transform.position;

        // X/Y stay 1:1
        Vector3 lateral = new Vector3(handOffset.x, handOffset.y, 0f);

        // Z is scaled
        float scale = initialDistance / Mathf.Max(currentHandDistance, minScaleDistance);
        float scaledZ = handOffset.z * scale;

        Vector3 newPosition = Camera.main.transform.position + lateral + Camera.main.transform.forward * scaledZ;

        targetObject.position = newPosition;
    }

    void EndGrab()
    {
        isGrabbing = false;
    }
}
