using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-game UI system for selecting between Traditional GoGo and ReverseGoGo interaction techniques.
/// Displays two buttons at runtime - only one technique can be active at a time.
/// </summary>
public class TechniqueSelector : MonoBehaviour
{
    [Header("Technique Managers")]
    [Tooltip("GameObject with TraditionalGoGoInteraction component")]
    public GameObject traditionalGoGoManager;
    
    [Tooltip("GameObject with VirtualHandAttach component (ReverseGoGo)")]
    public GameObject reverseGoGoManager;

    [Header("UI References")]
    [Tooltip("Button to activate Traditional GoGo")]
    public Button traditionalGoGoButton;
    
    [Tooltip("Button to activate ReverseGoGo")]
    public Button reverseGoGoButton;

    [Header("UI Settings")]
    [Tooltip("Hide UI after selection (automatically after 2 seconds)")]
    public bool autoHideUI = true;
    
    [Tooltip("Seconds before hiding UI after selection")]
    public float hideDelay = 2f;

    [Header("Button Visual Feedback")]
    [Tooltip("Color for selected button")]
    public Color selectedColor = Color.green;
    
    [Tooltip("Color for unselected button")]
    public Color unselectedColor = Color.white;

    private GameObject uiCanvas;
    private bool techniqueSelected = false;

    void Start()
    {
        // Find the Canvas if not assigned
        if (uiCanvas == null)
        {
            uiCanvas = transform.root.gameObject; // Assumes this script is on a child of Canvas
        }

        // Validate references
        if (traditionalGoGoManager == null)
        {
            Debug.LogError("❌ TechniqueSelector: traditionalGoGoManager not assigned!");
        }

        if (reverseGoGoManager == null)
        {
            Debug.LogError("❌ TechniqueSelector: reverseGoGoManager not assigned!");
        }

        if (traditionalGoGoButton == null)
        {
            Debug.LogError("❌ TechniqueSelector: traditionalGoGoButton not assigned!");
        }

        if (reverseGoGoButton == null)
        {
            Debug.LogError("❌ TechniqueSelector: reverseGoGoButton not assigned!");
        }

        // Setup button listeners
        if (traditionalGoGoButton != null)
        {
            traditionalGoGoButton.onClick.AddListener(SelectTraditionalGoGo);
        }

        if (reverseGoGoButton != null)
        {
            reverseGoGoButton.onClick.AddListener(SelectReverseGoGo);
        }

        // Initially disable both techniques until user selects
        if (traditionalGoGoManager != null)
        {
            traditionalGoGoManager.SetActive(false);
        }

        if (reverseGoGoManager != null)
        {
            reverseGoGoManager.SetActive(false);
        }

        Debug.Log("✅ TechniqueSelector initialized. Please select an interaction technique.");
    }

    /// <summary>
    /// Activate Traditional GoGo technique
    /// </summary>
    public void SelectTraditionalGoGo()
    {
        if (techniqueSelected)
        {
            Debug.LogWarning("⚠️ Technique already selected. Restart to change.");
            return;
        }

        Debug.Log("🎯 User selected: Traditional GoGo (Extend hand to reach far objects)");

        // Enable Traditional GoGo
        if (traditionalGoGoManager != null)
        {
            traditionalGoGoManager.SetActive(true);
        }

        // Disable ReverseGoGo
        if (reverseGoGoManager != null)
        {
            reverseGoGoManager.SetActive(false);
        }

        // Update button visuals
        UpdateButtonColors(true);

        // Mark as selected
        techniqueSelected = true;

        // Hide UI after delay
        if (autoHideUI)
        {
            Invoke(nameof(HideUI), hideDelay);
        }
    }

    /// <summary>
    /// Activate ReverseGoGo technique
    /// </summary>
    public void SelectReverseGoGo()
    {
        if (techniqueSelected)
        {
            Debug.LogWarning("⚠️ Technique already selected. Restart to change.");
            return;
        }

        Debug.Log("🎯 User selected: ReverseGoGo (Retract hand to pull distant objects)");

        // Enable ReverseGoGo
        if (reverseGoGoManager != null)
        {
            reverseGoGoManager.SetActive(true);
        }

        // Disable Traditional GoGo
        if (traditionalGoGoManager != null)
        {
            traditionalGoGoManager.SetActive(false);
        }

        // Update button visuals
        UpdateButtonColors(false);

        // Mark as selected
        techniqueSelected = true;

        // Hide UI after delay
        if (autoHideUI)
        {
            Invoke(nameof(HideUI), hideDelay);
        }
    }

    /// <summary>
    /// Update button colors to show which is selected
    /// </summary>
    private void UpdateButtonColors(bool traditionalSelected)
    {
        if (traditionalGoGoButton != null)
        {
            ColorBlock colors = traditionalGoGoButton.colors;
            colors.normalColor = traditionalSelected ? selectedColor : unselectedColor;
            traditionalGoGoButton.colors = colors;
        }

        if (reverseGoGoButton != null)
        {
            ColorBlock colors = reverseGoGoButton.colors;
            colors.normalColor = traditionalSelected ? unselectedColor : selectedColor;
            reverseGoGoButton.colors = colors;
        }
    }

    /// <summary>
    /// Hide the selection UI (called automatically after selection if autoHideUI is true)
    /// </summary>
    private void HideUI()
    {
        if (uiCanvas != null)
        {
            Canvas canvas = uiCanvas.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.enabled = false;
                Debug.Log("🙈 Technique selection UI hidden");
            }
        }
    }

    /// <summary>
    /// Show the UI again (can be called via inspector event or key press)
    /// </summary>
    public void ShowUI()
    {
        if (uiCanvas != null)
        {
            Canvas canvas = uiCanvas.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.enabled = true;
                Debug.Log("👁️ Technique selection UI shown");
            }
        }
    }

    /// <summary>
    /// Allow runtime technique switching (for debugging/testing)
    /// </summary>
    void Update()
    {
        // Press 'T' to toggle UI visibility (for debugging)
        if (Input.GetKeyDown(KeyCode.T))
        {
            if (uiCanvas != null)
            {
                Canvas canvas = uiCanvas.GetComponent<Canvas>();
                if (canvas != null)
                {
                    canvas.enabled = !canvas.enabled;
                    Debug.Log($"UI toggled: {(canvas.enabled ? "Visible" : "Hidden")}");
                }
            }
        }
    }

    void OnDestroy()
    {
        // Remove button listeners
        if (traditionalGoGoButton != null)
        {
            traditionalGoGoButton.onClick.RemoveListener(SelectTraditionalGoGo);
        }

        if (reverseGoGoButton != null)
        {
            reverseGoGoButton.onClick.RemoveListener(SelectReverseGoGo);
        }
    }
}
