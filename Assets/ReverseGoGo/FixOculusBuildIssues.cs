using UnityEngine;

public class FixOculusBuildIssues : MonoBehaviour
{
    void Start()
    {
        Debug.Log("========== FIXING OCULUS BUILD ISSUES ==========");
        
        // Fix 1: Ensure Main Camera renders everything
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            if (mainCamera.cullingMask != -1)
            {
                Debug.LogWarning($"Camera culling mask was {mainCamera.cullingMask}, setting to Everything (-1)");
                mainCamera.cullingMask = -1;
                Debug.Log("✅ FIXED: Camera now renders all layers");
            }
            else
            {
                Debug.Log("✓ Camera culling mask is correct (Everything)");
            }
        }
        
        // Fix 2: Move all controller objects to Default layer (0)
        GameObject xrOriginObj = GameObject.Find("XR Origin (VR)");
        if (xrOriginObj == null) xrOriginObj = GameObject.Find("XR Origin");
        
        if (xrOriginObj != null)
        {
            Transform cameraOffset = xrOriginObj.transform.Find("Camera Offset");
            if (cameraOffset != null)
            {
                // Fix Right Hand
                Transform rightHand = cameraOffset.Find("Right Hand");
                if (rightHand != null)
                {
                    FixControllerLayer(rightHand, "Right Hand");
                }
                
                // Fix Left Hand
                Transform leftHand = cameraOffset.Find("Left Hand");
                if (leftHand != null)
                {
                    FixControllerLayer(leftHand, "Left Hand");
                }
            }
        }
        
        Debug.Log("========== FIX COMPLETE - TEST IN VR HEADSET ==========");
    }
    
    void FixControllerLayer(Transform controller, string name)
    {
        Debug.Log($"\n--- Fixing {name} ---");
        
        // Set controller parent to Default layer
        if (controller.gameObject.layer != 0)
        {
            Debug.Log($"Changed {name} from layer {controller.gameObject.layer} to 0 (Default)");
            controller.gameObject.layer = 0;
        }
        
        // Recursively set all children to Default layer
        Renderer[] renderers = controller.GetComponentsInChildren<Renderer>(true);
        int fixedCount = 0;
        
        foreach (Renderer renderer in renderers)
        {
            if (renderer.gameObject.layer != 0)
            {
                int oldLayer = renderer.gameObject.layer;
                renderer.gameObject.layer = 0;
                Debug.Log($"  ✅ {renderer.gameObject.name}: Layer {oldLayer} → 0 (Default)");
                fixedCount++;
            }
        }
        
        if (fixedCount > 0)
        {
            Debug.Log($"✅ Fixed {fixedCount} objects in {name}");
        }
        else
        {
            Debug.Log($"✓ All objects in {name} already on correct layer");
        }
    }
}
