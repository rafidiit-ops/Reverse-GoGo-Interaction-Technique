using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

public class ControllerTrackingDiagnostic : MonoBehaviour
{
    void Start()
    {
        Debug.Log("========== CONTROLLER TRACKING DIAGNOSTIC ==========");
        
        // Check XR Settings
        Debug.Log($"XR Enabled: {XRSettings.enabled}");
        Debug.Log($"XR Device: {XRSettings.loadedDeviceName}");
        
        // Check Input Subsystems
        List<XRInputSubsystem> inputSubsystems = new List<XRInputSubsystem>();
        SubsystemManager.GetSubsystems(inputSubsystems);
        
        Debug.Log($"XR Input Subsystems: {inputSubsystems.Count}");
        foreach (var subsystem in inputSubsystems)
        {
            Debug.Log($"  - {subsystem.subsystemDescriptor.id}: Running={subsystem.running}");
        }
        
        // Check available input devices
        List<InputDevice> devices = new List<InputDevice>();
        InputDevices.GetDevices(devices);
        
        Debug.Log($"\nTotal Input Devices: {devices.Count}");
        foreach (var device in devices)
        {
            Debug.Log($"\n  Device: {device.name}");
            Debug.Log($"    Manufacturer: {device.manufacturer}");
            Debug.Log($"    Role: {device.characteristics}");
            Debug.Log($"    Valid: {device.isValid}");
            
            // Check tracking state
            if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked))
            {
                Debug.Log($"    Is Tracked: {isTracked}");
            }
            
            // Check position tracking
            if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position))
            {
                Debug.Log($"    Position: {position}");
            }
            
            // Check rotation tracking
            if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
            {
                Debug.Log($"    Rotation: {rotation.eulerAngles}");
            }
        }
        
        // Check for left/right controller specifically
        Debug.Log("\n--- Checking Left Controller ---");
        InputDevice leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        CheckControllerDevice(leftController, "Left Controller");
        
        Debug.Log("\n--- Checking Right Controller ---");
        InputDevice rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        CheckControllerDevice(rightController, "Right Controller");
        
        Debug.Log("\n========== DIAGNOSTIC COMPLETE ==========");
    }
    
    void CheckControllerDevice(InputDevice device, string name)
    {
        if (device.isValid)
        {
            Debug.Log($"✅ {name} device is valid");
            Debug.Log($"   Name: {device.name}");
            
            if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked))
            {
                if (isTracked)
                {
                    Debug.Log($"   ✅ Is being tracked");
                }
                else
                {
                    Debug.LogError($"   ❌ NOT TRACKED - Controller not responding!");
                }
            }
            else
            {
                Debug.LogError($"   ❌ Cannot read tracking state!");
            }
            
            if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos))
            {
                Debug.Log($"   Position: {pos}");
            }
            else
            {
                Debug.LogError($"   ❌ Cannot read position!");
            }
        }
        else
        {
            Debug.LogError($"❌ {name} device is INVALID or NOT CONNECTED!");
            Debug.LogError($"   This is why it's not responding!");
        }
    }
    
    void Update()
    {
        // Continuously check tracking state
        if (Time.frameCount % 60 == 0) // Every 60 frames
        {
            InputDevice rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (rightController.isValid)
            {
                rightController.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked);
                rightController.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos);
                Debug.Log($"[Tracking Check] Right: Tracked={tracked}, Pos={pos}");
            }
        }
    }
}
