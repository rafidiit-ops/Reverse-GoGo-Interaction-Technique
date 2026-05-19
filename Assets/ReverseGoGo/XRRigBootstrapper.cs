using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Attach this component to the XR Origin root GameObject inside the XR_Persistent scene.
///
/// This script:
///   1. Calls DontDestroyOnLoad on the XR rig so it survives across all scene loads — the
///      TrackedPoseDrivers, Camera, and both controllers stay alive with their OpenXR session
///      intact, preventing the Camera Offset Y bounce that causes controller drift.
///   2. On Start(), loads the initial content scene (UI by default) additively, so the app
///      has visible content alongside the persistent XR rig.
///
/// Scene structure (set up in Unity Editor, then add this component):
///   XR_Persistent.unity  ← always loaded, never unloaded
///   └─ XR Origin (VR)    ← has this component
///       └─ Camera Offset
///           ├─ Main Camera
///           ├─ Left Hand
///           └─ Right Hand
///
/// All other scenes (UI, ReverseGoGo SampleScene, TraditionalGoGoSampleScene,
/// HOMERStarterScene) should have their XR Origin removed — they are loaded/unloaded
/// additively by SceneAdditiveManager (via ControllerRaySceneMenu / GripReturnToUI).
/// </summary>
public class XRRigBootstrapper : MonoBehaviour
{
    [Tooltip("First scene to load additively when the app starts.")]
    public string initialScene = "UI";

    private static bool _initialized;

    void Awake()
    {
        // Singleton guard: if somehow this scene is loaded twice, destroy the duplicate.
        if (_initialized)
        {
            Destroy(gameObject);
            return;
        }

        _initialized = true;

        // Persist the entire XR rig across all future scene loads.
        // Because DontDestroyOnLoad requires a root-level GameObject, the XR Origin must
        // be at the scene root in XR_Persistent (not nested under any parent).
        DontDestroyOnLoad(gameObject);

        Debug.Log("[XRRigBootstrapper] XR rig marked DontDestroyOnLoad.");
    }

    void Start()
    {
        // Load the initial content scene (UI menu) so the user has something to see.
        if (!string.IsNullOrEmpty(initialScene) && !SceneManager.GetSceneByName(initialScene).isLoaded)
        {
            SceneAdditiveManager.SwitchTo(this, initialScene);
        }
    }

    // Reset the singleton flag if the app is restarted in the editor without a domain reload.
    void OnDestroy()
    {
        _initialized = false;
    }
}
