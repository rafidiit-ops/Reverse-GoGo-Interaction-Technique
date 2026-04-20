using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// Persistent singleton that enables XR joystick navigation + trigger select for the User Study UI scene.
/// Reinitializes whenever the UI scene is loaded, allowing proper menu operation after returning from game scenes.
/// </summary>
public class UIStudyMenuController : MonoBehaviour
{
    private const string UiSceneName = "UI";
    private const string TraditionalSceneName = "TraditionalGoGoSampleScene";
    private const string ReverseSceneName = "ReverseGoGo SampleScene";

    private readonly List<Button> orderedButtons = new List<Button>();
    private int selectedIndex;

    private bool previousNavUp;
    private bool previousNavDown;
    private bool previousTrigger;

    private readonly Color normalButtonColor = new Color(0.18f, 0.18f, 0.18f, 0.95f);
    private readonly Color selectedButtonColor = new Color(0.12f, 0.4f, 0.18f, 0.98f);
    private readonly Color normalLabelColor = new Color(0.9f, 0.9f, 0.9f, 1f);
    private readonly Color selectedLabelColor = new Color(1f, 1f, 0.65f, 1f);

    private static UIStudyMenuController instance;
    private bool justReturnedToUI = false;
    private bool isMenuReady;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureController()
    {
        if (instance != null)
            return;

        Scene active = SceneManager.GetActiveScene();
        if (active.name != UiSceneName)
            return;

        // Create singleton instance
        GameObject controllerObject = new GameObject("UIStudyMenuController");
        controllerObject.AddComponent<UIStudyMenuController>();
    }

    private void Awake()
    {
        // Singleton pattern: destroy duplicate instances
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        // Listen for whenever any scene loads
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        if (SceneManager.GetActiveScene().name == UiSceneName)
            InitializeMenu();
    }

    /// <summary>
    /// Called whenever a scene is loaded. Reinitializes the menu when UI scene is loaded.
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == UiSceneName)
        {
            InitializeMenu();
            justReturnedToUI = true; // Skip input next frame to avoid carryover
        }
        else
        {
            isMenuReady = false;
            orderedButtons.Clear();
        }
    }

    private void InitializeMenu()
    {
        if (!CacheButtons())
        {
            isMenuReady = false;
            return;
        }

        RebindActions();
        SetSelected(0);
        ResetInputState();
        isMenuReady = true;
    }

    private void Update()
    {
        // Skip input this frame right after returning to UI to avoid controller input carryover
        if (justReturnedToUI)
        {
            justReturnedToUI = false;
            return;
        }

        // Only process input when in UI scene
        if (SceneManager.GetActiveScene().name != UiSceneName)
            return;

        if (!isMenuReady)
        {
            InitializeMenu();
            if (!isMenuReady)
                return;
        }

        ReadControllerInput(out bool navUpPressedThisFrame, out bool navDownPressedThisFrame, out bool triggerPressedThisFrame);

        if (navUpPressedThisFrame)
            MoveSelection(-1);

        if (navDownPressedThisFrame)
            MoveSelection(1);

        if (triggerPressedThisFrame)
            ActivateSelected();
    }

    private bool CacheButtons()
    {
        orderedButtons.Clear();

        Button traditional = FindButtonByLabel("Traditional GoGo");
        Button reverse = FindButtonByLabel("Reverse GoGo");
        Button virtualHand = FindButtonByLabel("Virtual Hand");
        Button quit = FindButtonByLabel("Quit");

        if (traditional == null || reverse == null || virtualHand == null || quit == null)
            return false;

        orderedButtons.Add(traditional);
        orderedButtons.Add(reverse);
        orderedButtons.Add(virtualHand);
        orderedButtons.Add(quit);
        return true;
    }

    private static Button FindButtonByLabel(string label)
    {
        Button[] buttons = FindObjectsByType<Button>(FindObjectsSortMode.None);
        for (int i = 0; i < buttons.Length; i++)
        {
            Text text = buttons[i].GetComponentInChildren<Text>();
            if (text == null)
                continue;

            string value = text.text == null ? string.Empty : text.text.Trim();
            if (value == label)
                return buttons[i];
        }

        return null;
    }

    private void RebindActions()
    {
        if (orderedButtons.Count < 4)
            return;

        orderedButtons[0].onClick.RemoveAllListeners();
        orderedButtons[0].onClick.AddListener(() => SceneManager.LoadScene(TraditionalSceneName, LoadSceneMode.Single));

        orderedButtons[1].onClick.RemoveAllListeners();
        orderedButtons[1].onClick.AddListener(() => SceneManager.LoadScene(ReverseSceneName, LoadSceneMode.Single));

        // Virtual hand uses the traditional GoGo scene where virtual-hand interaction is configured.
        orderedButtons[2].onClick.RemoveAllListeners();
        orderedButtons[2].onClick.AddListener(() => SceneManager.LoadScene(TraditionalSceneName, LoadSceneMode.Single));

        orderedButtons[3].onClick.RemoveAllListeners();
        orderedButtons[3].onClick.AddListener(QuitApplication);
    }

    private void ReadControllerInput(out bool navUpPressedThisFrame, out bool navDownPressedThisFrame, out bool triggerPressedThisFrame)
    {
        bool navUp = false;
        bool navDown = false;
        bool triggerPressed = false;

        InputDevice rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (rightHand.isValid)
        {
            Vector2 axis;
            if (rightHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis))
            {
                navUp = axis.y > 0.6f;
                navDown = axis.y < -0.6f;
            }

            rightHand.TryGetFeatureValue(CommonUsages.triggerButton, out triggerPressed);
        }

        navUpPressedThisFrame = navUp && !previousNavUp;
        navDownPressedThisFrame = navDown && !previousNavDown;
        triggerPressedThisFrame = triggerPressed && !previousTrigger;

        previousNavUp = navUp;
        previousNavDown = navDown;
        previousTrigger = triggerPressed;
    }

    private void MoveSelection(int delta)
    {
        if (orderedButtons.Count == 0)
            return;

        int next = (selectedIndex + delta + orderedButtons.Count) % orderedButtons.Count;
        SetSelected(next);
    }

    private void SetSelected(int index)
    {
        if (orderedButtons.Count == 0)
        {
            selectedIndex = -1;
            return;
        }

        selectedIndex = Mathf.Clamp(index, 0, orderedButtons.Count - 1);

        for (int i = 0; i < orderedButtons.Count; i++)
        {
            Button button = orderedButtons[i];
            Image image = button.GetComponent<Image>();
            Text label = button.GetComponentInChildren<Text>();
            bool selected = i == selectedIndex;

            if (image != null)
                image.color = selected ? selectedButtonColor : normalButtonColor;

            if (label != null)
                label.color = selected ? selectedLabelColor : normalLabelColor;
        }
    }

    private void ActivateSelected()
    {
        if (orderedButtons.Count == 0)
            return;

        if (selectedIndex < 0 || selectedIndex >= orderedButtons.Count)
            return;

        Button selected = orderedButtons[selectedIndex];
        if (selected == null || !selected.interactable)
            return;

        selected.onClick.Invoke();
    }

    private void ResetInputState()
    {
        previousNavUp = false;
        previousNavDown = false;
        previousTrigger = false;
    }

    private static void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
