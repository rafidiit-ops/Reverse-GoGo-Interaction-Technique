using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

/// <summary>
/// Hides ALL controller renderers under Camera Offset at scene start and reveals
/// them only once OpenXR reports stable head tracking.
///
/// Root cause: on scene transition each XR controller starts at its prefab-serialized
/// world position (far from the user's hand). TrackedPoseDriver needs one or more
/// frames to apply the real tracked pose. This script hides every Renderer under
/// Camera Offset until that initialization window has passed.
///
/// Uses the same XR Origin name-search as FixControllerVisibility — proven reliable
/// across all scenes regardless of prefab depth. Attaches ONE component to the XR
/// Origin root so the Camera Offset subtree (both hands) is covered by a single instance.
///
/// Auto-spawns via RuntimeInitializeOnLoadMethod + SceneManager.sceneLoaded — no
/// manual scene setup required.
/// </summary>
public class HideUntilTracked : MonoBehaviour
{
    [Tooltip("Seconds of consecutive stable head tracking required before showing controllers.")]
    public float showDelay = 0.3f;

    [Tooltip("Failsafe: force-show after this many unscaled seconds even if tracking never reports valid.")]
    public float forceShowTimeout = 5f;

    private Transform _cameraOffset;
    private Renderer[] _renderers;
    private bool _shown;
    private float _elapsed;
    private float _stableTimer;

    // -----------------------------------------------------------------------
    // Auto-spawn: one instance per scene, on the XR Origin root.
    // -----------------------------------------------------------------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyToScene();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyToScene();

    private static void ApplyToScene()
    {
        // Locate XR Origin by name — same approach used by FixControllerVisibility,
        // ControllerVisibilityDebug, etc. Works regardless of prefab nesting depth
        // because GameObject.Find searches all active objects at runtime.
        GameObject xrOriginObj = GameObject.Find("XR Origin (VR)");
        if (xrOriginObj == null) xrOriginObj = GameObject.Find("XR Origin");
        if (xrOriginObj == null) return;

        if (xrOriginObj.GetComponent<HideUntilTracked>() != null) return; // already attached

        // Awake() runs synchronously, hiding all controllers before the first frame renders.
        xrOriginObj.AddComponent<HideUntilTracked>();
    }

    // -----------------------------------------------------------------------
    // Instance lifecycle
    // -----------------------------------------------------------------------
    void Awake()
    {
        // Camera Offset is the immediate child of XR Origin that holds all tracked nodes
        // (Main Camera, Left Hand, Right Hand). Collecting from here covers both controllers.
        _cameraOffset = transform.Find("Camera Offset");
        if (_cameraOffset != null)
            _renderers = _cameraOffset.GetComponentsInChildren<Renderer>(true);
        else
            _renderers = GetComponentsInChildren<Renderer>(true); // broad fallback

        // Force Camera Offset Y = 0 immediately.
        // The prefab serializes Camera Offset at Y=1.1176 (the Device-mode eye-height offset).
        // XROrigin.Awake() should reset this to 0 for Floor mode, but doing it here ensures
        // the correct value is applied before the first TrackedPoseDriver update.
        ForceCameraOffsetFloor();

        SetVisible(false);
    }

    void Update()
    {
        if (_shown) return;

        // Keep forcing Camera Offset Y = 0 every frame while hidden.
        // XROrigin.Update() can briefly re-apply the 1.1176 Device-mode offset during tracking
        // mode initialization after a scene switch (the XR subsystem takes a few frames to
        // fully switch to Floor mode). Forcing Y = 0 here prevents that offset from causing
        // the camera and controllers to appear ~1.1 m too high until the subsystem settles.
        ForceCameraOffsetFloor();

        // Re-hide every frame while not yet shown.
        // This overrides any script (e.g. FixControllerVisibility.Start()) that
        // re-enables renderers after our Awake() hid them.
        SetVisible(false);

        _elapsed += Time.unscaledDeltaTime;

        // Failsafe: never hide controllers forever if tracking is lost/unavailable.
        if (_elapsed >= forceShowTimeout)
        {
            SetVisible(true);
            _shown = true;
            return;
        }

        // Use head tracking as the signal — most reliable OpenXR indicator.
        // Head tracking starts valid as soon as the HMD pose is available,
        // which happens within 1-2 frames of scene load on any OpenXR device.
        var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!head.isValid) { _stableTimer = 0f; return; }

        bool tracked = false;
        Vector3 pos = Vector3.zero;
        head.TryGetFeatureValue(CommonUsages.isTracked, out tracked);
        head.TryGetFeatureValue(CommonUsages.devicePosition, out pos);

        // pos.y > 0.3 confirms floor-space tracking is active.
        // In Device mode the head starts at Y ≈ 0 (relative to session origin).
        // In Floor mode the head is at the user's real standing height (~1.5–1.8 m).
        // This also implicitly confirms Camera Offset Y = 0 is correct (floor mode).
        bool valid = tracked && pos.y > 0.3f;
        if (!valid) { _stableTimer = 0f; return; }

        // Require showDelay seconds of consecutive valid tracking before revealing.
        _stableTimer += Time.unscaledDeltaTime;
        if (_stableTimer >= showDelay)
        {
            SetVisible(true);
            _shown = true;
        }
    }

    // Force Camera Offset local Y = 0 (floor-mode position).
    // The prefab serializes Camera Offset at Y = 1.1176 (Device-mode eye-height).
    // XROrigin.Update() can re-apply that value while the XR subsystem transitions
    // to floor mode after a scene switch — this method prevents that drift.
    private void ForceCameraOffsetFloor()
    {
        if (_cameraOffset == null) return;
        Vector3 p = _cameraOffset.localPosition;
        if (Mathf.Abs(p.y) > 0.001f)
        {
            p.y = 0f;
            _cameraOffset.localPosition = p;
        }
    }

    private void SetVisible(bool visible)
    {
        if (_renderers == null) return;
        foreach (Renderer r in _renderers)
            if (r != null) r.enabled = visible;
    }
}
