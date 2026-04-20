using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class RaycastObjectSelector : MonoBehaviour
{
    public Transform rayOrigin;            // Right-hand controller transform
    public float rayLength = Mathf.Infinity;  // Infinite ray distance for far object selection
    public float maxVisualRayLength = 100f;   // Visual ray max length when not hitting anything
    public LayerMask selectableLayers;
    public HandCalibrationDepthScale depthScale;  // Reference to threshold system
    public InputActionProperty triggerAction; // Kept for compatibility with existing scene wiring
    public LineRenderer lineRenderer;      // LineRenderer for VR-visible ray
    public Color rayColor = Color.cyan;   // Ray color
    public float rayWidth = 0.01f;         // Ray width (0.01m = 1cm)
    public float rayActivationDistanceFromHmd = 0.120f; // Ray shows only beyond this HMD distance
    public float activationMargin = 0.05f; // Extra margin above depthScale threshold when available
    public Material highlightMaterial;     // Material to apply when hovering
    public bool disableOtherSceneRays = true; // Disable all other line visuals so only this ray remains

    private GameObject currentTarget;
    private GameObject previousTarget;
    private Material originalMaterial;
    private bool isGrabbed = false;        // Track if object is currently grabbed
    private bool createdLineRenderer;
    private Material fallbackRayMaterial;

    void Start()
    {
        if (rayOrigin == null)
            rayOrigin = transform;

        // Always use a dedicated runtime line so legacy/XR line visuals cannot override color.
        GameObject runtimeLineObj = new GameObject("RaycastSelectorRuntimeLine");
        runtimeLineObj.transform.SetParent(rayOrigin, false);
        lineRenderer = runtimeLineObj.AddComponent<LineRenderer>();
        createdLineRenderer = true;

        DisableCompetingRayVisualOnOrigin();
        if (disableOtherSceneRays)
            DisableAllOtherSceneRays();

        ConfigureLineRenderer();
        EnsureLineRendererCanSetPositions();
        ApplyRayVisualSettings();
        lineRenderer.enabled = false;

        if (triggerAction.action != null)
            triggerAction.action.Enable();
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
        if (lineRenderer == null || rayOrigin == null)
            return;

        EnsureLineRendererCanSetPositions();
        ApplyRayVisualSettings();

        // ReverseGoGo behavior: ray appears only when hand is in extension zone and not grabbing.
        bool shouldShowRay = !isGrabbed && IsRayExtensionActive();

        // Hide ray when extension is inactive.
        if (!shouldShowRay)
        {
            if (lineRenderer.enabled)
            {
                lineRenderer.enabled = false;
                ClearHighlight();
                currentTarget = null;
            }
            return;
        }

        // Show ray while extension is active.
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
            
            // Draw ray at visual max length when not hitting anything (use maxVisualRayLength instead of infinite)
            lineRenderer.SetPosition(1, rayOrigin.position + rayOrigin.forward * maxVisualRayLength);
        }

    }

    private bool IsRayExtensionActive()
    {
        // Block the ray entirely until arm-length calibration has been recorded.
        if (depthScale != null && !depthScale.IsCalibrationComplete())
            return false;

        Transform hmd = null;
        if (depthScale != null && depthScale.hmdTransform != null)
            hmd = depthScale.hmdTransform;
        else if (Camera.main != null)
            hmd = Camera.main.transform;

        if (hmd == null || rayOrigin == null)
            return false;

        float activationDistance = Mathf.Max(0.05f, rayActivationDistanceFromHmd);
        if (depthScale != null)
            activationDistance = Mathf.Max(activationDistance, depthScale.thresholdDistance + Mathf.Max(0f, activationMargin));

        float currentDistance = Vector3.Distance(hmd.position, rayOrigin.position);
        return currentDistance >= activationDistance;
    }

    private void ConfigureLineRenderer()
    {
        if (lineRenderer == null)
            return;

        // Respect inspector styling for existing LineRenderers.
        // Only initialize defaults if this script had to create one.
        if (!createdLineRenderer)
            return;

        if (lineRenderer.material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (shader != null)
                lineRenderer.material = new Material(shader);
        }

        lineRenderer.startColor = rayColor;
        lineRenderer.endColor = rayColor;
        lineRenderer.startWidth = rayWidth;
        lineRenderer.endWidth = rayWidth;
        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;
    }

    private void DisableCompetingRayVisualOnOrigin()
    {
        if (rayOrigin == null)
            return;

        Behaviour[] behaviours = rayOrigin.GetComponentsInChildren<Behaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            Behaviour b = behaviours[i];
            if (b == null)
                continue;

            if (b.GetType().Name == "XRInteractorLineVisual")
                b.enabled = false;
        }

        LineRenderer[] otherLines = rayOrigin.GetComponentsInChildren<LineRenderer>(true);
        for (int i = 0; i < otherLines.Length; i++)
        {
            LineRenderer other = otherLines[i];
            if (other == null || other == lineRenderer)
                continue;

            other.enabled = false;
        }
    }

    private void DisableAllOtherSceneRays()
    {
        LineRenderer[] allLines = FindObjectsByType<LineRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allLines.Length; i++)
        {
            LineRenderer lr = allLines[i];
            if (lr == null || lr == lineRenderer)
                continue;

            lr.enabled = false;
        }

        MonoBehaviour[] allBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allBehaviours.Length; i++)
        {
            MonoBehaviour b = allBehaviours[i];
            if (b == null)
                continue;

            if (b.GetType().Name == "XRInteractorLineVisual")
                b.enabled = false;
        }
    }

    private void EnsureLineRendererCanSetPositions()
    {
        if (lineRenderer == null)
            return;

        // Prevent SetPosition(index) out-of-bounds crashes on inspector-provided lines.
        if (lineRenderer.positionCount < 2)
            lineRenderer.positionCount = 2;
    }

    private void ApplyRayVisualSettings()
    {
        if (lineRenderer == null)
            return;

        Color effectiveRayColor = new Color(0f, 1f, 1f, 1f);

        lineRenderer.startColor = effectiveRayColor;
        lineRenderer.endColor = effectiveRayColor;
        lineRenderer.startWidth = rayWidth;
        lineRenderer.endWidth = rayWidth;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;

        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(effectiveRayColor, 0f),
                new GradientColorKey(effectiveRayColor, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        lineRenderer.colorGradient = g;

        Material mat = lineRenderer.material;
        if (fallbackRayMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader != null)
            {
                fallbackRayMaterial = new Material(shader);
                if (fallbackRayMaterial.HasProperty("_SrcBlend"))
                    fallbackRayMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                if (fallbackRayMaterial.HasProperty("_DstBlend"))
                    fallbackRayMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                if (fallbackRayMaterial.HasProperty("_Cull"))
                    fallbackRayMaterial.SetInt("_Cull", (int)CullMode.Off);
                if (fallbackRayMaterial.HasProperty("_ZWrite"))
                    fallbackRayMaterial.SetInt("_ZWrite", 0);
            }
        }

        if (fallbackRayMaterial != null)
            mat = lineRenderer.material = fallbackRayMaterial;

        if (mat != null)
        {
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", Texture2D.whiteTexture);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", effectiveRayColor);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", effectiveRayColor);
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
