using UnityEngine;

/// <summary>
/// Logs the initial position of Red_Phantom and RedCube, and their distance from the user (HMD), once at start.
/// </summary>
public class DistanceMeasurer : MonoBehaviour
{
    [Header("References (auto-found by name if left empty)")]
    public Transform userTransform;      // Defaults to Camera.main (HMD)
    public Transform redPhantom;         // GameObject named "Red_Phantom"
    public Transform redCube;            // GameObject named "RedCube"

    void Start()
    {
        if (userTransform == null && Camera.main != null)
        {
            userTransform = Camera.main.transform;
        }

        if (redPhantom == null)
        {
            redPhantom = FindIncludingInactive("Red_Phantom");
        }

        if (redCube == null)
        {
            redCube = FindIncludingInactive("RedCube");
        }

        if (userTransform == null)
        {
            Debug.LogWarning("[DistanceMeasurer] User transform (Camera.main) not found.");
            return;
        }

        if (redPhantom != null)
        {
            float phantomDistance = Vector3.Distance(userTransform.position, redPhantom.position);
            Debug.Log($"[DistanceMeasurer] Red_Phantom initial position: {redPhantom.position} | Distance from user: {phantomDistance:F2}m");
        }
        else
        {
            Debug.LogWarning("[DistanceMeasurer] Red_Phantom not found.");
        }

        if (redCube != null)
        {
            float cubeDistance = Vector3.Distance(userTransform.position, redCube.position);
            Debug.Log($"[DistanceMeasurer] RedCube initial position: {redCube.position} | Distance from user: {cubeDistance:F2}m");
        }
        else
        {
            Debug.LogWarning("[DistanceMeasurer] RedCube not found.");
        }
    }

    // GameObject.Find skips inactive objects; these targets can start disabled, so search the full hierarchy.
    private static Transform FindIncludingInactive(string name)
    {
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }
        }
        return null;
    }
}
