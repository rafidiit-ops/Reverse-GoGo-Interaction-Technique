using UnityEngine;
using UnityEngine.XR.Management;

public class FixControllerTracking : MonoBehaviour
{
    void Start()
    {
        Debug.Log("========== FIXING CONTROLLER TRACKING ==========");
        
        // Check if XR is initialized
        if (XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null)
        {
            var xrManager = XRGeneralSettings.Instance.Manager;
            
            if (xrManager.activeLoader != null)
            {
                Debug.Log($"✅ XR Loader active: {xrManager.activeLoader.name}");
            }
            else
            {
                Debug.LogError("❌ No XR Loader active!");
                Debug.LogError("   Action: Go to Edit → Project Settings → XR Plug-in Management");
                Debug.LogError("   Enable 'Oculus' or 'OpenXR' for your platform");
            }
        }
        else
        {
            Debug.LogError("❌ XR Management not initialized!");
        }
        
        // Check XR Origin tracking setup
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
                    CheckTrackedPoseDriver(rightHand, "Right Hand");
                }
                
                // Check Left Hand  
                Transform leftHand = cameraOffset.Find("Left Hand");
                if (leftHand != null)
                {
                    CheckTrackedPoseDriver(leftHand, "Left Hand");
                }
            }
        }
        
        Debug.Log("========== FIX ATTEMPT COMPLETE ==========");
        Debug.Log("If tracking still not working:");
        Debug.Log("1. Check Edit → Project Settings → XR Plug-in Management");
        Debug.Log("2. Ensure Oculus (or OpenXR) is enabled");
        Debug.Log("3. Restart Unity Editor");
        Debug.Log("4. Rebuild and test in actual VR headset");
    }
    
    void CheckTrackedPoseDriver(Transform controller, string name)
    {
        Debug.Log($"\n--- Checking {name} Tracking Components ---");
        
        // Check for various tracking components
        var trackedPoseDriver = controller.GetComponent<UnityEngine.SpatialTracking.TrackedPoseDriver>();
        if (trackedPoseDriver != null)
        {
            Debug.Log($"✅ TrackedPoseDriver found (Legacy)");
            Debug.Log($"   Tracking Type: {trackedPoseDriver.trackingType}");
            Debug.Log($"   Update Type: {trackedPoseDriver.updateType}");
            Debug.Log($"   Device Node: {trackedPoseDriver.deviceType}");
        }
        
        // Check for XR Controller component
        var xrController = controller.GetComponent<UnityEngine.XR.Interaction.Toolkit.XRController>();
        if (xrController != null)
        {
            Debug.Log($"✅ XRController found");
            Debug.Log($"   Controller Node: {xrController.controllerNode}");
            Debug.Log($"   Enabled: {xrController.enabled}");
            
            if (!xrController.enabled)
            {
                xrController.enabled = true;
                Debug.Log($"   ✅ ENABLED XRController");
            }
        }
        else
        {
            Debug.LogWarning($"⚠️ No XRController component found on {name}");
        }
        
        // Check for ActionBasedController (newer XR toolkit)
        var actionController = controller.GetComponent<UnityEngine.XR.Interaction.Toolkit.ActionBasedController>();
        if (actionController != null)
        {
            Debug.Log($"✅ ActionBasedController found");
            Debug.Log($"   Enabled: {actionController.enabled}");
            
            if (!actionController.enabled)
            {
                actionController.enabled = true;
                Debug.Log($"   ✅ ENABLED ActionBasedController");
            }
        }
        
        if (trackedPoseDriver == null && xrController == null && actionController == null)
        {
            Debug.LogError($"❌ NO TRACKING COMPONENT FOUND ON {name}!");
            Debug.LogError($"   Controller won't track without XRController or TrackedPoseDriver!");
            Debug.LogError($"   ACTION: Add XRController or ActionBasedController component");
        }
    }
}
