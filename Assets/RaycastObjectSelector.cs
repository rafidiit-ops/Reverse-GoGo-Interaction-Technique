using UnityEngine;

public class RaycastObjectSelector : MonoBehaviour
{
    public Transform rayOrigin;            // Right-hand controller transform
    public float rayLength = 3f;
    public LayerMask selectableLayers;
    public HandCalibrationDepthScale depthScale;  // Reference to threshold system
    public LineRenderer lineRenderer;      // LineRenderer for VR-visible ray
    public Color rayColor = Color.black;   // Ray color
    public float rayWidth = 0.01f;         // Ray width (0.01m = 1cm)
    public Material highlightMaterial;     // Material to apply when hovering

    private GameObject currentTarget;
    private GameObject previousTarget;
    private Material originalMaterial;
    private bool isGrabbed = false;        // Track if object is currently grabbed

    void Start()
    {
        // Check if depthScale is assigned
        if (depthScale == null)
        {
            Debug.LogError("RaycastObjectSelector: HandCalibrationDepthScale is not assigned! Ray will not work properly.");
        }
        
        // Auto-create LineRenderer if not assigned
        if (lineRenderer == null)
        {
            GameObject lineObj = new GameObject("RayLine");
            lineObj.transform.SetParent(rayOrigin);
            lineObj.transform.localPosition = Vector3.zero;
            lineObj.transform.localRotation = Quaternion.identity;
            
            lineRenderer = lineObj.AddComponent<LineRenderer>();
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = rayColor;
            lineRenderer.endColor = rayColor;
            lineRenderer.startWidth = rayWidth;
            lineRenderer.endWidth = rayWidth;
            lineRenderer.positionCount = 2;
            lineRenderer.useWorldSpace = true;
        }
        
        // Start with ray disabled - will enable when controller is beyond threshold
        if (lineRenderer != null)
        {
            lineRenderer.enabled = false;
        }
    }

    public GameObject GetCurrentTarget()
    {
        return currentTarget;
    }

    public void SetGrabbedState(bool grabbed)
    {
        isGrabbed = grabbed;
        
        // Clear highlight when grabbing
        if (grabbed)
        {
            ClearHighlight();
        }
    }

    void Update()
    {
        // Check if depthScale is assigned
        if (depthScale == null)
        {
            lineRenderer.enabled = false;
            return;
        }
        
        // Check if controller is beyond threshold
        bool isBeyondThreshold = depthScale.IsControllerBeyondThreshold();
        
        // Debug log every 60 frames (once per second at 60fps)
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"[RaycastSelector] Beyond threshold: {isBeyondThreshold} | Ray enabled: {lineRenderer.enabled} | Grabbed: {isGrabbed}");
        }
        
        // Hide ray if object is grabbed OR controller is inside threshold
        if (isGrabbed || !isBeyondThreshold)
        {
            if (lineRenderer.enabled)
            {
                lineRenderer.enabled = false;
                if (!isGrabbed)
                {
                    ClearHighlight();
                    currentTarget = null;
                }
            }
            return;
        }

        // Show ray when beyond threshold and not grabbing
        if (!lineRenderer.enabled)
        {
            lineRenderer.enabled = true;
        }
        
        lineRenderer.SetPosition(0, rayOrigin.position);

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, rayLength, selectableLayers))
        {
            currentTarget = hit.collider.gameObject;
            
            // Draw ray to hit point
            lineRenderer.SetPosition(1, hit.point);
            
            // Apply highlight
            ApplyHighlight(currentTarget);
        }
        else
        {
            ClearHighlight();
            currentTarget = null;
            
            // Draw ray at full length when not hitting anything
            lineRenderer.SetPosition(1, rayOrigin.position + rayOrigin.forward * rayLength);
        }
    }

    private void ApplyHighlight(GameObject target)
    {
        if (target == previousTarget || highlightMaterial == null)
            return;

        // Clear previous highlight
        ClearHighlight();

        // Apply new highlight
        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
        {
            originalMaterial = renderer.material;
            renderer.material = highlightMaterial;
            previousTarget = target;
        }
    }

    private void ClearHighlight()
    {
        if (previousTarget != null)
        {
            Renderer renderer = previousTarget.GetComponent<Renderer>();
            if (renderer != null && originalMaterial != null)
            {
                renderer.material = originalMaterial;
            }
            previousTarget = null;
            originalMaterial = null;
        }
    }

    void OnDisable()
    {
        ClearHighlight();
    }
}
