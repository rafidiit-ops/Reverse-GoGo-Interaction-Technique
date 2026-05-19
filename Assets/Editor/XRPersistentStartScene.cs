// Automatically sets XR_Persistent.unity as the Play Mode Start Scene so the XR rig
// (DontDestroyOnLoad + XRRigBootstrapper) always loads first, regardless of which
// scene the developer currently has open in the editor.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class XRPersistentStartScene
{
    static XRPersistentStartScene()
    {
        const string path = "Assets/XR_Persistent.unity";
        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        if (scene == null)
        {
            Debug.LogWarning("[XRPersistentStartScene] Could not find Assets/XR_Persistent.unity. Play Mode Start Scene not changed.");
            return;
        }

        if (EditorSceneManager.playModeStartScene != scene)
        {
            EditorSceneManager.playModeStartScene = scene;
            Debug.Log("[XRPersistentStartScene] Play Mode Start Scene automatically set to XR_Persistent.unity.");
        }
    }
}
