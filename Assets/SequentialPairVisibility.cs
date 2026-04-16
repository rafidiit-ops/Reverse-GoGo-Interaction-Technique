using System;
using System.Collections.Generic;
using UnityEngine;

public class SequentialPairVisibility : MonoBehaviour
{
    [Header("Ordered Pairs")]
    public GameObject[] orderedObjects; // e.g. Red, Green, Blue, Yellow
    public TargetCubeTrigger[] orderedTargets; // matching order with orderedObjects

    [Header("Auto Discovery")]
    public bool autoDiscoverPairs = true;
    public Transform objectsRoot;
    public Transform targetsRoot;

    [Header("Behavior")]
    public bool activateOnStart = true;
    public bool showAllAtStart = false;
    public bool enableProximityFallback = true;
    public float completeDistanceMeters = 0.0000001f;

    private int currentPairIndex = 0;
    private bool isSubscribed = false;
    private Vector3[] initialObjectPositions;
    private Quaternion[] initialObjectRotations;
    private float _pairActivationTime = -1f;   // Time.time when the current pair was last activated
    private const float PairActivationCooldown = 0.5f;  // seconds to ignore completion after activation
    private VirtualHandAttach virtualHandAttach;  // Reference to release the virtual hand when pair completes
    private static readonly string[] ColorOrder = { "red", "blue", "green", "yellow" };

    private void Start()
    {
        if (autoDiscoverPairs || !HasAssignedPairs())
        {
            AutoDiscoverPairs();
        }

        DisableBubbleTargets();
        CacheInitialObjectPoses();

        // Find VirtualHandAttach to release hand when pair completes
        virtualHandAttach = FindFirstObjectByType<VirtualHandAttach>();

        SubscribeToTargets();

        if (activateOnStart)
        {
            if (showAllAtStart)
            {
                ShowAllPairs();
            }
            else
            {
                ResetSequence();
            }
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromTargets();
    }

    private void Update()
    {
        if (!enableProximityFallback)
        {
            return;
        }

        int count = GetPairCount();
        if (currentPairIndex < 0 || currentPairIndex >= count)
        {
            return;
        }

        GameObject currentObject = orderedObjects[currentPairIndex];
        TargetCubeTrigger currentTarget = orderedTargets[currentPairIndex];

        if (currentObject == null || currentTarget == null)
        {
            return;
        }

        if (!currentObject.activeInHierarchy || !currentTarget.gameObject.activeInHierarchy)
        {
            return;
        }

        // Skip completion checks during activation cooldown
        if (_pairActivationTime >= 0f && Time.time - _pairActivationTime < PairActivationCooldown)
        {
            return;
        }

        if (IsObjectPlacedInsideTarget(currentObject, currentTarget.gameObject))
        {
            CompleteCurrentPair(currentObject);
            Debug.Log("SequentialPairVisibility completed by inside-target validation.");
        }
    }

    public void ResetSequence()
    {
        currentPairIndex = 0;

        int count = GetPairCount();
        for (int i = 0; i < count; i++)
        {
            SetPairActive(i, false);

            if (orderedTargets[i] != null)
            {
                orderedTargets[i].ResetVisual();
            }
        }

        if (count > 0)
        {
            SetPairActive(0, true);
        }
    }

    public void ShowAllPairs()
    {
        int count = GetPairCount();
        for (int i = 0; i < count; i++)
        {
            SetPairActive(i, true);
        }
    }

    private void HandleObjectPlaced(GameObject placedObject, string bubbleColor, bool isCorrect)
    {
        int count = GetPairCount();
        if (currentPairIndex < 0 || currentPairIndex >= count)
        {
            return;
        }

        GameObject currentObject = orderedObjects[currentPairIndex];
        TargetCubeTrigger currentTarget = orderedTargets[currentPairIndex];

        if (currentObject == null || currentTarget == null)
        {
            return;
        }

        bool isCurrentTarget = currentTarget.gameObject.activeInHierarchy
            && string.Equals(currentTarget.targetColor, bubbleColor, StringComparison.OrdinalIgnoreCase);
        bool isCurrentObject = IsSameObjectFamily(placedObject, currentObject);

        if (!isCurrentTarget || !isCurrentObject)
        {
            return;
        }

        // Ignore events fired immediately after pair activation
        if (_pairActivationTime >= 0f && Time.time - _pairActivationTime < PairActivationCooldown)
        {
            return;
        }

        if (!IsObjectPlacedInsideTarget(currentObject, currentTarget.gameObject))
        {
            return;
        }

        CompleteCurrentPair(placedObject);
    }

    private void CompleteCurrentPair(GameObject observedPlacedObject = null)
    {
        int completedIndex = currentPairIndex;
        int pairCount = GetPairCount();

        GameObject configuredObject = orderedObjects != null && currentPairIndex >= 0 && currentPairIndex < orderedObjects.Length
            ? orderedObjects[currentPairIndex]
            : null;

        if (configuredObject != null)
        {
            configuredObject.SetActive(false);
        }
        else if (observedPlacedObject != null)
        {
            observedPlacedObject.SetActive(false);
        }

        SetPairActive(currentPairIndex, false);

        currentPairIndex++;

        if (pairCount <= 0)
        {
            return;
        }

        if (currentPairIndex >= pairCount)
        {
            currentPairIndex = 0;
        }

        SetPairActive(currentPairIndex, true);

        Debug.Log($"SequentialPairVisibility completed pair index {completedIndex}.");
        
        // Release the virtual hand when pair completes
        if (virtualHandAttach != null)
        {
            virtualHandAttach.ReleaseAndRepositionToController();
        }
    }

    private static bool IsSameObjectFamily(GameObject observed, GameObject expected)
    {
        if (observed == null || expected == null)
        {
            return false;
        }

        if (observed == expected)
        {
            return true;
        }

        Transform observedTransform = observed.transform;
        Transform expectedTransform = expected.transform;

        if (observedTransform == null || expectedTransform == null)
        {
            return false;
        }

        return observedTransform.IsChildOf(expectedTransform)
            || expectedTransform.IsChildOf(observedTransform)
            || observedTransform.root == expectedTransform.root;
    }

    private static float CalculateObjectToTargetDistance(GameObject objectGo, GameObject targetGo)
    {
        if (objectGo == null || targetGo == null)
        {
            return float.MaxValue;
        }

        Collider targetCollider = targetGo.GetComponent<Collider>();
        Collider[] objectColliders = objectGo.GetComponentsInChildren<Collider>(true);

        if (targetCollider != null && objectColliders != null && objectColliders.Length > 0)
        {
            float minDistance = float.MaxValue;

            for (int i = 0; i < objectColliders.Length; i++)
            {
                Collider c = objectColliders[i];
                if (c == null)
                {
                    continue;
                }

                Vector3 pointOnObject = c.bounds.center;
                Vector3 closestOnTarget = targetCollider.ClosestPoint(pointOnObject);
                Vector3 closestOnObject = c.ClosestPoint(closestOnTarget);
                float distance = Vector3.Distance(closestOnObject, closestOnTarget);

                if (distance < minDistance)
                {
                    minDistance = distance;
                }
            }

            return minDistance;
        }

        return Vector3.Distance(objectGo.transform.position, targetGo.transform.position);
    }

    private static bool IsObjectPlacedInsideTarget(GameObject objectGo, GameObject targetGo)
    {
        if (objectGo == null || targetGo == null)
        {
            return false;
        }

        Collider targetCollider = targetGo.GetComponent<Collider>();
        Collider[] objectColliders = objectGo.GetComponentsInChildren<Collider>(true);

        if (targetCollider == null || objectColliders == null || objectColliders.Length == 0)
        {
            return false;
        }

        Bounds targetBounds = targetCollider.bounds;

        for (int i = 0; i < objectColliders.Length; i++)
        {
            Collider c = objectColliders[i];
            if (c == null)
            {
                continue;
            }

            if (targetBounds.Contains(c.bounds.center))
            {
                return true;
            }
        }

        if (targetBounds.Contains(objectGo.transform.position))
        {
            return true;
        }

        return false;
    }

    private void SetPairActive(int index, bool active)
    {
        if (orderedObjects != null && index >= 0 && index < orderedObjects.Length && orderedObjects[index] != null)
        {
            if (active)
            {
                RestoreObjectPose(index);
            }

            orderedObjects[index].SetActive(active);
        }

        if (orderedTargets != null && index >= 0 && index < orderedTargets.Length && orderedTargets[index] != null)
        {
            orderedTargets[index].gameObject.SetActive(active);

            if (active)
            {
                orderedTargets[index].ResetVisual();
                _pairActivationTime = Time.time;  // start cooldown
            }
        }
    }

    private void CacheInitialObjectPoses()
    {
        int count = GetPairCount();
        initialObjectPositions = new Vector3[count];
        initialObjectRotations = new Quaternion[count];

        for (int i = 0; i < count; i++)
        {
            GameObject go = orderedObjects != null && i < orderedObjects.Length ? orderedObjects[i] : null;
            if (go == null)
            {
                continue;
            }

            initialObjectPositions[i] = go.transform.position;
            initialObjectRotations[i] = go.transform.rotation;
        }
    }

    private void RestoreObjectPose(int index)
    {
        if (initialObjectPositions == null || initialObjectRotations == null)
        {
            return;
        }

        if (index < 0 || index >= initialObjectPositions.Length || index >= initialObjectRotations.Length)
        {
            return;
        }

        if (orderedObjects == null || index >= orderedObjects.Length || orderedObjects[index] == null)
        {
            return;
        }

        GameObject go = orderedObjects[index];
        go.transform.position = initialObjectPositions[index];
        go.transform.rotation = initialObjectRotations[index];

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private int GetPairCount()
    {
        if (orderedObjects == null || orderedTargets == null)
        {
            return 0;
        }

        return Mathf.Min(orderedObjects.Length, orderedTargets.Length);
    }

    private bool HasAssignedPairs()
    {
        return orderedObjects != null
            && orderedTargets != null
            && orderedObjects.Length > 0
            && orderedTargets.Length > 0;
    }

    private void AutoDiscoverPairs()
    {
        Dictionary<string, GameObject> objectsByColor = DiscoverObjectsByColor();
        Dictionary<string, TargetCubeTrigger> targetsByColor = DiscoverTargetsByColor();

        if (objectsByColor.Count == 0)
        {
            Dictionary<string, GameObject> fromStudyManager = DiscoverObjectsFromStudyManager();
            foreach (KeyValuePair<string, GameObject> kv in fromStudyManager)
            {
                if (!objectsByColor.ContainsKey(kv.Key))
                {
                    objectsByColor[kv.Key] = kv.Value;
                }
            }
        }

        List<GameObject> discoveredObjects = new List<GameObject>();
        List<TargetCubeTrigger> discoveredTargets = new List<TargetCubeTrigger>();

        for (int i = 0; i < ColorOrder.Length; i++)
        {
            string color = ColorOrder[i];
            if (objectsByColor.ContainsKey(color) && targetsByColor.ContainsKey(color))
            {
                discoveredObjects.Add(objectsByColor[color]);
                discoveredTargets.Add(targetsByColor[color]);
            }
        }

        orderedObjects = discoveredObjects.ToArray();
        orderedTargets = discoveredTargets.ToArray();

        Debug.Log($"SequentialPairVisibility auto-discovered {orderedObjects.Length} color pair(s).");

        if (orderedObjects.Length == 0)
        {
            Debug.LogWarning("SequentialPairVisibility did not find any phantom objects. Check object names/colors or UserStudyManager.coloredObjects assignments.");
        }
    }

    private Dictionary<string, GameObject> DiscoverObjectsByColor()
    {
        Dictionary<string, GameObject> result = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        List<Transform> candidates = new List<Transform>();

        if (objectsRoot == null)
        {
            objectsRoot = FindRootByName("Objects", "Phantoms", "PhantomObjects");
        }

        if (objectsRoot != null)
        {
            AddAllDescendants(objectsRoot, candidates);
        }
        else
        {
            Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                Transform t = allTransforms[i];
                if (t == null || !t.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (t.name.IndexOf("Phantom", StringComparison.OrdinalIgnoreCase) >= 0 || t.CompareTag("Grabbable"))
                {
                    candidates.Add(t);
                }
            }
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            GameObject go = candidates[i] != null ? candidates[i].gameObject : null;
            if (go == null)
            {
                continue;
            }

            string color = ExtractColorFromGameObject(go);
            if (string.IsNullOrEmpty(color) || result.ContainsKey(color))
            {
                continue;
            }

            result[color] = go;
        }

        return result;
    }

    private Dictionary<string, TargetCubeTrigger> DiscoverTargetsByColor()
    {
        Dictionary<string, TargetCubeTrigger> result = new Dictionary<string, TargetCubeTrigger>(StringComparer.OrdinalIgnoreCase);
        List<Transform> candidates = new List<Transform>();

        if (targetsRoot == null)
        {
            targetsRoot = FindRootByName("Targets", "Target");
        }

        if (targetsRoot != null)
        {
            AddAllDescendants(targetsRoot, candidates);
        }
        else
        {
            Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                Transform t = allTransforms[i];
                if (t == null || !t.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (t.name.IndexOf("Target", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    candidates.Add(t);
                }
            }
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            GameObject go = candidates[i] != null ? candidates[i].gameObject : null;
            if (go == null)
            {
                continue;
            }

            if (ShouldIgnoreAsBubbleTarget(go))
            {
                continue;
            }

            string color = string.Empty;

            TargetCubeTrigger existing = go.GetComponent<TargetCubeTrigger>();
            if (existing != null && !string.IsNullOrWhiteSpace(existing.targetColor))
            {
                color = ExtractColor(existing.targetColor);
            }

            if (string.IsNullOrEmpty(color))
            {
                color = ExtractColorFromGameObject(go);
            }

            if (string.IsNullOrEmpty(color) || result.ContainsKey(color))
            {
                continue;
            }

            Collider col = go.GetComponent<Collider>();
            if (col == null)
            {
                continue;
            }

            col.isTrigger = true;

            TargetCubeTrigger trigger = go.GetComponent<TargetCubeTrigger>();
            if (trigger == null)
            {
                trigger = go.AddComponent<TargetCubeTrigger>();
            }

            trigger.targetColor = FirstCharUpper(color);
            result[color] = trigger;
        }

        return result;
    }

    private void DisableBubbleTargets()
    {
        BubbleTarget[] bubbles = FindObjectsByType<BubbleTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < bubbles.Length; i++)
        {
            BubbleTarget bubble = bubbles[i];
            if (bubble == null)
            {
                continue;
            }

            bubble.gameObject.SetActive(false);
        }
    }

    private static bool ShouldIgnoreAsBubbleTarget(GameObject go)
    {
        if (go == null)
        {
            return true;
        }

        if (go.GetComponent<BubbleTarget>() != null)
        {
            return true;
        }

        return go.name.IndexOf("Bubble", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private Dictionary<string, GameObject> DiscoverObjectsFromStudyManager()
    {
        Dictionary<string, GameObject> result = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        UserStudyManager manager = FindFirstObjectByType<UserStudyManager>();
        if (manager == null || manager.coloredObjects == null)
        {
            return result;
        }

        for (int i = 0; i < manager.coloredObjects.Length; i++)
        {
            GameObject go = manager.coloredObjects[i];
            if (go == null)
            {
                continue;
            }

            string color = ExtractColorFromGameObject(go);
            if (string.IsNullOrEmpty(color) || result.ContainsKey(color))
            {
                continue;
            }

            result[color] = go;
        }

        return result;
    }

    private static void AddAllDescendants(Transform root, List<Transform> list)
    {
        if (root == null || list == null)
        {
            return;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            list.Add(child);
            AddAllDescendants(child, list);
        }
    }

    private static string ExtractColor(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string lower = text.ToLower();
        for (int i = 0; i < ColorOrder.Length; i++)
        {
            if (lower.Contains(ColorOrder[i]))
            {
                return ColorOrder[i];
            }
        }

        return string.Empty;
    }

    private static string ExtractColorFromGameObject(GameObject go)
    {
        if (go == null)
        {
            return string.Empty;
        }

        string color = ExtractColor(go.name);
        if (!string.IsNullOrEmpty(color))
        {
            return color;
        }

        Renderer renderer = go.GetComponentInChildren<Renderer>(true);
        if (renderer != null)
        {
            Material mat = renderer.sharedMaterial;
            if (mat != null)
            {
                color = ExtractColor(mat.name);
                if (!string.IsNullOrEmpty(color))
                {
                    return color;
                }
            }
        }

        return string.Empty;
    }

    private static string FirstCharUpper(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpper(value[0]) + value.Substring(1).ToLower();
    }

    private static Transform FindRootByName(params string[] names)
    {
        Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform t = allTransforms[i];
            if (t == null || t.parent != null || !t.gameObject.scene.IsValid())
            {
                continue;
            }

            for (int n = 0; n < names.Length; n++)
            {
                if (string.Equals(t.name, names[n], StringComparison.OrdinalIgnoreCase))
                {
                    return t;
                }
            }
        }

        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform t = allTransforms[i];
            if (t == null || !t.gameObject.scene.IsValid())
            {
                continue;
            }

            for (int n = 0; n < names.Length; n++)
            {
                if (t.name.IndexOf(names[n], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return t;
                }
            }
        }

        return null;
    }

    private void SubscribeToTargets()
    {
        if (isSubscribed || orderedTargets == null)
        {
            return;
        }

        for (int i = 0; i < orderedTargets.Length; i++)
        {
            if (orderedTargets[i] != null)
            {
                orderedTargets[i].OnObjectPlaced += HandleObjectPlaced;
            }
        }

        isSubscribed = true;
    }

    private void UnsubscribeFromTargets()
    {
        if (!isSubscribed || orderedTargets == null)
        {
            return;
        }

        for (int i = 0; i < orderedTargets.Length; i++)
        {
            if (orderedTargets[i] != null)
            {
                orderedTargets[i].OnObjectPlaced -= HandleObjectPlaced;
            }
        }

        isSubscribed = false;
    }
}
