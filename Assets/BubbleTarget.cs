using UnityEngine;
using System;

public class BubbleTarget : MonoBehaviour
{
    public string bubbleColor; // "Red", "Green", "Blue", "Yellow"
    public Material normalMaterial;
    public Material successMaterial; // Material when correct object placed
    
    // Event for when object is placed
    public event Action<GameObject, string, bool> OnObjectPlaced;
    
    private Renderer bubbleRenderer;
    private GameObject currentObject;
    
    void Start()
    {
        bubbleRenderer = GetComponent<Renderer>();
        if (bubbleRenderer != null && normalMaterial != null)
        {
            bubbleRenderer.material = normalMaterial;
        }
    }
    
    void OnTriggerEnter(Collider other)
    {
        // Check if it's one of the phantom objects
        if (other.gameObject.name.Contains("Phantom") || other.CompareTag("Grabbable"))
        {
            currentObject = other.gameObject;
            
            // Check if object color matches bubble color
            bool isCorrect = other.gameObject.name.ToLower().Contains(bubbleColor.ToLower());
            
            // Show visual feedback if correct
            if (isCorrect)
            {
                ShowSuccess();
            }
            
            // Notify listeners (UserStudyManager)
            OnObjectPlaced?.Invoke(other.gameObject, bubbleColor, isCorrect);
        }
    }
    
    void OnTriggerExit(Collider other)
    {
        if (other.gameObject == currentObject)
        {
            currentObject = null;
            ResetVisual();
        }
    }
    
    public void ShowSuccess()
    {
        if (bubbleRenderer != null && successMaterial != null)
        {
            bubbleRenderer.material = successMaterial;
        }
    }
    
    public void ResetVisual()
    {
        if (bubbleRenderer != null && normalMaterial != null)
        {
            bubbleRenderer.material = normalMaterial;
        }
    }
    
    public GameObject GetCurrentObject()
    {
        return currentObject;
    }
}
