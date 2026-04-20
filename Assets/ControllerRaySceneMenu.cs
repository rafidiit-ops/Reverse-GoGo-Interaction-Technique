using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// Standalone in-game scene menu controlled by controller ray + trigger.
/// Grip opens/closes menu and pauses gameplay while visible.
/// </summary>
public class ControllerRaySceneMenu : MonoBehaviour
{
    private const string UiSceneName = "UI";
    private const string UiScenePath = "Assets/UI/UI.unity";
    private const string TraditionalSceneName = "TraditionalGoGoSampleScene";
    private const string TraditionalScenePath = "Assets/TraditionalGoGo/TraditionalGoGoSampleScene.unity";
    private const string ReverseSceneName = "ReverseGoGo SampleScene";
    private const string ReverseScenePath = "Assets/ReverseGoGo/Scenes/SampleScene.unity";
    private const string HomerSceneName = "HOMERStarterScene";
    private const string HomerScenePath = "Assets/HOMER/Scenes/HOMERStarterScene.unity";

    private const float RayDistance = 3f;
    private const float RayVisualOffset = 0.01f;
    private const float MenuDistanceFromCamera = 1.2f;
    private const float MenuYawRecenterThresholdDeg = 60f;
    private const float TriggerUnlockDelaySeconds = 3.0f;

    private static ControllerRaySceneMenu instance;

    private Canvas menuCanvas;
    private Button[] optionButtons;
    private Text[] optionLabels;
    private string[] optionNames;
    private int selectedIndex;

    private LineRenderer rayLine;

    private bool isVisible;
    private int openedFrame = -1;

    private bool prevRightGrip = true;
    private bool prevLeftGrip = true;
    private bool prevRightTrigger = true;
    private bool uiScriptsDisconnected;
    private Vector3 anchoredMenuForward = Vector3.zero;
    private bool triggerArmed;
    private float triggerUnlockTime;

    private readonly Color normalButtonColor = new Color(0.18f, 0.18f, 0.18f, 0.95f);
    private readonly Color selectedButtonColor = new Color(0.12f, 0.4f, 0.18f, 0.98f);
    private readonly Color normalLabelColor = new Color(0.9f, 0.9f, 0.9f, 1f);
    private readonly Color selectedLabelColor = new Color(1f, 1f, 0.65f, 1f);

    private static readonly HashSet<string> PauseTypes = new HashSet<string>
    {
        "HandCalibrationDepthScale",
        "VirtualHandAttach",
        "TraditionalGoGoInteraction",
        "RaycastObjectSelector",
        "ReverseGoGoGrab",
        "XRReverseGoGo",
        "GoGoModeToggle",
        "HOMERController",
        "HOMERInteraction"
    };

    private readonly List<Behaviour> pausedBehaviours = new List<Behaviour>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (instance != null)
        {
            return;
        }

        GameObject root = new GameObject("ControllerRaySceneMenu");
        instance = root.AddComponent<ControllerRaySceneMenu>();
        DontDestroyOnLoad(root);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;

        // Only build the UI now if we're already in the UI scene.
        // Otherwise we wait for OnSceneLoaded to fire when UI loads.
        if (IsUiScene(SceneManager.GetActiveScene()))
        {
            BuildMenuUi();
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsUiScene(scene))
        {
            // Study scene loaded — just hide the menu and wait
            if (isVisible) HideMenu();
            uiScriptsDisconnected = false;
            return;
        }

        // UI scene loaded — (re)build if not yet built, then show the menu
        if (menuCanvas == null)
        {
            BuildMenuUi();
        }

        DisconnectUiSceneScripts();
        pausedBehaviours.Clear();
        RefreshButtons();
        ShowMenu();
    }

    private void Update()
    {
        if (!IsUiScene(SceneManager.GetActiveScene()))
        {
            return;
        }

        ReadInput(
            out bool gripPressedThisFrame,
            out bool triggerPressedThisFrame);

        if (!isVisible && gripPressedThisFrame)
        {
            ShowMenu();
        }

        if (!isVisible)
        {
            return;
        }

        bool canClose = Time.frameCount > openedFrame;
        if (canClose && gripPressedThisFrame)
        {
            HideMenu();
            return;
        }

        UpdateSelectionFromControllerRay();

            if (!triggerArmed)
            {
                if (!IsRightTriggerHeld())
                {
                    triggerArmed = true;
                }
            }

            if (triggerArmed && Time.unscaledTime >= triggerUnlockTime && triggerPressedThisFrame)
        {
            ActivateSelected();
        }

        RecenterMenuWhenHeadYawExceedsThreshold();
    }

    private void BuildMenuUi()
    {
        EnsureEventSystemExists();

        GameObject canvasObj = new GameObject("ControllerRaySceneMenuCanvas");
        canvasObj.transform.SetParent(transform, false);

        menuCanvas = canvasObj.AddComponent<Canvas>();
        menuCanvas.renderMode = RenderMode.WorldSpace;
        menuCanvas.sortingOrder = 1000;

        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        CreateRayLine(canvasObj.transform);

        GameObject panelObj = CreateUiObject("Panel", canvasObj.transform);
        Image panelImg = panelObj.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.55f);

        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(460f, 326f);

        GameObject titleObj = CreateUiObject("Title", panelObj.transform);
        Text title = titleObj.AddComponent<Text>();
        title.text = "Select Technique";
        title.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        title.alignment = TextAnchor.MiddleCenter;
        title.color = Color.white;

        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(0f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(12f, -10f);
        titleRect.sizeDelta = new Vector2(436f, 36f);

        Button traditional = CreateButton(panelObj.transform, "Traditional GoGo", new Vector2(12f, -58f));
        Button reverse = CreateButton(panelObj.transform, "Reverse GoGo", new Vector2(12f, -114f));
        Button homer = CreateButton(panelObj.transform, "HOMER", new Vector2(12f, -170f));
        Button close = CreateButton(panelObj.transform, "Close", new Vector2(12f, -226f));

        optionButtons = new[] { traditional, reverse, homer, close };
        optionLabels = new[]
        {
            traditional.GetComponentInChildren<Text>(),
            reverse.GetComponentInChildren<Text>(),
            homer.GetComponentInChildren<Text>(),
            close.GetComponentInChildren<Text>()
        };
        optionNames = new[] { "Traditional GoGo", "Reverse GoGo", "HOMER", "Close" };

        traditional.onClick.AddListener(() => LoadTechniqueScene(TraditionalSceneName, TraditionalScenePath));
        reverse.onClick.AddListener(() => LoadTechniqueScene(ReverseSceneName, ReverseScenePath));
        homer.onClick.AddListener(() => LoadTechniqueScene(HomerSceneName, HomerScenePath));
        close.onClick.AddListener(HideMenu);

        GameObject hintObj = CreateUiObject("Hint", panelObj.transform);
        Text hint = hintObj.AddComponent<Text>();
        hint.text = "Grip: open/close menu | Point controller ray + Trigger: select";
        hint.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hint.alignment = TextAnchor.MiddleCenter;
        hint.color = new Color(0.9f, 0.9f, 0.9f, 1f);

        RectTransform hintRect = hintObj.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0f, 1f);
        hintRect.anchorMax = new Vector2(0f, 1f);
        hintRect.pivot = new Vector2(0f, 1f);
        hintRect.anchoredPosition = new Vector2(12f, -280f);
        hintRect.sizeDelta = new Vector2(436f, 36f);

        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(460f, 326f);

        menuCanvas.enabled = false;
        RefreshButtons();
        ShowMenu();
    }

    private static bool IsUiScene(Scene scene)
    {
        if (!string.IsNullOrEmpty(scene.path) && scene.path == UiScenePath)
        {
            return true;
        }

        return scene.name == UiSceneName;
    }

    private void CreateRayLine(Transform parent)
    {
        GameObject lineObj = new GameObject("ControllerRayLine");
        lineObj.transform.SetParent(parent, false);

        rayLine = lineObj.AddComponent<LineRenderer>();
        rayLine.positionCount = 2;
        rayLine.useWorldSpace = true;
        rayLine.startWidth = 0.012f;
        rayLine.endWidth = 0.006f;
        rayLine.numCapVertices = 4;
        rayLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rayLine.receiveShadows = false;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        Material material = new Material(shader);
        material.color = new Color(1f, 0.95f, 0.2f, 0.95f);
        rayLine.material = material;

        rayLine.startColor = new Color(1f, 1f, 1f, 0.95f);
        rayLine.endColor = new Color(1f, 0.9f, 0.2f, 0.95f);
        rayLine.enabled = false;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();
        return obj;
    }

    private static Button CreateButton(Transform parent, string label, Vector2 anchoredPos)
    {
        GameObject buttonObj = CreateUiObject(label + "Button", parent);

        int menuButtonLayer = LayerMask.NameToLayer("MenuButton");
        if (menuButtonLayer < 0)
        {
            menuButtonLayer = LayerMask.NameToLayer("UI");
        }

        if (menuButtonLayer >= 0)
        {
            buttonObj.layer = menuButtonLayer;
        }

        Image buttonImg = buttonObj.AddComponent<Image>();
        buttonImg.color = new Color(0.18f, 0.18f, 0.18f, 0.95f);

        Button button = buttonObj.AddComponent<Button>();

        RectTransform buttonRect = buttonObj.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0f, 1f);
        buttonRect.anchorMax = new Vector2(0f, 1f);
        buttonRect.pivot = new Vector2(0f, 1f);
        buttonRect.anchoredPosition = anchoredPos;
        buttonRect.sizeDelta = new Vector2(436f, 48f);

        BoxCollider collider = buttonObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(buttonRect.sizeDelta.x, buttonRect.sizeDelta.y, 2f);

        GameObject textObj = CreateUiObject("Text", buttonObj.transform);
        Text text = textObj.AddComponent<Text>();
        text.text = label;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return button;
    }

    private static void EnsureEventSystemExists()
    {
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
        eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        DontDestroyOnLoad(eventSystem);
    }

    private void ReadInput(out bool gripPressedThisFrame, out bool triggerPressedThisFrame)
    {
        bool rightGrip = false;
        bool leftGrip = false;
        bool rightTrigger = false;

        InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (right.isValid)
        {
            right.TryGetFeatureValue(CommonUsages.gripButton, out rightGrip);
            right.TryGetFeatureValue(CommonUsages.triggerButton, out rightTrigger);
        }

        InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (left.isValid)
        {
            left.TryGetFeatureValue(CommonUsages.gripButton, out leftGrip);
        }

        bool grip = rightGrip || leftGrip;

        gripPressedThisFrame = grip && !(prevRightGrip || prevLeftGrip);
        triggerPressedThisFrame = rightTrigger && !prevRightTrigger;

        prevRightGrip = rightGrip;
        prevLeftGrip = leftGrip;
        prevRightTrigger = rightTrigger;
    }

    private void ShowMenu()
    {
        if (isVisible)
        {
            return;
        }

        DisconnectUiSceneScripts();
        isVisible = true;
        openedFrame = Time.frameCount;

        if (!IsUiScene(SceneManager.GetActiveScene()))
        {
            Time.timeScale = 0f;
            AudioListener.pause = true;
            PauseGameplay();
        }

        triggerArmed = false;
        triggerUnlockTime = Time.unscaledTime + TriggerUnlockDelaySeconds;
        prevRightTrigger = true;  // treat trigger as held to block any first-frame carryover

        menuCanvas.enabled = true;
        StartCoroutine(PlaceMenuWhenCameraReady());
        SetRayVisible(true);
        RefreshButtons();
        SetSelected(GetDefaultIndex());
    }

    // Retries PlaceMenuInFrontOfCamera every frame until Camera.main is available
    // AND XR head tracking is active (camera not sitting at world origin).
    private System.Collections.IEnumerator PlaceMenuWhenCameraReady()
    {
        // Wait for Camera.main to exist
        while (Camera.main == null)
            yield return null;

        // Wait for XR head tracking to be active: the camera must have moved
        // away from world origin, which means the HMD is actually tracking.
        int fallbackFrames = 0;
        while (fallbackFrames < 120) // 2s fallback at 60fps
        {
            InputDevice hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (hmd.isValid)
            {
                // Give tracking one extra frame to settle the position
                yield return null;
                break;
            }
            fallbackFrames++;
            yield return null;
        }

        PlaceMenuInFrontOfCamera(resetAnchorDirection: true);
    }

    private static bool IsRightTriggerHeld()
    {
        InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!right.isValid)
        {
            return false;
        }

        bool triggerPressed;
        if (right.TryGetFeatureValue(CommonUsages.triggerButton, out triggerPressed))
        {
            return triggerPressed;
        }

        return false;
    }

    private void HideMenu()
    {
        if (!isVisible)
        {
            return;
        }

        isVisible = false;

        if (!IsUiScene(SceneManager.GetActiveScene()))
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            ResumeGameplay();
        }

        menuCanvas.enabled = false;
        SetRayVisible(false);
    }

    private void PauseGameplay()
    {
        pausedBehaviours.Clear();

        Behaviour[] all = FindObjectsByType<Behaviour>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Behaviour b = all[i];
            if (b == null || !b.enabled)
            {
                continue;
            }

            if (b == this)
            {
                continue;
            }

            if (menuCanvas != null && b.transform.IsChildOf(menuCanvas.transform))
            {
                continue;
            }

            if (!PauseTypes.Contains(b.GetType().Name))
            {
                continue;
            }

            b.enabled = false;
            pausedBehaviours.Add(b);
        }
    }

    private void ResumeGameplay()
    {
        for (int i = 0; i < pausedBehaviours.Count; i++)
        {
            Behaviour b = pausedBehaviours[i];
            if (b != null)
            {
                b.enabled = true;
            }
        }

        pausedBehaviours.Clear();
    }

    private void PlaceMenuInFrontOfCamera(bool resetAnchorDirection)
    {
        if (menuCanvas == null)
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        Vector3 flatForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.0001f)
        {
            flatForward = cam.transform.forward;
        }

        flatForward.Normalize();

        if (resetAnchorDirection || anchoredMenuForward == Vector3.zero)
        {
            anchoredMenuForward = flatForward;
        }

        Transform t = menuCanvas.transform;
        t.position = cam.transform.position + anchoredMenuForward * MenuDistanceFromCamera;
        t.rotation = Quaternion.LookRotation(anchoredMenuForward, Vector3.up);
        t.localScale = Vector3.one * 0.0018f;
    }

    private void RecenterMenuWhenHeadYawExceedsThreshold()
    {
        if (!isVisible || menuCanvas == null)
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        Vector3 flatForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.0001f)
        {
            return;
        }

        flatForward.Normalize();

        if (anchoredMenuForward == Vector3.zero)
        {
            anchoredMenuForward = flatForward;
            PlaceMenuInFrontOfCamera(resetAnchorDirection: false);
            return;
        }

        // Recentre if the camera moved far from the menu (e.g. XR tracking started
        // after the menu was placed at world origin).
        float distToMenu = Vector3.Distance(cam.transform.position,
            cam.transform.position + anchoredMenuForward * MenuDistanceFromCamera -
            (menuCanvas.transform.position - (cam.transform.position + anchoredMenuForward * MenuDistanceFromCamera)));
        float camToMenuDist = Vector3.Distance(cam.transform.position, menuCanvas.transform.position);
        if (camToMenuDist > MenuDistanceFromCamera + 1.5f || camToMenuDist < MenuDistanceFromCamera - 1.0f)
        {
            anchoredMenuForward = flatForward;
            PlaceMenuInFrontOfCamera(resetAnchorDirection: false);
            return;
        }

        float yawDelta = Vector3.SignedAngle(anchoredMenuForward, flatForward, Vector3.up);
        if (Mathf.Abs(yawDelta) < MenuYawRecenterThresholdDeg)
        {
            return;
        }

        anchoredMenuForward = flatForward;
        PlaceMenuInFrontOfCamera(resetAnchorDirection: false);
    }

    private void UpdateSelectionFromControllerRay()
    {
        if (!TryGetControllerRay(out Ray ray))
        {
            SetRayVisible(false);
            return;
        }

        int menuLayer = LayerMask.NameToLayer("MenuButton");
        if (menuLayer < 0)
        {
            menuLayer = LayerMask.NameToLayer("UI");
        }

        int uiLayer = LayerMask.NameToLayer("UI");
        int mask = Physics.DefaultRaycastLayers;
        if (menuLayer >= 0)
        {
            mask |= (1 << menuLayer);
        }
        if (uiLayer >= 0)
        {
            mask |= (1 << uiLayer);
        }

        // SphereCast adds ~1.5 cm radius tolerance so aiming near a button still registers.
        const float sphereRadius = 0.015f;
        if (!Physics.SphereCast(ray, sphereRadius, out RaycastHit hit, RayDistance, mask, QueryTriggerInteraction.Collide))
        {
            SetRayVisible(true);
            SetRay(ray.origin, ray.origin + ray.direction * RayDistance);
            return;
        }

        SetRayVisible(true);
        SetRay(ray.origin, hit.point - ray.direction * RayVisualOffset);

        for (int i = 0; i < optionButtons.Length; i++)
        {
            Button b = optionButtons[i];
            if (b == null || !b.interactable)
            {
                continue;
            }

            Transform hitT = hit.collider != null ? hit.collider.transform : null;
            if (hitT == b.transform || (hitT != null && hitT.IsChildOf(b.transform)))
            {
                SetSelected(i);
                return;
            }
        }
    }

    private bool TryGetControllerRay(out Ray ray)
    {
        Transform right = FindControllerTransform(
            "RightHand Controller",
            "RightHandController",
            "Right Controller",
            "Right Hand",
            "RightHand");
        if (right != null)
        {
            ray = new Ray(right.position, right.forward);
            return true;
        }

        Transform left = FindControllerTransform(
            "LeftHand Controller",
            "LeftHandController",
            "Left Controller",
            "Left Hand",
            "LeftHand");
        if (left != null)
        {
            ray = new Ray(left.position, left.forward);
            return true;
        }

        InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (rightDevice.isValid
            && rightDevice.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 rightPos)
            && rightDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rightRot))
        {
            ApplyXrOriginTransform(ref rightPos, ref rightRot);
            ray = new Ray(rightPos, rightRot * Vector3.forward);
            return true;
        }

        InputDevice leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (leftDevice.isValid
            && leftDevice.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 leftPos)
            && leftDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion leftRot))
        {
            ApplyXrOriginTransform(ref leftPos, ref leftRot);
            ray = new Ray(leftPos, leftRot * Vector3.forward);
            return true;
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            ray = new Ray(cam.transform.position, cam.transform.forward);
            return true;
        }

        ray = default;
        return false;
    }

    private static Transform FindControllerTransform(params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            GameObject direct = GameObject.Find(names[i]);
            if (direct != null)
            {
                return direct.transform;
            }
        }

        Transform[] all = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null)
            {
                continue;
            }

            for (int n = 0; n < names.Length; n++)
            {
                if (string.Equals(t.name, names[n], System.StringComparison.OrdinalIgnoreCase))
                {
                    return t;
                }
            }
        }

        return null;
    }

    private static void ApplyXrOriginTransform(ref Vector3 position, ref Quaternion rotation)
    {
        Transform xrOrigin = FindXrOriginTransform();
        if (xrOrigin == null)
        {
            return;
        }

        position = xrOrigin.TransformPoint(position);
        rotation = xrOrigin.rotation * rotation;
    }

    private static Transform FindXrOriginTransform()
    {
        GameObject xrOriginByName = GameObject.Find("XR Origin");
        if (xrOriginByName != null)
        {
            return xrOriginByName.transform;
        }

        GameObject xrOriginVrByName = GameObject.Find("XR Origin (VR)");
        if (xrOriginVrByName != null)
        {
            return xrOriginVrByName.transform;
        }

        GameObject xrRigByName = GameObject.Find("XR Rig");
        if (xrRigByName != null)
        {
            return xrRigByName.transform;
        }

        Transform[] all = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null)
            {
                continue;
            }

            if (t.name.IndexOf("XR Origin", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return t;
            }
        }

        return null;
    }

    private void DisconnectUiSceneScripts()
    {
        if (uiScriptsDisconnected || !IsUiScene(SceneManager.GetActiveScene()))
        {
            return;
        }

        MonoBehaviour[] allBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        for (int i = 0; i < allBehaviours.Length; i++)
        {
            MonoBehaviour behaviour = allBehaviours[i];
            if (behaviour == null || behaviour == this)
            {
                continue;
            }

            if (!behaviour.enabled)
            {
                continue;
            }

            if (menuCanvas != null && behaviour.transform.IsChildOf(menuCanvas.transform))
            {
                continue;
            }

            System.Type type = behaviour.GetType();
            if (type.Assembly.GetName().Name != "Assembly-CSharp")
            {
                continue;
            }

            if (type.Name == "JoystickReturnToUIScene")
            {
                continue;
            }

            behaviour.enabled = false;
        }

        uiScriptsDisconnected = true;
    }

    private void ActivateSelected()
    {
        if (selectedIndex < 0 || selectedIndex >= optionButtons.Length)
        {
            return;
        }

        Button selected = optionButtons[selectedIndex];
        if (selected != null && selected.interactable)
        {
            selected.onClick.Invoke();
        }
    }

    private void LoadTechniqueScene(string sceneName, string scenePath)
    {
        HideMenu();

        Scene active = SceneManager.GetActiveScene();
        if (active.name == sceneName || active.path == scenePath)
        {
            return;
        }

        bool canLoadByPath = Application.CanStreamedLevelBeLoaded(scenePath);
        bool canLoadByName = Application.CanStreamedLevelBeLoaded(sceneName);

        if (canLoadByPath)
        {
            Debug.Log($"[ControllerRaySceneMenu] Loading scene by path: {scenePath}");
            SceneManager.LoadScene(scenePath, LoadSceneMode.Single);
            return;
        }

        if (canLoadByName)
        {
            Debug.Log($"[ControllerRaySceneMenu] Loading scene by name: {sceneName}");
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            return;
        }

        Debug.LogError($"[ControllerRaySceneMenu] Scene is not in Build Settings: name={sceneName}, path={scenePath}");
    }

    private int GetDefaultIndex()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.name == TraditionalSceneName || active.path == TraditionalScenePath)
        {
            return 0;
        }

        if (active.name == ReverseSceneName || active.path == ReverseScenePath)
        {
            return 1;
        }

        if (active.name == HomerSceneName || active.path == HomerScenePath)
        {
            return 2;
        }

        return 0;
    }

    private void RefreshButtons()
    {
        if (optionButtons == null || optionButtons.Length < 4)
        {
            return;
        }

        Scene active = SceneManager.GetActiveScene();
        optionButtons[0].interactable = active.name != TraditionalSceneName && active.path != TraditionalScenePath;
        optionButtons[1].interactable = active.name != ReverseSceneName && active.path != ReverseScenePath;
        optionButtons[2].interactable = active.name != HomerSceneName && active.path != HomerScenePath;
        optionButtons[3].interactable = true;

        SetSelected(GetDefaultIndex());
    }

    private void SetSelected(int index)
    {
        if (optionButtons == null || optionButtons.Length == 0)
        {
            return;
        }

        selectedIndex = Mathf.Clamp(index, 0, optionButtons.Length - 1);

        for (int i = 0; i < optionButtons.Length; i++)
        {
            Button b = optionButtons[i];
            if (b == null)
            {
                continue;
            }

            bool selected = i == selectedIndex && b.interactable;

            Image img = b.GetComponent<Image>();
            if (img != null)
            {
                img.color = selected ? selectedButtonColor : normalButtonColor;
            }

            if (optionLabels != null && i < optionLabels.Length && optionLabels[i] != null)
            {
                optionLabels[i].color = selected ? selectedLabelColor : normalLabelColor;
                optionLabels[i].text = selected ? "> " + optionNames[i] + " <" : optionNames[i];
            }
        }
    }

    private void SetRayVisible(bool visible)
    {
        if (rayLine != null)
        {
            rayLine.enabled = visible && isVisible;
        }
    }

    private void SetRay(Vector3 start, Vector3 end)
    {
        if (rayLine == null)
        {
            return;
        }

        rayLine.SetPosition(0, start);
        rayLine.SetPosition(1, end);
    }
}
