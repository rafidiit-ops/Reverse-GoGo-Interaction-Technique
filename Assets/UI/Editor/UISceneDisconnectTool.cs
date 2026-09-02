#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class UISceneDisconnectTool
{
    private const string UiScenePath = "Assets/UI/UI.unity";
    private const string UiAssetsRoot = "Assets/UI";
    private const string UiMaterialsFolder = "Assets/UI/Materials";

    [MenuItem("Tools/UI Scene/Disconnect UI Scene From ReverseGoGo")]
    private static void DisconnectUiSceneFromReverseGoGo()
    {
        if (!File.Exists(UiScenePath))
        {
            Debug.LogError($"[UI Disconnect] Scene not found: {UiScenePath}");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[UI Disconnect] Canceled by user.");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(UiScenePath, OpenSceneMode.Single);

        int unpackedCount = UnpackAllOutermostPrefabInstances(scene);
        int duplicatedMaterials = DuplicateExternalMaterialsIntoUiFolder(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[UI Disconnect] Completed. Unpacked prefab instances: {unpackedCount}. Duplicated materials: {duplicatedMaterials}. Scene saved: {UiScenePath}");
    }

    private static int UnpackAllOutermostPrefabInstances(Scene scene)
    {
        List<GameObject> prefabRoots = new List<GameObject>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject go = transforms[i].gameObject;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(go))
                {
                    continue;
                }

                Transform parent = go.transform.parent;
                if (parent != null && PrefabUtility.IsPartOfPrefabInstance(parent.gameObject))
                {
                    continue;
                }

                prefabRoots.Add(go);
            }
        }

        for (int i = 0; i < prefabRoots.Count; i++)
        {
            PrefabUtility.UnpackPrefabInstance(prefabRoots[i], PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        return prefabRoots.Count;
    }

    private static int DuplicateExternalMaterialsIntoUiFolder(Scene scene)
    {
        EnsureFolderExists(UiMaterialsFolder);

        Dictionary<Material, Material> materialMap = new Dictionary<Material, Material>();
        int duplicatedCount = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;

                for (int m = 0; m < materials.Length; m++)
                {
                    Material original = materials[m];
                    if (original == null)
                    {
                        continue;
                    }

                    string originalPath = AssetDatabase.GetAssetPath(original);
                    if (string.IsNullOrEmpty(originalPath))
                    {
                        continue;
                    }

                    if (originalPath.StartsWith(UiAssetsRoot))
                    {
                        continue;
                    }

                    if (!materialMap.TryGetValue(original, out Material uiCopy))
                    {
                        uiCopy = Object.Instantiate(original);
                        uiCopy.name = original.name + "_UI";

                        string safeName = SanitizeFileName(uiCopy.name);
                        string newPath = AssetDatabase.GenerateUniqueAssetPath($"{UiMaterialsFolder}/{safeName}.mat");
                        AssetDatabase.CreateAsset(uiCopy, newPath);

                        materialMap[original] = uiCopy;
                        duplicatedCount++;
                    }

                    materials[m] = uiCopy;
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                    EditorUtility.SetDirty(renderer);
                }
            }
        }

        return duplicatedCount;
    }

    private static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string[] parts = folderPath.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
        {
            value = value.Replace(invalid[i], '_');
        }

        return value;
    }
}
#endif
