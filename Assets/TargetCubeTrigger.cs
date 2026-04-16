using System;
using UnityEngine;

public class TargetCubeTrigger : MonoBehaviour
{
    [Header("Target Config")]
    public string targetColor; // Red, Green, Blue, Yellow

    public event Action<GameObject, string, bool> OnObjectPlaced;

    private Renderer targetRenderer;
    private Material originalMaterial;

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

        bool isCorrect = IsColorMatch(placedObject, targetColor);
        OnObjectPlaced?.Invoke(placedObject, targetColor, isCorrect);
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
        if (targetRenderer != null && originalMaterial != null)
        {
            targetRenderer.material = originalMaterial;
        }
    }
}
