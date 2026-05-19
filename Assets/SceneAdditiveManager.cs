using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Central utility for additive scene switching.
///
/// Only one content scene is loaded at a time alongside the always-present XR_Persistent
/// scene. Switching: asynchronously unloads the old scene, then loads the new one
/// additively and sets it as the active scene (so lighting, skybox, etc. come from it).
///
/// Usage from any MonoBehaviour:
///   SceneAdditiveManager.SwitchTo(this, "UI");
///   SceneAdditiveManager.SwitchTo(this, "Assets/UI/UI.unity"); // path also accepted
///
/// Usage from a static context (e.g. GripReturnToUI):
///   SceneAdditiveManager.SwitchTo("UI");
/// </summary>
public static class SceneAdditiveManager
{
    // Always stored as a scene name (not a path) so GetSceneByName() works reliably.
    private static string _currentContentScene;
    private static bool _switching;

    // --- Public API ---

    /// <summary>Switches to <paramref name="targetScene"/> using <paramref name="host"/> to run the coroutine.</summary>
    public static void SwitchTo(MonoBehaviour host, string targetScene)
    {
        string sceneName = SceneNameFrom(targetScene);
        if (_switching) return;
        if (_currentContentScene == sceneName) return;
        host.StartCoroutine(DoSwitch(targetScene, sceneName));
    }

    /// <summary>
    /// Switches to <paramref name="targetScene"/> by finding an active MonoBehaviour to host
    /// the coroutine. Prefer the overload that takes a host when one is available.
    /// </summary>
    public static void SwitchTo(string targetScene)
    {
        string sceneName = SceneNameFrom(targetScene);
        if (_switching) return;
        if (_currentContentScene == sceneName) return;

        // Find any live MonoBehaviour to host the coroutine.
        // ControllerRaySceneMenu (DontDestroyOnLoad) is always available after first load.
        MonoBehaviour host = Object.FindFirstObjectByType<ControllerRaySceneMenu>();
        if (host == null) host = Object.FindFirstObjectByType<MonoBehaviour>();
        if (host == null) { Debug.LogError("[SceneAdditiveManager] No MonoBehaviour found to host coroutine."); return; }

        host.StartCoroutine(DoSwitch(targetScene, sceneName));
    }

    // --- Internal ---

    /// <summary>
    /// Strips directory and .unity extension so both "UI" and "Assets/UI/UI.unity"
    /// map to the same key "UI". SceneManager.GetSceneByName only accepts bare names.
    /// </summary>
    private static string SceneNameFrom(string nameOrPath)
    {
        if (string.IsNullOrEmpty(nameOrPath)) return nameOrPath;
        // If it contains a slash or a dot it is a path — extract just the filename stem.
        if (nameOrPath.IndexOf('/') >= 0 || nameOrPath.IndexOf('.') >= 0)
            return System.IO.Path.GetFileNameWithoutExtension(nameOrPath);
        return nameOrPath;
    }

    private static IEnumerator DoSwitch(string targetScene, string targetSceneName)
    {
        _switching = true;

        // Unload the previous content scene if one is loaded.
        if (!string.IsNullOrEmpty(_currentContentScene))
        {
            Scene prev = SceneManager.GetSceneByName(_currentContentScene);
            if (prev.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(prev);
                while (!unload.isDone) yield return null;
            }
        }

        // Load the new scene additively (accepts both names and paths).
        bool canLoad = Application.CanStreamedLevelBeLoaded(targetScene);
        if (!canLoad)
        {
            // Fallback: try the bare name in case a path was passed and path lookup failed.
            canLoad = Application.CanStreamedLevelBeLoaded(targetSceneName);
            if (canLoad) targetScene = targetSceneName;
        }

        if (!canLoad)
        {
            Debug.LogError($"[SceneAdditiveManager] Scene '{targetScene}' is not in Build Settings.");
            _switching = false;
            yield break;
        }

        AsyncOperation load = SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Additive);
        while (!load.isDone) yield return null;

        // Make the new scene active so its lighting, skybox, and physics settings apply.
        Scene newScene = SceneManager.GetSceneByName(targetSceneName);
        if (newScene.isLoaded)
            SceneManager.SetActiveScene(newScene);

        _currentContentScene = targetSceneName;
        _switching = false;

        Debug.Log($"[SceneAdditiveManager] Switched to '{targetSceneName}'.");
    }
}
