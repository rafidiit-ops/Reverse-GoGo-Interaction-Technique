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
    [Range(0f, 1f)] public float requiredObjectInsideFraction = 0.8f;

    private int currentPairIndex = 0;
    private bool isSubscribed = false;
    private Vector3[] initialObjectPositions;
    private Quaternion[] initialObjectRotations;
    private float _pairActivationTime = -1f;   // Time.time when the current pair was last activated
    private const float PairActivationCooldown = 0.5f;  // seconds to ignore completion after activation
    private VirtualHandAttach virtualHandAttach;  // Legacy single-reference path (kept for compatibility)
    private static readonly string[] ColorOrder = { "red", "blue", "green", "yellow" };

    private void Start()
    {
        if (autoDiscoverPairs || !HasAssignedPairs())
        {
            AutoDiscoverPairs();
        }

        DisableBubbleTargets();
        HideAllPhantomObjects();
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

        ReleaseAllActiveGrabControllers();
    }

    private void ReleaseAllActiveGrabControllers()
    {
        // Handle Reverse Go-Go style controllers.
        VirtualHandAttach[] virtualHands = FindObjectsByType<VirtualHandAttach>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < virtualHands.Length; i++)
        {
            VirtualHandAttach hand = virtualHands[i];
            if (hand != null && hand.isActiveAndEnabled)
            {
                hand.ReleaseAndRepositionToController();
            }
        }

        // Handle Traditional Go-Go controller path.
        TraditionalGoGoInteraction[] traditionalControllers = FindObjectsByType<TraditionalGoGoInteraction>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < traditionalControllers.Length; i++)
        {
            TraditionalGoGoInteraction controller = traditionalControllers[i];
            if (controller != null && controller.isActiveAndEnabled)
            {
                controller.ForceReleaseForSequenceTransition();
            }
        }

        // Handle HOMER controller path.
        HOMERController[] homerControllers = FindObjectsByType<HOMERController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < homerControllers.Length; i++)
        {
            HOMERController controller = homerControllers[i];
            if (controller != null && controller.isActiveAndEnabled)
            {
                controller.ForceRelease();
            }
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

    private bool IsObjectPlacedInsideTarget(GameObject objectGo, GameObject targetGo)
    {
        float insideFraction = CalculateObjectInsideFraction(objectGo, targetGo);
        return insideFraction >= Mathf.Clamp01(requiredObjectInsideFraction);
    }

    private static float CalculateObjectInsideFraction(GameObject objectGo, GameObject targetGo)
    {
        if (objectGo == null || targetGo == null)
        {
            return 0f;
        }

        Renderer targetRenderer = targetGo.GetComponent<Renderer>();
        if (targetRenderer != null)
        {
            Collider[] objectCollidersForRendererTarget = objectGo.GetComponentsInChildren<Collider>(true);
            int totalRendererSamples = 0;
            int insideRendererSamples = 0;

            for (int i = 0; i < objectCollidersForRendererTarget.Length; i++)
            {
                Collider objectCollider = objectCollidersForRendererTarget[i];
                if (objectCollider == null || !objectCollider.enabled)
                {
                    continue;
                }

                SampleColliderVolumeAgainstBounds(objectCollider, targetRenderer.bounds, ref insideRendererSamples, ref totalRendererSamples);
            }

            if (totalRendererSamples > 0)
            {
                return (float)insideRendererSamples / totalRendererSamples;
            }
        }

        Collider targetCollider = targetGo.GetComponent<Collider>();
        if (targetCollider == null)
        {
            return 0f;
        }

        Collider[] objectColliders = objectGo.GetComponentsInChildren<Collider>(true);
        int totalSamples = 0;
        int insideSamples = 0;

        for (int i = 0; i < objectColliders.Length; i++)
        {
            Collider objectCollider = objectColliders[i];
            if (objectCollider == null || !objectCollider.enabled)
            {
                continue;
            }

            SampleColliderVolume(objectCollider, targetCollider, ref insideSamples, ref totalSamples);
        }

        if (totalSamples == 0)
        {
            Renderer renderer = objectGo.GetComponentInChildren<Renderer>(true);
            if (renderer == null)
            {
                return 0f;
            }

            SampleBoundsVolume(renderer.bounds, targetCollider, ref insideSamples, ref totalSamples);
        }

        if (totalSamples == 0)
        {
            return 0f;
        }

        return (float)insideSamples / totalSamples;
    }

    private static void SampleColliderVolume(Collider objectCollider, Collider targetCollider, ref int insideSamples, ref int totalSamples)
    {
        if (objectCollider is BoxCollider box)
        {
            SampleBoxColliderVolume(box, targetCollider, ref insideSamples, ref totalSamples);
            return;
        }

        SampleBoundsVolume(objectCollider.bounds, targetCollider, ref insideSamples, ref totalSamples);
    }

    private static void SampleColliderVolumeAgainstBounds(Collider objectCollider, Bounds targetBounds, ref int insideSamples, ref int totalSamples)
    {
        if (objectCollider is BoxCollider box)
        {
            const int stepsPerAxis = 7;
            Vector3 center = box.center;
            Vector3 size = box.size;

            for (int x = 0; x < stepsPerAxis; x++)
            {
                float tx = x / (float)(stepsPerAxis - 1) - 0.5f;
                for (int y = 0; y < stepsPerAxis; y++)
                {
                    float ty = y / (float)(stepsPerAxis - 1) - 0.5f;
                    for (int z = 0; z < stepsPerAxis; z++)
                    {
                        float tz = z / (float)(stepsPerAxis - 1) - 0.5f;
                        Vector3 localPoint = center + Vector3.Scale(size, new Vector3(tx, ty, tz));
                        Vector3 worldPoint = box.transform.TransformPoint(localPoint);
                        totalSamples++;
                        if (targetBounds.Contains(worldPoint))
                        {
                            insideSamples++;
                        }
                    }
                }
            }

            return;
        }

        const int fallbackStepsPerAxis = 7;
        Vector3 min = objectCollider.bounds.min;
        Vector3 fallbackSize = objectCollider.bounds.size;
        for (int x = 0; x < fallbackStepsPerAxis; x++)
        {
            float tx = x / (float)(fallbackStepsPerAxis - 1);
            for (int y = 0; y < fallbackStepsPerAxis; y++)
            {
                float ty = y / (float)(fallbackStepsPerAxis - 1);
                for (int z = 0; z < fallbackStepsPerAxis; z++)
                {
                    float tz = z / (float)(fallbackStepsPerAxis - 1);
                    Vector3 worldPoint = min + Vector3.Scale(fallbackSize, new Vector3(tx, ty, tz));
                    totalSamples++;
                    if (targetBounds.Contains(worldPoint))
                    {
                        insideSamples++;
                    }
                }
            }
        }
    }

    private static void SampleBoxColliderVolume(BoxCollider box, Collider targetCollider, ref int insideSamples, ref int totalSamples)
    {
        const int stepsPerAxis = 7;
        Vector3 center = box.center;
        Vector3 size = box.size;

        for (int x = 0; x < stepsPerAxis; x++)
        {
            float tx = x / (float)(stepsPerAxis - 1) - 0.5f;
            for (int y = 0; y < stepsPerAxis; y++)
            {
                float ty = y / (float)(stepsPerAxis - 1) - 0.5f;
                for (int z = 0; z < stepsPerAxis; z++)
                {
                    float tz = z / (float)(stepsPerAxis - 1) - 0.5f;
                    Vector3 localPoint = center + Vector3.Scale(size, new Vector3(tx, ty, tz));
                    Vector3 worldPoint = box.transform.TransformPoint(localPoint);
                    totalSamples++;
                    if (IsPointInsideCollider(targetCollider, worldPoint))
                    {
                        insideSamples++;
                    }
                }
            }
        }
    }

    private static void SampleBoundsVolume(Bounds bounds, Collider targetCollider, ref int insideSamples, ref int totalSamples)
    {
        const int stepsPerAxis = 7;
        Vector3 min = bounds.min;
        Vector3 size = bounds.size;

        for (int x = 0; x < stepsPerAxis; x++)
        {
            float tx = x / (float)(stepsPerAxis - 1);
            for (int y = 0; y < stepsPerAxis; y++)
            {
                float ty = y / (float)(stepsPerAxis - 1);
                for (int z = 0; z < stepsPerAxis; z++)
                {
                    float tz = z / (float)(stepsPerAxis - 1);
                    Vector3 worldPoint = min + Vector3.Scale(size, new Vector3(tx, ty, tz));
                    totalSamples++;
                    if (IsPointInsideCollider(targetCollider, worldPoint))
                    {
                        insideSamples++;
                    }
                }
            }
        }
    }

    private static bool IsPointInsideCollider(Collider collider, Vector3 worldPoint)
    {
        Vector3 closestPoint = collider.ClosestPoint(worldPoint);
        return (closestPoint - worldPoint).sqrMagnitude <= 0.000001f;
    }

    private void SetPairActive(int index, bool active)
    {
        if (orderedObjects != null && index >= 0 && index < orderedObjects.Length && orderedObjects[index] != null)
        {
            if (active)
            {
                RestoreObjectPose(index);
                EnsureParentHierarchyActive(orderedObjects[index].transform);
            }

            orderedObjects[index].SetActive(active);
        }

        if (orderedTargets != null && index >= 0 && index < orderedTargets.Length && orderedTargets[index] != null)
        {
            if (active)
            {
                EnsureParentHierarchyActive(orderedTargets[index].transform);
            }

            orderedTargets[index].gameObject.SetActive(active);

            if (active)
            {
                orderedTargets[index].ResetVisual();
                _pairActivationTime = Time.time;  // start cooldown
            }
        }
    }

    // Walk up the parent chain and activate any inactive ancestor so that
    // SetActive(true) on a child actually makes it visible in-hierarchy.
    private static void EnsureParentHierarchyActive(Transform child)
    {
        Transform parent = child.parent;
        while (parent != null)
        {
            if (!parent.gameObject.activeSelf)
            {
                parent.gameObject.SetActive(true);
            }
            parent = parent.parent;
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
        if (rb != null && !rb.isKinematic)
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

            // Ignore grouping containers like "Phantom Object". Only bind real
            // interactive phantom objects that have their own renderer/collider.
            if (!IsConcretePhantomObject(go))
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

    private static bool IsConcretePhantomObject(GameObject go)
    {
        if (go == null)
        {
            return false;
        }

        Renderer renderer = go.GetComponent<Renderer>();
        Collider collider = go.GetComponent<Collider>();

        if (renderer == null || collider == null)
        {
            return false;
        }

        return go.name.IndexOf("Phantom", StringComparison.OrdinalIgnoreCase) >= 0
            || go.CompareTag("Phantom")
            || go.CompareTag("Grabbable");
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

        // Fallback: if root-based search found nothing (e.g. only BubbleTargets were
        // present under targetsRoot), scan the whole scene for TargetCubeTrigger.
        if (result.Count == 0)
        {
            TargetCubeTrigger[] allTriggers = FindObjectsByType<TargetCubeTrigger>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < allTriggers.Length; i++)
            {
                TargetCubeTrigger trigger = allTriggers[i];
                if (trigger == null || ShouldIgnoreAsBubbleTarget(trigger.gameObject))
                {
                    continue;
                }

                string color = ExtractColor(trigger.targetColor);
                if (string.IsNullOrEmpty(color) || result.ContainsKey(color))
                {
                    continue;
                }

                result[color] = trigger;
            }
        }

        return result;
    }

    // Hide every Phantom-named/Phantom-tagged object in the scene before the sequence
    // starts. Some scene overrides only deactivate 3 of 4 phantoms, leaving one visible.
    private void HideAllPhantomObjects()
    {
        Transform[] allTransforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform t = allTransforms[i];
            if (t == null || !t.gameObject.scene.IsValid())
            {
                continue;
            }

            bool isPhantom = t.name.IndexOf("Phantom", System.StringComparison.OrdinalIgnoreCase) >= 0
                             || t.CompareTag("Phantom");
            if (isPhantom && t.GetComponent<Renderer>() != null)
            {
                t.gameObject.SetActive(false);
            }
        }
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
