using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(Renderer))]
public class HoverHighlightAlt : MonoBehaviour
{
    public Material highlightMaterial;

    private Material originalMaterial;
    private Renderer rend;

    private void AwakeAlt()
    {
        rend = GetComponent<Renderer>();
        if (rend != null)
        {
            originalMaterial = rend.material;
        }
    }

    public void OnHoverEnterAlt(XRBaseInteractor interactor)
    {
        ApplyHighlightAlt();
    }

    public void OnHoverExitAlt(XRBaseInteractor interactor)
    {
        ClearHighlightAlt();
    }

    public void ApplyHighlightAlt()
    {
        if (rend != null && highlightMaterial != null)
        {
            rend.material = highlightMaterial;
        }
    }

    public void ClearHighlightAlt()
    {
        if (rend != null && originalMaterial != null)
        {
            rend.material = originalMaterial;
        }
    }
}
