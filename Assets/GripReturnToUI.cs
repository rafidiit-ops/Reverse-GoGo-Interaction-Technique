using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

/// <summary>
/// Persistent singleton. Auto-spawns on startup.
/// In any non-UI scene: right grip press returns to the UI scene.
/// Uses the XR legacy input API (checks both gripButton and grip axis) for maximum
/// hardware compatibility on Meta/Oculus controllers.
/// No manual scene setup required.
/// </summary>
public class GripReturnToUI : MonoBehaviour
{
    private const string UiSceneName = "UI";

    private static GripReturnToUI _instance;

    // Start true so that a grip already held when a scene loads is never mistaken for a new press.
    private bool _prevGrip = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (_instance != null) return;
        var go = new GameObject("[GripReturnToUI]");
        _instance = go.AddComponent<GripReturnToUI>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        if (SceneManager.GetActiveScene().name == UiSceneName)
        {
            // In UI: keep prev = true so that returning to a study scene
            // won't fire on the very first frame (carryover guard).
            _prevGrip = true;
            return;
        }

        // Don't intercept grip when HOMER is actively holding an object (clutch uses grip).
        HOMERController homer = FindFirstObjectByType<HOMERController>();
        if (homer != null && homer.IsAttached())
        {
            _prevGrip = ReadRightGrip(); // keep tracking so we don't false-fire after release
            return;
        }

        bool grip = ReadRightGrip();
        bool pressed = grip && !_prevGrip;
        _prevGrip = grip;

        if (pressed)
        {
            Debug.Log("[GripReturnToUI] Right grip pressed — loading UI scene.");
            SceneManager.LoadScene(UiSceneName);
        }
    }

    // Returns whether the right-hand grip is currently pressed.
    // Checks the digital gripButton first; falls back to the analogue grip axis
    // (threshold 0.7) for controllers that only expose an axis.
    private static bool ReadRightGrip()
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!device.isValid) return false;

        bool gripBtn;
        if (device.TryGetFeatureValue(CommonUsages.gripButton, out gripBtn) && gripBtn)
            return true;

        float gripAxis;
        if (device.TryGetFeatureValue(CommonUsages.grip, out gripAxis))
            return gripAxis > 0.7f;

        return false;
    }
}
