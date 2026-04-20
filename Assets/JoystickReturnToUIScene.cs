using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

/// <summary>
/// Pressing joystick click in non-UI scenes returns to the UI scene.
/// </summary>
public class JoystickReturnToUIScene : MonoBehaviour
{
    private const string UiSceneName = "UI";

    private static JoystickReturnToUIScene instance;
    private bool previousLeftStickClick;
    private bool previousRightStickClick;
    private bool justLoadedUI = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (instance != null)
            return;

        GameObject go = new GameObject("JoystickReturnToUIScene");
        instance = go.AddComponent<JoystickReturnToUIScene>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void Update()
    {
        string activeScene = SceneManager.GetActiveScene().name;
        if (activeScene == UiSceneName)
        {
            // Just loaded UI, reset input state and skip this frame to avoid double-trigger
            if (justLoadedUI)
            {
                justLoadedUI = false;
                ResetInputState();
                return;
            }
            ResetInputState();
            return;
        }

        // We're in a game scene, listen for stick click
        bool leftStickClick = ReadStickClick(XRNode.LeftHand);
        bool rightStickClick = ReadStickClick(XRNode.RightHand);

        bool leftPressedThisFrame = leftStickClick && !previousLeftStickClick;
        bool rightPressedThisFrame = rightStickClick && !previousRightStickClick;

        previousLeftStickClick = leftStickClick;
        previousRightStickClick = rightStickClick;

        if (leftPressedThisFrame || rightPressedThisFrame)
        {
            justLoadedUI = true;
            SceneManager.LoadScene(UiSceneName, LoadSceneMode.Single);
        }
    }

    private static bool ReadStickClick(XRNode hand)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(hand);
        if (!device.isValid)
            return false;

        bool stickClick;
        if (device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out stickClick))
            return stickClick;

        return false;
    }

    private void ResetInputState()
    {
        previousLeftStickClick = false;
        previousRightStickClick = false;
    }
}
