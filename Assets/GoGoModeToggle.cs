using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.XR;
using UnityEngine.SceneManagement;

/// <summary>
/// Toggle between Traditional GoGo and ReverseGoGo interaction modes
/// Press a button (e.g., Y/B on Quest) to switch between modes
/// </summary>
public class GoGoModeToggle : MonoBehaviour
{
    [Header("Interaction Scripts")]
    public TraditionalGoGoInteraction traditionalGoGo;
    public VirtualHandAttach reverseGoGo;
    
    [Header("Toggle Input")]
    public InputActionProperty toggleAction;  // Assign a button to toggle modes

    private const string TraditionalSceneName = "TraditionalGoGoSampleScene";
    private const string ReverseSceneName = "ReverseGoGo SampleScene";
    private const string HomerSceneName = "HOMERStarterScene";
    
    [Header("Current Mode")]
    public bool useTraditionalGoGo = true;    // Start with Traditional GoGo by default

    private bool modeLockedForScene;
    
    void Start()
    {
        if (traditionalGoGo == null)
        {
            Debug.LogError("GoGoModeToggle: TraditionalGoGoInteraction not assigned!");
        }
        
        if (reverseGoGo == null)
        {
            Debug.LogError("GoGoModeToggle: VirtualHandAttach (ReverseGoGo) not assigned!");
        }
        
        // Enable toggle action
        if (toggleAction.action != null)
        {
            toggleAction.action.Enable();
        }
        
        // Lock behavior based on active study scene and avoid runtime toggles there.
        ConfigureModeForActiveScene();

        if (!modeLockedForScene)
        {
            // Non-study scenes can still use explicit toggle mode.
            SetMode(useTraditionalGoGo);
        }
        
        Debug.Log($"✅ GoGo Mode Toggle initialized. Current mode: {(useTraditionalGoGo ? "Traditional GoGo" : "ReverseGoGo")}");
    }
    
    void Update()
    {
        if (modeLockedForScene)
        {
            return;
        }

        string activeScene = SceneManager.GetActiveScene().name;

        // Hard-lock technique mapping in the study scenes.
        if (activeScene == TraditionalSceneName)
        {
            if (!useTraditionalGoGo || (traditionalGoGo != null && !traditionalGoGo.enabled) || (reverseGoGo != null && reverseGoGo.enabled))
            {
                useTraditionalGoGo = true;
                SetMode(true);
            }
            return;
        }

        if (activeScene == ReverseSceneName)
        {
            if (useTraditionalGoGo || (traditionalGoGo != null && traditionalGoGo.enabled) || (reverseGoGo != null && !reverseGoGo.enabled))
            {
                useTraditionalGoGo = false;
                SetMode(false);
            }
            return;
        }

        if (activeScene == TraditionalSceneName || activeScene == ReverseSceneName || activeScene == HomerSceneName)
        {
            return;
        }

        // Never allow trigger to drive mode toggling.
        if (IsRightTriggerPressed())
        {
            return;
        }

        // Toggle mode when button is pressed
        if (toggleAction.action != null && toggleAction.action.WasPressedThisFrame())
        {
            useTraditionalGoGo = !useTraditionalGoGo;
            SetMode(useTraditionalGoGo);
        }
    }

    private void ConfigureModeForActiveScene()
    {
        string activeScene = SceneManager.GetActiveScene().name;

        if (activeScene == TraditionalSceneName)
        {
            useTraditionalGoGo = true;
            SetMode(true);
            modeLockedForScene = true;
            return;
        }

        if (activeScene == ReverseSceneName)
        {
            useTraditionalGoGo = false;
            SetMode(false);
            modeLockedForScene = true;
            return;
        }

        if (activeScene == HomerSceneName)
        {
            // HOMER should run independently from GoGo toggle mapping.
            if (traditionalGoGo != null)
            {
                traditionalGoGo.enabled = false;
            }

            if (reverseGoGo != null)
            {
                reverseGoGo.enabled = false;
            }

            modeLockedForScene = true;
            return;
        }

        modeLockedForScene = false;
    }

    private static bool IsRightTriggerPressed()
    {
        UnityEngine.XR.InputDevice rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!rightHand.isValid)
        {
            return IsInputSystemTriggerPressed();
        }

        bool triggerPressed;
        if (rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out triggerPressed))
        {
            return triggerPressed;
        }

        return IsInputSystemTriggerPressed();
    }

    private static bool IsInputSystemTriggerPressed()
    {
        UnityEngine.InputSystem.InputDevice rightHandDevice = InputSystem.GetDevice("RightHand");
        if (rightHandDevice == null)
        {
            return false;
        }

        ButtonControl triggerPressedControl = rightHandDevice.TryGetChildControl<ButtonControl>("triggerPressed");
        if (triggerPressedControl != null)
        {
            return triggerPressedControl.isPressed;
        }

        AxisControl triggerControl = rightHandDevice.TryGetChildControl<AxisControl>("trigger");
        if (triggerControl != null)
        {
            return triggerControl.ReadValue() > 0.55f;
        }

        return false;
    }
    
    private void SetMode(bool traditional)
    {
        if (traditionalGoGo != null)
        {
            traditionalGoGo.enabled = traditional;
        }
        
        if (reverseGoGo != null)
        {
            reverseGoGo.enabled = !traditional;
        }
        
        string modeName = traditional ? "Traditional GoGo (Extend to Reach)" : "ReverseGoGo (Retract to Pull)";
        Debug.Log($"🔄 Mode switched to: {modeName}");
    }
    
    /// <summary>
    /// Public method to switch to Traditional GoGo
    /// </summary>
    public void SwitchToTraditionalGoGo()
    {
        useTraditionalGoGo = true;
        SetMode(true);
    }
    
    /// <summary>
    /// Public method to switch to ReverseGoGo
    /// </summary>
    public void SwitchToReverseGoGo()
    {
        useTraditionalGoGo = false;
        SetMode(false);
    }
    
    /// <summary>
    /// Get current mode name
    /// </summary>
    public string GetCurrentModeName()
    {
        return useTraditionalGoGo ? "Traditional GoGo" : "ReverseGoGo";
    }
}
