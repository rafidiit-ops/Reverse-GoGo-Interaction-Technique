using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using System.Collections.Generic;

/// <summary>
/// Creates a small runtime UI that lets users switch between study scenes.
/// It is auto-spawned and persisted across scene loads.
/// </summary>
public class SceneToggleRuntimeUI : MonoBehaviour
{
    private static bool RuntimeMenuEnabled = false;
    private const string TraditionalSceneName = "TraditionalGoGoSampleScene";
    private const string ReverseSceneName = "ReverseGoGo SampleScene";
    private const string HomerSceneName = "HOMERStarterScene";
    private const float MaxControllerRayDistance = 3f;
    private const float RayHitVisualOffset = 0.01f;

    private static SceneToggleRuntimeUI instance;

    private Canvas menuCanvas;
    private RectTransform panelRect;
    private LineRenderer gazeLine;
    private Transform rightControllerRayTransform;
    private Transform leftControllerRayTransform;
    private Button traditionalButton;
    private Button reverseButton;
    private Button homerButton;
    private Button exitButton;
    private Button[] optionButtons;
    private Text[] optionLabels;
    private string[] baseOptionLabels;
    private int selectedOption;
    private Text hintText;

    private bool isMenuVisible = false;
    private bool previousAPressed;
    private bool previousGripPressed;
    private bool previousTriggerPressed;
    private bool previousNavUp;
    private bool previousNavDown;
    private int menuOpenedFrame = -1;

    private readonly Color normalButtonColor = new Color(0.18f, 0.18f, 0.18f, 0.95f);
    private readonly Color selectedButtonColor = new Color(0.12f, 0.4f, 0.18f, 0.98f);
    private readonly Color normalLabelColor = new Color(0.9f, 0.9f, 0.9f, 1f);
    private readonly Color selectedLabelColor = new Color(1f, 1f, 0.65f, 1f);

    // Scripts that should be suspended while the menu is visible.
    private static readonly HashSet<string> PausedBehaviourTypeNames = new HashSet<string>
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

    private readonly List<Behaviour> disabledForMenu = new List<Behaviour>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (!RuntimeMenuEnabled)
            return;

        if (instance != null)
            return;

        GameObject root = new GameObject("SceneToggleRuntimeUI");
        instance = root.AddComponent<SceneToggleRuntimeUI>();
        DontDestroyOnLoad(root);
    }

    private void Awake()
    {
        if (!RuntimeMenuEnabled)
        {
            Destroy(gameObject);
            return;
        }

        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        BuildUi();
        SceneManager.sceneLoaded += OnSceneLoaded;
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
        rightControllerRayTransform = null;
        leftControllerRayTransform = null;
        disabledForMenu.Clear();
        RefreshButtonState();
        if (isMenuVisible)
        {
            FreezeGameplayBehaviours();
            RepositionMenuInFrontOfCamera();
        }
    }

    private void Update()
    {
        ReadControllerInput(
            out bool aPressedThisFrame,
            out bool gripPressedThisFrame,
            out bool triggerPressedThisFrame,
            out bool navUpPressedThisFrame,
            out bool navDownPressedThisFrame);

        if (!isMenuVisible && (gripPressedThisFrame || Input.GetKeyDown(KeyCode.M)))
        {
            ShowMenu();
        }

        if (isMenuVisible)
        {
            bool canCloseThisFrame = Time.frameCount > menuOpenedFrame;
            if ((canCloseThisFrame && gripPressedThisFrame) || Input.GetKeyDown(KeyCode.Escape))
            {
                HideMenu();
                return;
            }

            UpdateControllerRaySelection();

            if (navUpPressedThisFrame || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
            {
                MoveSelection(-1);
            }

            if (navDownPressedThisFrame || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            {
                MoveSelection(1);
            }

            if (triggerPressedThisFrame || aPressedThisFrame || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
            {
                ActivateSelectedOption();
            }

            RepositionMenuInFrontOfCamera();
        }
    }

    private void BuildUi()
    {
        EnsureEventSystemExists();

        GameObject canvasObject = new GameObject("SceneToggleCanvas");
        canvasObject.transform.SetParent(transform, false);

        menuCanvas = canvasObject.AddComponent<Canvas>();
        menuCanvas.renderMode = RenderMode.WorldSpace;
        menuCanvas.sortingOrder = 1000;

        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        CreateGazeLine(canvasObject.transform);

        GameObject panelObject = CreateUiObject("Panel", canvasObject.transform);
        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.55f);

        panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(460f, 220f);

        GameObject titleObj = CreateUiObject("Title", panelObject.transform);
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

        traditionalButton = CreateButton(panelObject.transform, "Traditional GoGo", new Vector2(12f, -58f));
        reverseButton = CreateButton(panelObject.transform, "Reverse GoGo", new Vector2(12f, -114f));
        homerButton = CreateButton(panelObject.transform, "HOMER", new Vector2(12f, -170f));
        exitButton = CreateButton(panelObject.transform, "Exit", new Vector2(12f, -226f));
        optionButtons = new[] { traditionalButton, reverseButton, homerButton, exitButton };
        optionLabels = new[]
        {
            traditionalButton.GetComponentInChildren<Text>(),
            reverseButton.GetComponentInChildren<Text>(),
            homerButton.GetComponentInChildren<Text>(),
            exitButton.GetComponentInChildren<Text>()
        };
        baseOptionLabels = new[] { "Traditional GoGo", "Reverse GoGo", "HOMER", "Exit" };

        GameObject hintObj = CreateUiObject("Hint", panelObject.transform);
        hintText = hintObj.AddComponent<Text>();
        hintText.text = "Grab (Grip): open/close | Point controller ray + Trigger: select";
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hintText.alignment = TextAnchor.MiddleCenter;
        hintText.color = new Color(0.9f, 0.9f, 0.9f, 1f);

        RectTransform hintRect = hintObj.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0f, 1f);
        hintRect.anchorMax = new Vector2(0f, 1f);
        hintRect.pivot = new Vector2(0f, 1f);
        hintRect.anchoredPosition = new Vector2(12f, -280f);
        hintRect.sizeDelta = new Vector2(436f, 36f);

        traditionalButton.onClick.AddListener(() => SwitchToTechnique(TraditionalSceneName));
        reverseButton.onClick.AddListener(() => SwitchToTechnique(ReverseSceneName));
        homerButton.onClick.AddListener(() => SwitchToTechnique(HomerSceneName));
        exitButton.onClick.AddListener(ExitApplication);

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(460f, 326f);
        panelRect.sizeDelta = new Vector2(460f, 326f);

        RefreshButtonState();
        ShowMenu();
    }

    private void CreateGazeLine(Transform parent)
    {
        GameObject lineObject = new GameObject("GazeLine");
        lineObject.transform.SetParent(parent, false);

        gazeLine = lineObject.AddComponent<LineRenderer>();
        gazeLine.positionCount = 2;
        gazeLine.useWorldSpace = true;
        gazeLine.startWidth = 0.005f;
        gazeLine.endWidth = 0.0025f;
        gazeLine.numCapVertices = 4;
        gazeLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        gazeLine.receiveShadows = false;
        gazeLine.textureMode = LineTextureMode.Stretch;

        Shader lineShader = Shader.Find("Sprites/Default");
        if (lineShader == null)
        {
            lineShader = Shader.Find("Unlit/Color");
        }

        Material lineMaterial = new Material(lineShader);
        lineMaterial.color = new Color(1f, 0.95f, 0.2f, 0.95f);
        gazeLine.material = lineMaterial;

        gazeLine.startColor = new Color(1f, 1f, 1f, 0.95f);
        gazeLine.endColor = new Color(1f, 0.9f, 0.2f, 0.95f);
        gazeLine.enabled = false;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();
        return obj;
    }

    private static Button CreateButton(Transform parent, string label, Vector2 anchoredPosition)
    {
        GameObject buttonObject = CreateUiObject(label + "Button", parent);

        int menuButtonLayer = LayerMask.NameToLayer("MenuButton");
        if (menuButtonLayer < 0)
        {
            menuButtonLayer = LayerMask.NameToLayer("UI");
        }

        if (menuButtonLayer >= 0)
        {
            buttonObject.layer = menuButtonLayer;
        }

        Image buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = new Color(0.18f, 0.18f, 0.18f, 0.95f);

        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.18f, 0.18f, 0.18f, 0.95f);
        colors.highlightedColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        colors.pressedColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        colors.disabledColor = new Color(0.08f, 0.08f, 0.08f, 0.6f);
        button.colors = colors;

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0f, 1f);
        buttonRect.anchorMax = new Vector2(0f, 1f);
        buttonRect.pivot = new Vector2(0f, 1f);
        buttonRect.anchoredPosition = anchoredPosition;
        buttonRect.sizeDelta = new Vector2(436f, 48f);

        BoxCollider buttonCollider = buttonObject.AddComponent<BoxCollider>();
        buttonCollider.size = new Vector3(buttonRect.sizeDelta.x, buttonRect.sizeDelta.y, 2f);
        buttonCollider.center = Vector3.zero;

        GameObject textObject = CreateUiObject("Text", buttonObject.transform);
        Text text = textObject.AddComponent<Text>();
        text.text = label;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return button;
    }

    private static void EnsureEventSystemExists()
    {
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null)
            return;

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
        eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        DontDestroyOnLoad(eventSystem);
    }

    private void UpdateControllerRaySelection()
    {
        if (optionButtons == null || optionButtons.Length == 0)
            return;

        if (!TryGetControllerRay(out Ray controllerRay))
        {
            SetGazeLineVisible(false);
            return;
        }

        int menuButtonLayer = LayerMask.NameToLayer("MenuButton");
        if (menuButtonLayer < 0)
        {
            menuButtonLayer = LayerMask.NameToLayer("UI");
        }

        int mask = menuButtonLayer >= 0 ? (1 << menuButtonLayer) : Physics.DefaultRaycastLayers;

        if (!Physics.Raycast(controllerRay, out RaycastHit hit, MaxControllerRayDistance, mask, QueryTriggerInteraction.Collide))
        {
            SetGazeLineVisible(true);
            SetGazeLine(controllerRay.origin, controllerRay.origin + controllerRay.direction * MaxControllerRayDistance);
            return;
        }

        SetGazeLineVisible(true);
        Vector3 lineEnd = hit.point - controllerRay.direction * RayHitVisualOffset;
        SetGazeLine(controllerRay.origin, lineEnd);

        for (int i = 0; i < optionButtons.Length; i++)
        {
            Button button = optionButtons[i];
            if (button == null || !button.interactable)
                continue;

            Transform hitTransform = hit.collider != null ? hit.collider.transform : null;
            if (hitTransform == button.transform || hitTransform.IsChildOf(button.transform))
            {
                if (i != selectedOption)
                {
                    SetSelectedOption(i);
                }

                return;
            }
        }
    }

    private void SetGazeLineVisible(bool visible)
    {
        if (gazeLine != null)
        {
            gazeLine.enabled = visible && isMenuVisible;
        }
    }

    private void SetGazeLine(Vector3 start, Vector3 end)
    {
        if (gazeLine == null)
            return;

        gazeLine.SetPosition(0, start);
        gazeLine.SetPosition(1, end);
    }

    private bool TryGetControllerRay(out Ray controllerRay)
    {
        EnsureControllerRayTransforms();

        if (rightControllerRayTransform != null)
        {
            controllerRay = new Ray(rightControllerRayTransform.position, rightControllerRayTransform.forward);
            return true;
        }

        if (leftControllerRayTransform != null)
        {
            controllerRay = new Ray(leftControllerRayTransform.position, leftControllerRayTransform.forward);
            return true;
        }

        InputDevice rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (rightHand.isValid
            && rightHand.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 rightPos)
            && rightHand.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rightRot))
        {
            controllerRay = new Ray(rightPos, rightRot * Vector3.forward);
            return true;
        }

        InputDevice leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (leftHand.isValid
            && leftHand.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 leftPos)
            && leftHand.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion leftRot))
        {
            controllerRay = new Ray(leftPos, leftRot * Vector3.forward);
            return true;
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            controllerRay = new Ray(cam.transform.position, cam.transform.forward);
            return true;
        }

        controllerRay = default;
        return false;
    }

    private void EnsureControllerRayTransforms()
    {
        if (rightControllerRayTransform == null)
        {
            rightControllerRayTransform = FindControllerTransform("RightHand Controller", "RightHandController", "Right Hand", "RightHand");
        }

        if (leftControllerRayTransform == null)
        {
            leftControllerRayTransform = FindControllerTransform("LeftHand Controller", "LeftHandController", "Left Hand", "LeftHand");
        }
    }

    private static Transform FindControllerTransform(params string[] candidateNames)
    {
        for (int i = 0; i < candidateNames.Length; i++)
        {
            GameObject direct = GameObject.Find(candidateNames[i]);
            if (direct != null)
            {
                return direct.transform;
            }
        }

        Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform t = allTransforms[i];
            if (t == null)
                continue;

            for (int n = 0; n < candidateNames.Length; n++)
            {
                if (string.Equals(t.name, candidateNames[n], System.StringComparison.OrdinalIgnoreCase))
                {
                    return t;
                }
            }
        }

        return null;
    }

    private void SwitchToTechnique(string sceneName)
    {
        HideMenu();

        if (SceneManager.GetActiveScene().name == sceneName)
        {
            RefreshButtonState();
            return;
        }

        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    private void ShowMenu()
    {
        if (isMenuVisible)
            return;

        isMenuVisible = true;
        menuOpenedFrame = Time.frameCount;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        FreezeGameplayBehaviours();
        if (menuCanvas != null)
        {
            menuCanvas.enabled = true;
            RepositionMenuInFrontOfCamera();
        }
        SetGazeLineVisible(true);
        RefreshButtonState();
        SetSelectedOption(GetDefaultSelectedOption());
    }

    private void HideMenu()
    {
        if (!isMenuVisible)
            return;

        isMenuVisible = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        RestoreGameplayBehaviours();
        if (menuCanvas != null)
        {
            menuCanvas.enabled = false;
        }
        SetGazeLineVisible(false);
    }

    private void FreezeGameplayBehaviours()
    {
        disabledForMenu.Clear();
        Behaviour[] behaviours = FindObjectsByType<Behaviour>(FindObjectsSortMode.None);
        for (int i = 0; i < behaviours.Length; i++)
        {
            Behaviour behaviour = behaviours[i];
            if (behaviour == null || !behaviour.enabled)
                continue;

            if (behaviour == this)
                continue;

            if (menuCanvas != null && behaviour.transform.IsChildOf(menuCanvas.transform))
                continue;

            string typeName = behaviour.GetType().Name;
            if (!PausedBehaviourTypeNames.Contains(typeName))
                continue;

            behaviour.enabled = false;
            disabledForMenu.Add(behaviour);
        }
    }

    private void RestoreGameplayBehaviours()
    {
        for (int i = 0; i < disabledForMenu.Count; i++)
        {
            Behaviour behaviour = disabledForMenu[i];
            if (behaviour != null)
            {
                behaviour.enabled = true;
            }
        }
        disabledForMenu.Clear();
    }

    private void RepositionMenuInFrontOfCamera()
    {
        if (menuCanvas == null)
            return;

        Camera cam = Camera.main;
        if (cam == null)
            return;

        Transform canvasTransform = menuCanvas.transform;
        Vector3 forward = cam.transform.forward;
        Vector3 up = cam.transform.up;

        canvasTransform.position = cam.transform.position + forward * 1.2f;
        canvasTransform.rotation = Quaternion.LookRotation(forward, up);
        canvasTransform.localScale = Vector3.one * 0.0018f;
    }

    private void ReadControllerInput(
        out bool aPressedThisFrame,
        out bool gripPressedThisFrame,
        out bool triggerPressedThisFrame,
        out bool navUpPressedThisFrame,
        out bool navDownPressedThisFrame)
    {
        bool aPressed = false;
        bool gripPressed = false;
        bool triggerPressed = false;
        bool navUpPressed = false;
        bool navDownPressed = false;

        InputDevice rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (rightHand.isValid)
        {
            rightHand.TryGetFeatureValue(CommonUsages.primaryButton, out aPressed);
            rightHand.TryGetFeatureValue(CommonUsages.gripButton, out bool rightGripPressed);
            gripPressed |= rightGripPressed;
            rightHand.TryGetFeatureValue(CommonUsages.triggerButton, out triggerPressed);

            Vector2 axis;
            if (rightHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis))
            {
                navUpPressed = axis.y > 0.6f;
                navDownPressed = axis.y < -0.6f;
            }
        }

        InputDevice leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (leftHand.isValid)
        {
            leftHand.TryGetFeatureValue(CommonUsages.gripButton, out bool leftGripPressed);
            gripPressed |= leftGripPressed;
        }

        aPressedThisFrame = aPressed && !previousAPressed;
        gripPressedThisFrame = gripPressed && !previousGripPressed;
        triggerPressedThisFrame = triggerPressed && !previousTriggerPressed;
        navUpPressedThisFrame = navUpPressed && !previousNavUp;
        navDownPressedThisFrame = navDownPressed && !previousNavDown;

        previousAPressed = aPressed;
        previousGripPressed = gripPressed;
        previousTriggerPressed = triggerPressed;
        previousNavUp = navUpPressed;
        previousNavDown = navDownPressed;
    }

    private int GetDefaultSelectedOption()
    {
        string active = SceneManager.GetActiveScene().name;
        if (active == TraditionalSceneName)
            return 0;
        if (active == ReverseSceneName)
            return 1;
        if (active == HomerSceneName)
            return 2;
        return 0;
    }

    private void MoveSelection(int delta)
    {
        if (optionButtons == null || optionButtons.Length == 0)
            return;

        int next = selectedOption;
        for (int i = 0; i < optionButtons.Length; i++)
        {
            next = (next + delta + optionButtons.Length) % optionButtons.Length;
            if (optionButtons[next] != null && optionButtons[next].interactable)
            {
                SetSelectedOption(next);
                return;
            }
        }
    }

    private void ActivateSelectedOption()
    {
        if (optionButtons == null || selectedOption < 0 || selectedOption >= optionButtons.Length)
            return;

        Button selectedButton = optionButtons[selectedOption];
        if (selectedButton != null && selectedButton.interactable)
        {
            selectedButton.onClick.Invoke();
        }
    }

    private void SetSelectedOption(int index)
    {
        if (optionButtons == null || optionButtons.Length == 0)
            return;

        selectedOption = Mathf.Clamp(index, 0, optionButtons.Length - 1);
        UpdateButtonVisuals();
    }

    private void UpdateButtonVisuals()
    {
        if (optionButtons == null)
            return;

        for (int i = 0; i < optionButtons.Length; i++)
        {
            Button button = optionButtons[i];
            if (button == null)
                continue;

            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = (i == selectedOption && button.interactable) ? selectedButtonColor : normalButtonColor;
            }

            if (optionLabels != null && i < optionLabels.Length && optionLabels[i] != null)
            {
                bool selected = i == selectedOption && button.interactable;
                optionLabels[i].color = selected ? selectedLabelColor : normalLabelColor;
                optionLabels[i].text = selected
                    ? "> " + baseOptionLabels[i] + " <"
                    : baseOptionLabels[i];
            }
        }
    }

    private static void ExitApplication()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void RefreshButtonState()
    {
        if (traditionalButton == null || reverseButton == null || homerButton == null)
            return;

        string active = SceneManager.GetActiveScene().name;
        traditionalButton.interactable = active != TraditionalSceneName;
        reverseButton.interactable = active != ReverseSceneName;
        homerButton.interactable = active != HomerSceneName;
        SetSelectedOption(GetDefaultSelectedOption());
    }
}
