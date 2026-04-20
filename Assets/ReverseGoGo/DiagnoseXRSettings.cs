using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class DiagnoseXRSettings : MonoBehaviour
{
    void Start()
    {
        Debug.Log("========== XR SETTINGS DIAGNOSTIC ==========");
        
        // Find Main Camera
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Debug.Log($"✅ Main Camera found: {mainCamera.name}");
            Debug.Log($"   Culling Mask (binary): {System.Convert.ToString(mainCamera.cullingMask, 2)}");
            Debug.Log($"   Culling Mask (int): {mainCamera.cullingMask}");
            Debug.Log($"   Is culling Everything? {mainCamera.cullingMask == -1}");
            
            // List which layers are being rendered
            for (int i = 0; i < 32; i++)
            {
                if ((mainCamera.cullingMask & (1 << i)) != 0)
                {
                    string layerName = LayerMask.LayerToName(i);
                    if (!string.IsNullOrEmpty(layerName))
                    {
                        Debug.Log($"   ✓ Rendering Layer {i}: {layerName}");
                    }
                }
            }
        }
        else
        {
            Debug.LogError("❌ Main Camera NOT FOUND!");
        }
        
        // Check XR Origin
        GameObject xrOriginObj = GameObject.Find("XR Origin (VR)");
        if (xrOriginObj == null) xrOriginObj = GameObject.Find("XR Origin");
        
        if (xrOriginObj != null)
        {
            Transform cameraOffset = xrOriginObj.transform.Find("Camera Offset");
            if (cameraOffset != null)
            {
                // Check Right Hand
                Transform rightHand = cameraOffset.Find("Right Hand");
                if (rightHand != null)
                {
                    CheckControllerLayers(rightHand, "Right Hand");
                }
                
                // Check Left Hand
                Transform leftHand = cameraOffset.Find("Left Hand");
                if (leftHand != null)
                {
                    CheckControllerLayers(leftHand, "Left Hand");
                }
            }
        }
        
        // Check XR Interaction Manager
        XRInteractionManager interactionManager = FindObjectOfType<XRInteractionManager>();
        if (interactionManager != null)
        {
            Debug.Log($"✅ XR Interaction Manager found and active");
        }
        else
        {
            Debug.LogError("❌ XR Interaction Manager NOT FOUND!");
        }
        
        Debug.Log("========== DIAGNOSTIC COMPLETE ==========");
    }
    
    void CheckControllerLayers(Transform controller, string name)
    {
        Debug.Log($"\n--- {name} Layers ---");
        Debug.Log($"Controller layer: {controller.gameObject.layer} ({LayerMask.LayerToName(controller.gameObject.layer)})");
        
        // Check all child renderers
        Renderer[] renderers = controller.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            int layer = renderer.gameObject.layer;
            string layerName = LayerMask.LayerToName(layer);
            Debug.Log($"  {renderer.gameObject.name}: Layer {layer} ({layerName})");
            
            // Check if camera can see this layer
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                bool canSee = (mainCamera.cullingMask & (1 << layer)) != 0;
                if (!canSee)
                {
                    Debug.LogError($"    ❌ CAMERA CANNOT SEE THIS LAYER!");
                    Debug.LogError($"       FIX: Change controller to Default layer (0)");
                }
                else
                {
                    Debug.Log($"    ✓ Camera can see this layer");
                }
            }
        }
    }
}
