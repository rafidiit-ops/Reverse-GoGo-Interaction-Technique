using UnityEngine;
using UnityEngine.InputSystem;

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
    
    [Header("Current Mode")]
    public bool useTraditionalGoGo = true;    // Start with Traditional GoGo by default
    
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
        
        // Set initial mode
        SetMode(useTraditionalGoGo);
        
        Debug.Log($"✅ GoGo Mode Toggle initialized. Current mode: {(useTraditionalGoGo ? "Traditional GoGo" : "ReverseGoGo")}");
    }
    
    void Update()
    {
        // Toggle mode when button is pressed
        if (toggleAction.action != null && toggleAction.action.WasPressedThisFrame())
        {
            useTraditionalGoGo = !useTraditionalGoGo;
            SetMode(useTraditionalGoGo);
        }
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
