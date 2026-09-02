using UnityEngine;

/// <summary>
/// Quick diagnostic tool to check controller visibility issues
/// Attach this to any GameObject in your scene and press Play
/// </summary>
public class ControllerVisibilityDebug : MonoBehaviour
{
    void Start()
    {
        Debug.Log("===== CONTROLLER VISIBILITY DIAGNOSTIC =====");
        
        // Find XR Origin
        GameObject xrOrigin = GameObject.Find("XR Origin (VR)");
        if (xrOrigin == null)
        {
            xrOrigin = GameObject.Find("XR Origin");
        }
        
        if (xrOrigin == null)
        {
            Debug.LogError("❌ XR Origin not found in scene!");
            return;
        }
        
        Debug.Log($"✅ Found XR Origin: {xrOrigin.name}");
        
        // Check for Camera Offset
        Transform cameraOffset = xrOrigin.transform.Find("Camera Offset");
        if (cameraOffset == null)
        {
            Debug.LogError("❌ Camera Offset not found!");
            return;
        }
        
        // Check Right Controller
        Transform rightController = cameraOffset.Find("RightHand Controller");
        if (rightController == null)
        {
            rightController = cameraOffset.Find("Right Controller");
        }
        if (rightController == null)
        {
            rightController = cameraOffset.Find("RightHandController");
        }
        
        if (rightController != null)
        {
            Debug.Log($"✅ Found Right Controller: {rightController.name}");
            Debug.Log($"   - Is Active: {rightController.gameObject.activeSelf}");
            Debug.Log($"   - Is Active in Hierarchy: {rightController.gameObject.activeInHierarchy}");
            
            // Check renderers
            Renderer[] renderers = rightController.GetComponentsInChildren<Renderer>();
            Debug.Log($"   - Total Renderers found: {renderers.Length}");
            foreach (Renderer rend in renderers)
            {
                Debug.Log($"      • {rend.gameObject.name} - Enabled: {rend.enabled} | Active: {rend.gameObject.activeSelf}");
            }
        }
        else
        {
            Debug.LogError("❌ Right Controller not found!");
        }
        
        // Check Left Controller
        Transform leftController = cameraOffset.Find("LeftHand Controller");
        if (leftController == null)
        {
            leftController = cameraOffset.Find("Left Controller");
        }
        if (leftController == null)
        {
            leftController = cameraOffset.Find("LeftHandController");
        }
        
        if (leftController != null)
        {
            Debug.Log($"✅ Found Left Controller: {leftController.name}");
            Debug.Log($"   - Is Active: {leftController.gameObject.activeSelf}");
            Debug.Log($"   - Is Active in Hierarchy: {leftController.gameObject.activeInHierarchy}");
            
            // Check renderers
            Renderer[] renderers = leftController.GetComponentsInChildren<Renderer>();
            Debug.Log($"   - Total Renderers found: {renderers.Length}");
            foreach (Renderer rend in renderers)
            {
                Debug.Log($"      • {rend.gameObject.name} - Enabled: {rend.enabled} | Active: {rend.gameObject.activeSelf}");
            }
        }
        else
        {
            Debug.LogWarning("⚠️ Left Controller not found!");
        }
        
        // Check for XR Input Modality Manager
        var modalityManager = FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.Inputs.XRInputModalityManager>();
        if (modalityManager != null)
        {
            Debug.Log("⚠️ XRInputModalityManager found - this controls controller visibility!");
            Debug.Log("   Check this component in Inspector to see Left/Right Controller assignments");
        }
        
        Debug.Log("===== END DIAGNOSTIC =====");
    }
}
