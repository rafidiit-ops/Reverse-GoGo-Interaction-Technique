using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(Renderer))]
public class HoverHighlight : MonoBehaviour
{
    public Material highlightMaterial;

    private Material originalMaterial;
    private Renderer rend;

    private void Awake()
    {
        rend = GetComponent<Renderer>();
        if (rend != null)
        {
            originalMaterial = rend.material;
        }
    }

    public void OnHoverEnter(XRBaseInteractor interactor)
    {
        ApplyHighlight();
    }

    public void OnHoverExit(XRBaseInteractor interactor)
    {
        ClearHighlight();
    }

    public void ApplyHighlight()
    {
        if (rend != null && highlightMaterial != null)
        {
            rend.material = highlightMaterial;
        }
    }

    public void ClearHighlight()
    {
        if (rend != null && originalMaterial != null)
        {
            rend.material = originalMaterial;
        }
    }
}
