using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Serialization;
using UnityEngine.XR;
using UnityEngine.SceneManagement;

/// <summary>
/// Toggle between Traditional GoGo and GoMER interaction modes
/// Press a button (e.g., Y/B on Quest) to switch between modes
/// </summary>
public class GoGoModeToggle : MonoBehaviour
{
    [Header("Interaction Scripts")]
    public TraditionalGoGoInteraction traditionalGoGo;
    [FormerlySerializedAs("reverseGoGo")]
    public VirtualHandAttach goMER;
    
    [Header("Toggle Input")]
    public InputActionProperty toggleAction;  // Assign a button to toggle modes

    private const string TraditionalSceneName = "TraditionalGoGoSampleScene";
    private const string GoMERSceneName = "GoMER SampleScene";
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
        
        if (goMER == null)
        {
            Debug.LogError("GoGoModeToggle: VirtualHandAttach (GoMER) not assigned!");
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
        
        Debug.Log($"✅ GoGo Mode Toggle initialized. Current mode: {(useTraditionalGoGo ? "Traditional GoGo" : "GoMER")}");
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
            if (!useTraditionalGoGo || (traditionalGoGo != null && !traditionalGoGo.enabled) || (goMER != null && goMER.enabled))
            {
                useTraditionalGoGo = true;
                SetMode(true);
            }
            return;
        }

        if (activeScene == GoMERSceneName)
        {
            if (useTraditionalGoGo || (traditionalGoGo != null && traditionalGoGo.enabled) || (goMER != null && !goMER.enabled))
            {
                useTraditionalGoGo = false;
                SetMode(false);
            }
            return;
        }

        if (activeScene == TraditionalSceneName || activeScene == GoMERSceneName || activeScene == HomerSceneName)
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

        if (activeScene == GoMERSceneName)
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

            if (goMER != null)
            {
                goMER.enabled = false;
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
        
        if (goMER != null)
        {
            goMER.enabled = !traditional;
        }
        
        string modeName = traditional ? "Traditional GoGo (Extend to Reach)" : "GoMER (Retract to Pull)";
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
    /// Public method to switch to GoMER
    /// </summary>
    public void SwitchToGoMER()
    {
        useTraditionalGoGo = false;
        SetMode(false);
    }
    
    /// <summary>
    /// Get current mode name
    /// </summary>
    public string GetCurrentModeName()
    {
        return useTraditionalGoGo ? "Traditional GoGo" : "GoMER";
    }
}
