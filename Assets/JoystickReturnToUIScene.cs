using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Pressing grip in non-UI scenes returns to the UI scene.
/// </summary>
public class JoystickReturnToUIScene : MonoBehaviour
{
    private const string UiSceneName = "UI";
    private const string UiScenePath = "Assets/UI/UI.unity";

    private static JoystickReturnToUIScene instance;
    private bool justLoadedUI = false;
    private InputAction leftGripPressedAction;
    private InputAction rightGripPressedAction;
    private float leftGripHoldStart = -1f;
    private float rightGripHoldStart = -1f;

    private const float GripHoldThreshold = 0.85f;
    private const float GripHoldSecondsToReturn = 0.2f;

    // Auto-spawn disabled: each study script now handles grip-return directly via GripReturnPressed().
    // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
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

        leftGripPressedAction = new InputAction(
            name: "LeftGripPressed",
            type: InputActionType.Button,
            binding: "<XRController>{LeftHand}/gripPressed");
        leftGripPressedAction.AddBinding("<XRController>{LeftHand}/grip")
            .WithInteraction("press(pressPoint=0.65)");

        rightGripPressedAction = new InputAction(
            name: "RightGripPressed",
            type: InputActionType.Button,
            binding: "<XRController>{RightHand}/gripPressed");
        rightGripPressedAction.AddBinding("<XRController>{RightHand}/grip")
            .WithInteraction("press(pressPoint=0.65)");

        leftGripPressedAction.Enable();
        rightGripPressedAction.Enable();
    }

    private void OnDestroy()
    {
        if (leftGripPressedAction != null)
        {
            leftGripPressedAction.Disable();
            leftGripPressedAction.Dispose();
            leftGripPressedAction = null;
        }

        if (rightGripPressedAction != null)
        {
            rightGripPressedAction.Disable();
            rightGripPressedAction.Dispose();
            rightGripPressedAction = null;
        }
    }

    private void Update()
    {
        string activeScene = SceneManager.GetActiveScene().name;
        if (activeScene == UiSceneName || SceneManager.GetActiveScene().path == UiScenePath)
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

        bool gripPressedThisFrame = (leftGripPressedAction != null && leftGripPressedAction.WasPressedThisFrame())
            || (rightGripPressedAction != null && rightGripPressedAction.WasPressedThisFrame())
            || WasGripPressedThisFrameInputSystem();

        // Hold fallback in case some runtimes never emit a clean pressed edge.
        bool gripHeldLongEnough = IsGripHeldLongEnough(XRNode.LeftHand, ref leftGripHoldStart)
            || IsGripHeldLongEnough(XRNode.RightHand, ref rightGripHoldStart);

        if (gripPressedThisFrame || gripHeldLongEnough)
        {
            justLoadedUI = true;
            if (Application.CanStreamedLevelBeLoaded(UiScenePath))
            {
                SceneManager.LoadScene(UiScenePath, LoadSceneMode.Single);
            }
            else
            {
                SceneManager.LoadScene(UiSceneName, LoadSceneMode.Single);
            }
            return;
        }

        // Final fallback for runtimes where Input System controls are unavailable.
        if (ReadGrip(XRNode.LeftHand) || ReadGrip(XRNode.RightHand))
        {
            justLoadedUI = true;
            if (Application.CanStreamedLevelBeLoaded(UiScenePath))
            {
                SceneManager.LoadScene(UiScenePath, LoadSceneMode.Single);
            }
            else
            {
                SceneManager.LoadScene(UiSceneName, LoadSceneMode.Single);
            }
        }
    }

    private static bool WasGripPressedThisFrameInputSystem()
    {
        UnityEngine.InputSystem.InputDevice leftHand = InputSystem.GetDevice("LeftHand");
        if (leftHand != null)
        {
            ButtonControl leftGripPressed = leftHand.TryGetChildControl<ButtonControl>("gripPressed");
            if (leftGripPressed != null && leftGripPressed.wasPressedThisFrame)
            {
                return true;
            }

            AxisControl leftGrip = leftHand.TryGetChildControl<AxisControl>("grip");
            if (leftGrip != null && leftGrip.ReadValue() > 0.65f)
            {
                return true;
            }
        }

        UnityEngine.InputSystem.InputDevice rightHand = InputSystem.GetDevice("RightHand");
        if (rightHand != null)
        {
            ButtonControl rightGripPressed = rightHand.TryGetChildControl<ButtonControl>("gripPressed");
            if (rightGripPressed != null && rightGripPressed.wasPressedThisFrame)
            {
                return true;
            }

            AxisControl rightGrip = rightHand.TryGetChildControl<AxisControl>("grip");
            if (rightGrip != null && rightGrip.ReadValue() > 0.65f)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReadGrip(XRNode hand)
    {
        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(hand);
        if (!device.isValid)
            return false;

        bool grip;
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out grip))
            return grip;

        float gripAxis;
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out gripAxis))
            return gripAxis > 0.55f;

        bool axisClick;
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out axisClick))
            return axisClick;

        return false;
    }

    private static bool ReadGripAxis(XRNode hand)
    {
        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(hand);
        if (!device.isValid)
            return false;

        float gripAxis;
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out gripAxis))
            return gripAxis >= GripHoldThreshold;

        bool gripButton;
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out gripButton))
            return gripButton;

        return false;
    }

    private static bool IsGripHeldLongEnough(XRNode hand, ref float holdStartTime)
    {
        if (ReadGripAxis(hand))
        {
            if (holdStartTime < 0f)
            {
                holdStartTime = Time.unscaledTime;
                return false;
            }

            return (Time.unscaledTime - holdStartTime) >= GripHoldSecondsToReturn;
        }

        holdStartTime = -1f;
        return false;
    }

    private void ResetInputState()
    {
        leftGripHoldStart = -1f;
        rightGripHoldStart = -1f;
    }
}
