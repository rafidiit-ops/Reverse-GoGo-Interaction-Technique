using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

/// <summary>
/// Persistent singleton. Auto-spawns on startup.
/// In any non-UI scene: right A or B button press returns to the UI scene.
/// Uses the XR legacy input API (primaryButton / secondaryButton) for Meta/Oculus controllers.
/// No manual scene setup required.
/// </summary>
public class GripReturnToUI : MonoBehaviour
{
    private const string UiSceneName = "UI";

    private static GripReturnToUI _instance;

    // Start true so that a button already held when a scene loads is never mistaken for a new press.
    private bool _prevAB = true;

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
            _prevAB = true;
            return;
        }

        // Don't intercept A/B when HOMER is actively holding an object (clutch uses grip).
        HOMERController homer = FindFirstObjectByType<HOMERController>();
        if (homer != null && homer.IsAttached())
        {
            _prevAB = ReadRightAB(); // keep tracking so we don't false-fire after release
            return;
        }

        bool ab = ReadRightAB();
        bool pressed = ab && !_prevAB;
        _prevAB = ab;

        if (pressed)
        {
            Debug.Log("[GripReturnToUI] Right A/B pressed — loading UI scene.");
            SceneManager.LoadScene(UiSceneName);
        }
    }

    // Returns whether the right-hand A or B button is currently pressed.
    private static bool ReadRightAB()
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!device.isValid) return false;

        bool a = false;
        bool b = false;
        device.TryGetFeatureValue(CommonUsages.primaryButton, out a);   // A
        device.TryGetFeatureValue(CommonUsages.secondaryButton, out b); // B
        return a || b;
    }
}
