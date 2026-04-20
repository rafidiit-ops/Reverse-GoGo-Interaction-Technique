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
    private bool waitingForRelease = false;  // Track if we're waiting for object to be released
    
    void Start()
    {
        bubbleRenderer = GetComponent<Renderer>();
        if (bubbleRenderer != null && normalMaterial != null)
        {
            bubbleRenderer.material = normalMaterial;
        }
    }
    
    void Update()
    {
        // If we're waiting for an object to be released, check if it's released now
        if (waitingForRelease && currentObject != null)
        {
            TraditionalGoGoInteraction gogoController = FindFirstObjectByType<TraditionalGoGoInteraction>();
            
            // Check if object is no longer being held
            if (gogoController == null || gogoController.GetCurrentObject() != currentObject)
            {
                // Object was released! Now process it
                Debug.Log($"✅ Object {currentObject.name} released in bubble - processing now");
                waitingForRelease = false;
                
                // Check if object color matches bubble color
                bool isCorrect = currentObject.name.ToLower().Contains(bubbleColor.ToLower());
                
                // Show visual feedback if correct
                if (isCorrect)
                {
                    ShowSuccess();
                }
                
                // Notify listeners (UserStudyManager)
                OnObjectPlaced?.Invoke(currentObject, bubbleColor, isCorrect);
            }
        }
    }
    
    void OnTriggerEnter(Collider other)
    {
        // Check if it's one of the phantom objects
        if (other.gameObject.name.Contains("Phantom"))
        {
            // Check if object is currently being held by TraditionalGoGo
            TraditionalGoGoInteraction gogoController = FindFirstObjectByType<TraditionalGoGoInteraction>();
            if (gogoController != null && gogoController.GetCurrentObject() == other.gameObject)
            {
                // Object is being held - don't process yet, wait for release
                Debug.Log($"⏳ Object {other.gameObject.name} in bubble but still being held - waiting for release");
                currentObject = other.gameObject;
                waitingForRelease = true;
                return;
            }
            
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
            waitingForRelease = false;
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
