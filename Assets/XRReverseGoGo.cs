using UnityEngine;

public class XRReverseGoGo : MonoBehaviour
{
    public Transform rightHand;
    public Transform targetObject;
    public float thresholdZ = 0.5f;
    public float pullStrength = 2.0f;

    void Update()
    {
        if (rightHand == null || targetObject == null) return;

        float zDistance = rightHand.localPosition.z;

        if (zDistance > thresholdZ)
        {
            float overshoot = zDistance - thresholdZ;
            Vector3 pullDirection = (rightHand.position - targetObject.position).normalized;
            targetObject.position += pullDirection * overshoot * pullStrength * Time.deltaTime;
        }
    }
}
