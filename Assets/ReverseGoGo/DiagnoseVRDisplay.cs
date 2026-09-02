using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using System.Collections.Generic;

/// <summary>
/// Diagnose VR Display Issues - checks why headset isn't showing game view
/// </summary>
public class DiagnoseVRDisplay : MonoBehaviour
{
    void Start()
    {
        // Wait a frame to let XR initialize
        Invoke("CheckVRDisplay", 1f);
    }

    void CheckVRDisplay()
    {
        Debug.Log("=== VR DISPLAY DIAGNOSTIC ===");
        
        // Check if XR is enabled
        Debug.Log($"XR Enabled: {XRSettings.enabled}");
        Debug.Log($"XR Device Model: {XRSettings.loadedDeviceName}");
        Debug.Log($"XR Is Device Active: {XRSettings.isDeviceActive}");
        
        // Check display subsystem
        List<XRDisplaySubsystem> displaySubsystems = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displaySubsystems);
        
        Debug.Log($"Display Subsystems Found: {displaySubsystems.Count}");
        
        foreach (var displaySubsystem in displaySubsystems)
        {
            Debug.Log($"  Display Subsystem: {displaySubsystem.GetType().Name}");
            Debug.Log($"  Running: {displaySubsystem.running}");
            
            if (displaySubsystem.running)
            {
                Debug.Log($"  ✅ Display subsystem is RUNNING");
            }
            else
            {
                Debug.LogError($"  ❌ Display subsystem is NOT RUNNING!");
            }
        }
        
        // Check XR General Settings
        var xrGeneralSettings = XRGeneralSettings.Instance;
        if (xrGeneralSettings != null)
        {
            Debug.Log($"XR General Settings Found: YES");
            
            var xrManager = xrGeneralSettings.Manager;
            if (xrManager != null)
            {
                Debug.Log($"XR Manager Active: {xrManager.isInitializationComplete}");
                Debug.Log($"XR Manager Loader: {(xrManager.activeLoader != null ? xrManager.activeLoader.GetType().Name : "NULL")}");
            }
            else
            {
                Debug.LogError("❌ XR Manager is NULL!");
            }
        }
        else
        {
            Debug.LogError("❌ XR General Settings is NULL!");
        }
        
        // Check camera render mode
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Debug.Log($"Main Camera Found: {mainCamera.name}");
            Debug.Log($"Camera Stereo Target Eye: {mainCamera.stereoTargetEye}");
            Debug.Log($"Camera Rendering Path: {mainCamera.renderingPath}");
            
            if (mainCamera.stereoTargetEye == StereoTargetEyeMask.Both)
            {
                Debug.Log("  ✅ Camera set to render to BOTH eyes (VR mode)");
            }
            else if (mainCamera.stereoTargetEye == StereoTargetEyeMask.None)
            {
                Debug.LogError("  ❌ Camera NOT rendering to VR headset (stereo disabled)!");
            }
        }
        else
        {
            Debug.LogError("❌ Main Camera not found!");
        }
        
        Debug.Log("=== END DIAGNOSTIC ===");
    }
}
