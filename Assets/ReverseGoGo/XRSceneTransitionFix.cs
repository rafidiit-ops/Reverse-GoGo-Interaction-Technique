using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

/// <summary>
/// Persistent singleton that forces XR tracking origin mode to Floor immediately after
/// every scene load.
///
/// Root cause of the scene-switch controller position drift:
///   When a new scene loads, XROrigin.Awake() requests Floor tracking mode from the XR
///   subsystem, but the subsystem needs a few frames to process the request.
///   During that window XROrigin.Update() detects the subsystem still reporting Device
///   mode and applies the serialized CameraYOffset (1.1176 m) to Camera Offset.
///   TrackedPoseDriver is already supplying floor-space poses, so the camera ends up at
///   ~2.8 m instead of ~1.7 m — making everything appear far below the user.
///   It self-corrects once the subsystem settles, but this fix prevents it ever happening.
///
/// Fix strategy (matches Claude AI "Fix 2" + "Fix 4" recommendations):
///   After every scene load, wait two frames for XROrigin.Awake/Update to run, then:
///   1. Zero Camera Offset Y directly (belt-and-suspenders alongside HideUntilTracked).
///   2. Re-request Floor tracking on the XROrigin — forces the XR subsystem to re-apply
///      the floor-space origin, ending the Device-mode transition window immediately.
///
/// Works alongside HideUntilTracked.cs which:
///   - Hides all controller renderers until floor-space tracking is confirmed.
///   - Forces Camera Offset Y = 0 every frame while hidden.
///   This pairing provides defense-in-depth: XRSceneTransitionFix fixes the cause,
///   HideUntilTracked masks any sub-frame residual before the fix applies.
/// </summary>
[DefaultExecutionOrder(-100)]
public class XRSceneTransitionFix : MonoBehaviour
{
    private static XRSceneTransitionFix _instance;

    // Auto-spawn a persistent instance on first scene load; no manual scene setup needed.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        GameObject go = new GameObject("[XRSceneTransitionFix]");
        _instance = go.AddComponent<XRSceneTransitionFix>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (_instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(ForceFloorTracking(scene));
    }

    private IEnumerator ForceFloorTracking(Scene scene)
    {
        // Two frames: long enough for XROrigin.Awake() + its first Update() to run,
        // short enough that HideUntilTracked is still hiding controllers during this window.
        yield return null;
        yield return null;

        // Step 1: Zero Camera Offset Y directly as immediate insurance.
        // The prefab serializes Camera Offset at Y = 1.1176 (Device-mode eye-height).
        // HideUntilTracked also does this every frame, but doing it here too ensures
        // correctness even if HideUntilTracked is briefly disabled or not yet active.
        ForceCameraOffsetFloor();

        // Step 2: Re-request Floor tracking mode on the XROrigin.
        // This forces XROrigin.Update() to re-submit the TrySetTrackingOriginMode(Floor)
        // call to the XR subsystem, ending the Device-mode transition window.
        // After this call XROrigin will stop applying the 1.1176 m Device-mode offset.
        XROrigin origin = FindFirstObjectByType<XROrigin>();
        if (origin == null) yield break;

        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        Debug.Log($"[XRSceneTransitionFix] Floor tracking re-applied on scene '{scene.name}'.");
    }

    private static void ForceCameraOffsetFloor()
    {
        GameObject xrOriginObj = GameObject.Find("XR Origin (VR)");
        if (xrOriginObj == null) xrOriginObj = GameObject.Find("XR Origin");
        if (xrOriginObj == null) return;

        Transform camOffset = xrOriginObj.transform.Find("Camera Offset");
        if (camOffset == null) return;

        Vector3 p = camOffset.localPosition;
        if (Mathf.Abs(p.y) > 0.001f)
        {
            p.y = 0f;
            camOffset.localPosition = p;
            Debug.Log($"[XRSceneTransitionFix] Corrected Camera Offset Y from {p.y} to 0.");
        }
    }
}
