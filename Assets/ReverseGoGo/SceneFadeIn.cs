using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Fades the view from black to clear at the start of every scene until head
/// tracking reports a valid non-zero pose.  This hides the brief period where
/// the camera sits at its scene-serialized default position before
/// TrackedPoseDriver moves it to the real head pose — which makes the scene
/// appear "far away" from the user.
///
/// Works in stereo (single-pass instanced OpenXR) because the overlay is a
/// WorldSpace canvas parented to the camera — a regular mesh that renders
/// correctly for both eyes with no GL/CommandBuffer workarounds needed.
///
/// Auto-attaches to Main Camera in every scene via RuntimeInitializeOnLoadMethod
/// and SceneManager.sceneLoaded — no manual setup required in any scene.
/// </summary>
[RequireComponent(typeof(Camera))]
public class SceneFadeIn : MonoBehaviour
{
    [Tooltip("Seconds of consecutive valid head tracking required before starting fade-in.")]
    public float trackingStableDelay = 0.1f;

    [Tooltip("Duration of the black-to-clear fade once tracking is confirmed stable.")]
    public float fadeDuration = 0.35f;

    [Tooltip("Failsafe: begin fade-in after this many unscaled seconds even if head tracking never reports valid.")]
    public float forceStartTimeout = 4f;

    private Image _overlay;     // black quad that we fade out
    private float _alpha = 1f;  // 1 = fully black, 0 = fully clear
    private float _elapsed;     // total unscaled seconds since Awake
    private float _stable;      // consecutive unscaled seconds with valid head tracking
    private bool  _fading;      // actively running the fade-in animation
    private bool  _done;        // fade complete — nothing more to do

    // -----------------------------------------------------------------------
    // Auto-spawn: attach to Main Camera in every scene.
    // RuntimeInitializeOnLoadMethod fires once; sceneLoaded covers all reloads.
    // -----------------------------------------------------------------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Attach();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach();

    private static void Attach()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        if (cam.GetComponent<SceneFadeIn>() != null) return;
        cam.gameObject.AddComponent<SceneFadeIn>();
    }

    // -----------------------------------------------------------------------
    // Instance lifecycle
    // -----------------------------------------------------------------------
    void Awake()
    {
        Camera cam = GetComponent<Camera>();

        // Place the overlay quad just in front of the near clip plane so it is
        // always closer to the camera than any scene geometry — no depth-test
        // tricks needed.  Parenting to the camera means it rotates with the head.
        float dist = cam.nearClipPlane + 0.02f;

        // Canvas (WorldSpace — renders as a real stereo mesh, works in single-pass)
        var canvasGO = new GameObject("_SceneFadeCanvas");
        canvasGO.transform.SetParent(transform, false);
        canvasGO.transform.localPosition = new Vector3(0f, 0f, dist);
        canvasGO.transform.localRotation = Quaternion.identity;

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.WorldSpace;
        canvas.sortingOrder = 32767;   // render after all other canvases

        // Size the rect so it comfortably covers a ~110° FOV at this distance
        // (Quest FOV ≈ 95°h × 90°v; tan(55°) ≈ 1.43, so half-height ≈ dist*1.43)
        float halfSize = dist * 2f;   // generous: 2 × dist covers >90° half-angle
        var rt = canvasGO.GetComponent<RectTransform>();
        rt.sizeDelta  = new Vector2(100f, 100f);                // canvas units
        rt.localScale = Vector3.one * (halfSize * 2f / 100f);  // scale to world size

        // Image — fills the canvas rect; set black, full opacity at start
        var imageGO = new GameObject("_FadeImage");
        imageGO.transform.SetParent(canvasGO.transform, false);
        _overlay = imageGO.AddComponent<Image>();
        _overlay.color = Color.black;

        var imgRt = imageGO.GetComponent<RectTransform>();
        imgRt.anchorMin    = Vector2.zero;
        imgRt.anchorMax    = Vector2.one;
        imgRt.sizeDelta    = Vector2.zero;
        imgRt.localPosition = Vector3.zero;
    }

    // -----------------------------------------------------------------------
    // Update: wait for stable head tracking, then fade in
    // -----------------------------------------------------------------------
    void Update()
    {
        if (_done) return;

        _elapsed += Time.unscaledDeltaTime;

        // --- Running fade-in ---
        if (_fading)
        {
            _alpha -= Time.unscaledDeltaTime / Mathf.Max(fadeDuration, 0.01f);
            if (_alpha <= 0f)
            {
                _alpha = 0f;
                _done  = true;
                if (_overlay != null) Destroy(_overlay.transform.parent.gameObject);
            }
            else
            {
                SetAlpha(_alpha);
            }
            return;
        }

        // --- Failsafe: force start after timeout ---
        if (_elapsed >= forceStartTimeout) { _fading = true; return; }

        // --- Check head tracking ---
        var head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
        bool valid = false;
        if (head.isValid)
        {
            bool   tracked = false;
            Vector3 pos    = Vector3.zero;
            head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked,     out tracked);
            head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out pos);
            valid = tracked && pos.sqrMagnitude > 0.0001f;
        }

        if (!valid) { _stable = 0f; return; }

        _stable += Time.unscaledDeltaTime;
        if (_stable >= trackingStableDelay) _fading = true;
    }

    private void SetAlpha(float a)
    {
        if (_overlay == null) return;
        Color c = _overlay.color;
        c.a = a;
        _overlay.color = c;
    }
}
