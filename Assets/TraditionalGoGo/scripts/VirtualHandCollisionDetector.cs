using UnityEngine;

/// <summary>
/// Helper component for Traditional GoGo - detects collisions with virtual hand
/// Attach this to the Virtual Hand GameObject
/// </summary>
public class VirtualHandCollisionDetector : MonoBehaviour
{
    public TraditionalGoGoInteraction gogoController;
    
    void OnTriggerEnter(Collider other)
    {
        if (gogoController != null)
        {
            gogoController.OnVirtualHandTouchObject(other.gameObject);
        }
    }
    
    void OnTriggerExit(Collider other)
    {
        if (gogoController != null)
        {
            gogoController.OnVirtualHandLeaveObject(other.gameObject);
        }
    }
}
