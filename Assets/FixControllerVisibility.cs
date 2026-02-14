using UnityEngine;

public class FixControllerVisibility : MonoBehaviour
{
    void Start()
    {
        Debug.Log("========== COMPREHENSIVE CONTROLLER FIX ==========");
        
        // Find XR Origin by name
        GameObject xrOriginObj = GameObject.Find("XR Origin (VR)");
        if (xrOriginObj == null)
        {
            xrOriginObj = GameObject.Find("XR Origin");
        }
        
        if (xrOriginObj == null)
        {
            Debug.LogError("❌ XR Origin not found!");
            return;
        }
        
        Debug.Log("✅ Found XR Origin: " + xrOriginObj.name);
        
        // Find Camera Offset
        Transform cameraOffset = xrOriginObj.transform.Find("Camera Offset");
        if (cameraOffset == null)
        {
            Debug.LogError("❌ Camera Offset not found!");
            return;
        }
        
        Debug.Log("✅ Found Camera Offset");
        
        // Check for Right Hand
        Transform rightHand = cameraOffset.Find("Right Hand");
        if (rightHand != null)
        {
            Debug.Log("✅ Found Right Hand controller");
            FixController(rightHand, "Right Hand");
        }
        else
        {
            Debug.LogWarning("⚠️ Right Hand not found in Camera Offset");
        }
        
        // Check for Left Hand
        Transform leftHand = cameraOffset.Find("Left Hand");
        if (leftHand != null)
        {
            Debug.Log("✅ Found Left Hand controller");
            FixController(leftHand, "Left Hand");
        }
        else
        {
            Debug.LogWarning("⚠️ Left Hand not found in Camera Offset");
        }
        
        Debug.Log("========== FIX COMPLETE ==========");
    }
    
    void FixController(Transform controller, string controllerName)
    {
        Debug.Log($"\n--- FIXING {controllerName} ---");
        
        // Check scale
        Vector3 scale = controller.localScale;
        Debug.Log($"Controller scale: {scale}");
        if (scale.x < 0.01f || scale.y < 0.01f || scale.z < 0.01f)
        {
            controller.localScale = Vector3.one;
            Debug.Log("⚠️ Scale was too small - reset to (1,1,1)");
        }
        
        // Check position
        Debug.Log($"Controller local position: {controller.localPosition}");
        
        // Find all renderers recursively
        Renderer[] allRenderers = controller.GetComponentsInChildren<Renderer>(true);
        Debug.Log($"Total renderers found: {allRenderers.Length}");
        
        foreach (Renderer renderer in allRenderers)
        {
            GameObject rendererObj = renderer.gameObject;
            
            Debug.Log($"\n  Checking: {rendererObj.name}");
            Debug.Log($"    - GameObject active: {rendererObj.activeSelf}");
            Debug.Log($"    - Renderer enabled: {renderer.enabled}");
            Debug.Log($"    - Layer: {LayerMask.LayerToName(rendererObj.layer)}");
            Debug.Log($"    - Local scale: {rendererObj.transform.localScale}");
            
            // Enable renderer
            if (!renderer.enabled)
            {
                renderer.enabled = true;
                Debug.Log($"    ✅ ENABLED renderer");
            }
            
            // Activate GameObject
            if (!rendererObj.activeSelf)
            {
                rendererObj.SetActive(true);
                Debug.Log($"    ✅ ACTIVATED GameObject");
            }
            
            // Check materials
            Material[] materials = renderer.sharedMaterials;
            Debug.Log($"    - Materials count: {materials.Length}");
            
            bool hasMissingMaterial = false;
            foreach (Material mat in materials)
            {
                if (mat == null)
                {
                    hasMissingMaterial = true;
                    Debug.LogWarning($"    ⚠️ NULL/MISSING MATERIAL DETECTED!");
                }
                else
                {
                    Debug.Log($"    - Material: {mat.name}, Shader: {mat.shader.name}");
                    
                    // Check if material has color and it's visible
                    if (mat.HasProperty("_Color"))
                    {
                        Color color = mat.color;
                        Debug.Log($"      Color: {color}, Alpha: {color.a}");
                        
                        if (color.a < 0.1f)
                        {
                            color.a = 1.0f;
                            mat.color = color;
                            Debug.Log($"      ✅ FIXED: Set alpha to 1.0");
                        }
                    }
                }
            }
            
            if (hasMissingMaterial)
            {
                Debug.LogError($"    ❌ CRITICAL: {rendererObj.name} has missing materials!");
                Debug.LogError($"       This is why controllers are invisible!");
                Debug.LogError($"       ACTION NEEDED: Re-import XR Interaction Toolkit Samples");
            }
            
            // Check scale
            Vector3 objScale = rendererObj.transform.localScale;
            if (objScale.x < 0.001f || objScale.y < 0.001f || objScale.z < 0.001f)
            {
                rendererObj.transform.localScale = Vector3.one;
                Debug.Log($"    ✅ FIXED: Reset scale from {objScale} to (1,1,1)");
            }
        }
    }
}
