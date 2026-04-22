using System;
using System.Collections;
using UnityEngine;

public class TargetCubeTrigger : MonoBehaviour
{
    [Header("Target Config")]
    public string targetColor; // Red, Green, Blue, Yellow
    [Range(0f, 1f)] public float requiredObjectInsideFraction = 0.8f;

    [Header("Hover Visual")]
    [Tooltip("Material applied to the target cube when the placed object is >=80% inside.")]
    public Material hoverMaterial;
    [Range(0f, 1f)]
    [Tooltip("Fraction of the object that must be inside the target to show the hover highlight (default 0.8 = 80%).")]
    public float hoverInsideFraction = 0.8f;
    [Tooltip("How long (seconds) the hover highlight is shown before the placement event fires and the target disappears.")]
    public float hoverDisplayDuration = 0.5f;

    public event Action<GameObject, string, bool> OnObjectPlaced;

    private Renderer targetRenderer;
    private Material originalMaterial;
    private bool _isHovering = false;
    private bool _placementPending = false;

    private void Awake()
    {
        EnsurePhysicsSetup();
    }

    private void Start()
    {
        targetRenderer = GetComponent<Renderer>();
        if (targetRenderer != null)
        {
            originalMaterial = targetRenderer.material;
        }

        EnsurePhysicsSetup();
    }

    private void EnsurePhysicsSetup()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }

        rb.isKinematic = true;
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    private void OnTriggerEnter(Collider other)
    {
        ProcessTrigger(other);
    }

    private void OnTriggerStay(Collider other)
    {
        ProcessTrigger(other);
    }

    private void ProcessTrigger(Collider other)
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        if (other == null || other.gameObject == gameObject)
        {
            return;
        }

        GameObject placedObject = ResolvePlacedObject(other);
        if (placedObject == null)
        {
            return;
        }

        float fraction = CalculateObjectInsideFraction(placedObject, gameObject);
        float threshold = Mathf.Clamp01(requiredObjectInsideFraction);

        // Hover activates independently at hoverInsideFraction (default 80%).
        SetHover(fraction >= Mathf.Clamp01(hoverInsideFraction));

        if (fraction < threshold)
        {
            return;
        }

        TriggerPlacement(placedObject);
    }

    // Called by SequentialPairVisibility.Update() proximity path so both routes
    // go through hover → delay → OnObjectPlaced instead of disappearing immediately.
    public bool IsPlacementPending => _placementPending;

    public void TriggerPlacement(GameObject placedObject)
    {
        if (_placementPending || placedObject == null)
        {
            return;
        }

        SetHover(true);
        _placementPending = true;
        bool isCorrect = IsColorMatch(placedObject, targetColor);
        // Delay the event so the hover highlight is visible before the target disappears.
        StartCoroutine(FirePlacementAfterDelay(placedObject, isCorrect));
    }

    private IEnumerator FirePlacementAfterDelay(GameObject placedObject, bool isCorrect)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, hoverDisplayDuration));
        OnObjectPlaced?.Invoke(placedObject, targetColor, isCorrect);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!_placementPending)
        {
            SetHover(false);
        }
    }

    private void SetHover(bool hover)
    {
        if (targetRenderer == null || hover == _isHovering)
        {
            return;
        }

        _isHovering = hover;
        if (hover && hoverMaterial != null)
        {
            targetRenderer.material = hoverMaterial;
        }
        else if (!hover && originalMaterial != null)
        {
            targetRenderer.material = originalMaterial;
        }
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

    private static GameObject ResolvePlacedObject(Collider other)
    {
        if (other == null)
        {
            return null;
        }

        if (other.attachedRigidbody != null)
        {
            return other.attachedRigidbody.gameObject;
        }

        Rigidbody rbInParent = other.GetComponentInParent<Rigidbody>();
        if (rbInParent != null)
        {
            return rbInParent.gameObject;
        }

        return other.transform.root != null ? other.transform.root.gameObject : other.gameObject;
    }

    private static bool IsColorMatch(GameObject placedObject, string expectedColor)
    {
        if (placedObject == null || string.IsNullOrWhiteSpace(expectedColor))
        {
            return false;
        }

        string expected = expectedColor.ToLowerInvariant();

        if (placedObject.name.ToLowerInvariant().Contains(expected))
        {
            return true;
        }

        Renderer renderer = placedObject.GetComponentInChildren<Renderer>(true);
        if (renderer != null && renderer.sharedMaterial != null)
        {
            if (renderer.sharedMaterial.name.ToLowerInvariant().Contains(expected))
            {
                return true;
            }
        }

        return false;
    }

    public void ResetVisual()
    {
        _isHovering = false;
        _placementPending = false;
        StopAllCoroutines();
        if (targetRenderer != null && originalMaterial != null)
        {
            targetRenderer.material = originalMaterial;
        }
    }
}
