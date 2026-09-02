using UnityEngine;

public class VirtualHandVisualSetup : MonoBehaviour
{
    /// <summary>
    /// This script automatically configures the VirtualHand to use the existing XR Controller visual
    /// Makes sure the visual stays visible even when grabbing cubes
    /// </summary>

    [ContextMenu("Setup XR Controller Visual")]
    public void SetupXRControllerVisual()
    {
        Debug.Log("🤖 Setting up XR Controller visual...");

        // Step 1: Remove the cyan cube mesh and renderer from VirtualHand parent if they exist
        MeshFilter parentMeshFilter = GetComponent<MeshFilter>();
        if (parentMeshFilter != null)
        {
            DestroyImmediate(parentMeshFilter);
            Debug.Log("✅ Removed MeshFilter from VirtualHand parent");
        }

        MeshRenderer parentRenderer = GetComponent<MeshRenderer>();
        if (parentRenderer != null)
        {
            DestroyImmediate(parentRenderer);
            Debug.Log("✅ Removed MeshRenderer from VirtualHand parent");
        }

        // Step 2: Find ANY child (first child) - it should be the XR Controller
        Transform xrControllerChild = null;
        
        if (transform.childCount > 0)
        {
            xrControllerChild = transform.GetChild(0);
        }

        if (xrControllerChild == null)
        {
            Debug.LogError("❌ No child found under VirtualHand! Please ensure VirtualHand has a child object.");
            return;
        }

        Debug.Log("✅ Found child: " + xrControllerChild.name);

        // Step 3: Enable the child if it's disabled
        xrControllerChild.gameObject.SetActive(true);
        Debug.Log("✅ Ensured child is active/visible");

        // Step 4: Ensure the child has a Renderer for visibility
        Renderer childRenderer = xrControllerChild.GetComponent<Renderer>();
        if (childRenderer == null)
        {
            Debug.Log("⚠️ Child has no Renderer. Looking for one in descendants...");
            childRenderer = xrControllerChild.GetComponentInChildren<Renderer>();
        }

        if (childRenderer == null)
        {
            Debug.LogWarning("⚠️ No Renderer found. Creating one with a cube mesh...");
            
            MeshFilter childMeshFilter = xrControllerChild.GetComponent<MeshFilter>();
            if (childMeshFilter == null)
            {
                childMeshFilter = xrControllerChild.gameObject.AddComponent<MeshFilter>();
                childMeshFilter.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                Debug.Log("✅ Added MeshFilter with Cube");
            }

            childRenderer = xrControllerChild.gameObject.AddComponent<MeshRenderer>();
            Debug.Log("✅ Added MeshRenderer to child");
        }

        // Step 5: Assign a controller-like material
        Material controllerMaterial = new Material(Shader.Find("Standard"));
        controllerMaterial.name = "ControllerMaterial";
        controllerMaterial.color = new Color(0.2f, 0.2f, 0.2f, 1f); // Dark gray (controller-like)
        childRenderer.material = controllerMaterial;
        Debug.Log("✅ Assigned controller-like material");

        // Step 6: Make sure child is positioned at origin relative to VirtualHand
        if (xrControllerChild.localPosition != Vector3.zero)
        {
            xrControllerChild.localPosition = Vector3.zero;
            Debug.Log("✅ Set child position to origin");
        }

        // Step 7: Ensure the VirtualHand parent is properly positioned
        transform.localScale = Vector3.one;
        Debug.Log("✅ Set VirtualHand scale to normal");

        Debug.Log("✅✅✅ Virtual Controller is now ready! The XR Controller will attach to cubes and stay visible.");
    }

    [ContextMenu("Reset to Default")]
    public void ResetToDefault()
    {
        Debug.Log("🔄 Resetting Virtual Hand...");

        // Remove cyan cube components from parent
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf != null) DestroyImmediate(mf);

        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null) DestroyImmediate(mr);

        BoxCollider bc = GetComponent<BoxCollider>();
        if (bc != null) DestroyImmediate(bc);

        transform.localScale = Vector3.one;

        Debug.Log("✅ Reset complete");
    }
}