using UnityEngine;
using System.Collections.Generic;

public class UserStudyManager : MonoBehaviour
{
    [Header("References")]
    public VirtualHandAttach handAttach;
    public DataLogger dataLogger;
    
    [Header("Objects and Bubbles")]
    public GameObject[] coloredObjects; // 4 colored objects (Red, Green, Blue, Yellow)
    public BubbleTarget[] bubbleTargets; // 4 bubble targets (Red, Green, Blue, Yellow)

    [Header("Legacy Bubble Mode")]
    public bool enableBubbleTargets = false;
    
    [Header("Participant Info")]
    private string currentParticipantID;
    private TrialData currentData;
    
    // Tracking variables
    private int totalAttempts = 0;
    private int successfulPlacements = 0;
    private List<float> taskTimes = new List<float>();
    private List<int> selectionCounts = new List<int>();
    
    // Current task tracking
    private int currentObjectIndex = 0;
    private float taskStartTime = 0f;
    private int currentSelectionCount = 0;
    private GameObject lastSelectedObject = null;

    private const int RequiredPairs = 4;
    
    void Start()
    {
        if (FindFirstObjectByType<SequentialPairVisibility>() != null)
        {
            enableBubbleTargets = false;
        }

        // Initialize data logger
        dataLogger.InitializeSession();
        
        // Get next participant ID automatically
        currentParticipantID = dataLogger.GetNextParticipantID();
        Debug.Log($"Starting study for Participant {currentParticipantID}");
        
        // Subscribe to bubble events
        if (enableBubbleTargets && bubbleTargets != null)
        {
            foreach (BubbleTarget bubble in bubbleTargets)
            {
                if (bubble != null)
                {
                    bubble.OnObjectPlaced += HandleObjectPlaced;
                }
            }
        }
        else
        {
            DisableAllBubbleTargetsInScene();
        }

        // Show only the first object-target pair at startup.
        ShowOnlyCurrentPair();
        
        // Start first task
        taskStartTime = Time.time;
        
        Debug.Log("Study started. User should place each object in matching color bubble.");
    }
    
    void Update()
    {
        // Track selection/deselection for pulling accuracy
        if (handAttach != null)
        {
            GameObject currentObject = handAttach.GetCurrentObject();
            
            if (currentObject != null && currentObject != lastSelectedObject)
            {
                currentSelectionCount++;
                lastSelectedObject = currentObject;
                Debug.Log($"Object selected. Selection count: {currentSelectionCount}");
            }
            else if (currentObject == null && lastSelectedObject != null)
            {
                // Object was deselected
                lastSelectedObject = null;
            }
        }
    }
    
    private void HandleObjectPlaced(GameObject placedObject, string bubbleColor, bool isCorrect)
    {
        // Ignore placements after the 4th pair is complete.
        if (currentObjectIndex >= RequiredPairs)
        {
            return;
        }

        // Ignore events from inactive/non-current pairs.
        if (!IsCurrentPairEvent(placedObject, bubbleColor))
        {
            return;
        }

        totalAttempts++;
        
        if (isCorrect)
        {
            successfulPlacements++;
            float taskTime = Time.time - taskStartTime;
            taskTimes.Add(taskTime);
            selectionCounts.Add(currentSelectionCount);
            
            Debug.Log($"✅ Correct placement! Object placed in {bubbleColor} bubble. Time: {taskTime:F2}s, Selections: {currentSelectionCount}");

            // Hide the exact completed object and target immediately.
            HideCompletedPair(placedObject, bubbleColor);
            
            // Move to next object
            currentObjectIndex++;
            
            // Check if all 4 objects are placed
            if (currentObjectIndex >= RequiredPairs)
            {
                FinishStudy();
            }
            else
            {
                ShowOnlyCurrentPair();

                // Reset for next object
                taskStartTime = Time.time;
                currentSelectionCount = 0;
                lastSelectedObject = null;
            }
        }
        else
        {
            Debug.Log($"❌ Incorrect placement! Object does not match {bubbleColor} bubble.");
        }
    }
    
    private void FinishStudy()
    {
        // Calculate metrics
        float successRate = (float)successfulPlacements / totalAttempts * 100f;
        float errorRate = totalAttempts - successfulPlacements;
        float avgTaskTime = 0f;
        float pullingAccuracy = 0f;
        
        if (taskTimes.Count > 0)
        {
            foreach (float time in taskTimes)
            {
                avgTaskTime += time;
            }
            avgTaskTime /= taskTimes.Count;
        }
        
        // Pulling Accuracy: Lower selection count = higher accuracy
        // If user selects/deselects multiple times, accuracy decreases
        if (selectionCounts.Count > 0)
        {
            float totalSelections = 0;
            foreach (int count in selectionCounts)
            {
                totalSelections += count;
            }
            float avgSelections = totalSelections / selectionCounts.Count;
            
            // Perfect accuracy = 1 selection per object (100%)
            // Each additional selection reduces accuracy
            pullingAccuracy = Mathf.Max(0, (1f - (avgSelections - 1f) * 0.2f)) * 100f;
        }
        
        // Create trial data
        currentData = new TrialData();
        currentData.ParticipantID = currentParticipantID;
        currentData.SuccessRate = successRate;
        currentData.ErrorRate = errorRate;
        currentData.AverageTaskTime = avgTaskTime;
        currentData.PullingAccuracy = pullingAccuracy;
        
        // Log data
        dataLogger.LogParticipantData(currentData);
        
        Debug.Log($"===== Study Complete for Participant {currentParticipantID} =====");
        Debug.Log($"Success Rate: {successRate:F2}%");
        Debug.Log($"Error Rate: {errorRate}");
        Debug.Log($"Average Task Time: {avgTaskTime:F2}s");
        Debug.Log($"Pulling Accuracy: {pullingAccuracy:F2}%");
        Debug.Log($"Data saved to: {dataLogger.GetDataFilePath()}");
        
        // Reset for next participant (optional - can also stop here)
        ResetStudy();
    }
    
    private void ResetStudy()
    {
        // Reset all tracking variables for next participant
        totalAttempts = 0;
        successfulPlacements = 0;
        taskTimes.Clear();
        selectionCounts.Clear();
        currentObjectIndex = 0;
        currentSelectionCount = 0;
        lastSelectedObject = null;
        
        // Get next participant ID
        currentParticipantID = dataLogger.GetNextParticipantID();
        taskStartTime = Time.time;
        
        // Reset all bubbles
        if (enableBubbleTargets && bubbleTargets != null)
        {
            foreach (BubbleTarget bubble in bubbleTargets)
            {
                if (bubble != null)
                {
                    bubble.ResetVisual();
                }
            }
        }

        SequentialPairVisibility sequenceController = FindFirstObjectByType<SequentialPairVisibility>();
        if (sequenceController != null)
        {
            sequenceController.ResetSequence();
        }

        // Start next participant from the first pair only.
        ShowOnlyCurrentPair();
        
        Debug.Log($"Study reset. Ready for Participant {currentParticipantID}");
    }

    private void ShowOnlyCurrentPair()
    {
        int pairCount = RequiredPairs;

        if (coloredObjects == null)
        {
            pairCount = 0;
        }
        else if (enableBubbleTargets)
        {
            pairCount = Mathf.Min(RequiredPairs, coloredObjects.Length, bubbleTargets != null ? bubbleTargets.Length : 0);
        }
        else
        {
            pairCount = Mathf.Min(RequiredPairs, coloredObjects.Length);
        }

        for (int i = 0; i < pairCount; i++)
        {
            bool isCurrent = i == currentObjectIndex;
            SetPairActive(i, isCurrent);
        }
    }

    private void SetPairActive(int index, bool isActive)
    {
        if (coloredObjects != null && index >= 0 && index < coloredObjects.Length && coloredObjects[index] != null)
        {
            coloredObjects[index].SetActive(isActive);
        }

        if (enableBubbleTargets && bubbleTargets != null && index >= 0 && index < bubbleTargets.Length && bubbleTargets[index] != null)
        {
            bubbleTargets[index].gameObject.SetActive(isActive);
            if (isActive)
            {
                bubbleTargets[index].ResetVisual();
            }
        }
    }

    private bool IsCurrentPairEvent(GameObject placedObject, string bubbleColor)
    {
        bool objectMatchesCurrent = coloredObjects != null
            && currentObjectIndex >= 0
            && currentObjectIndex < coloredObjects.Length
            && coloredObjects[currentObjectIndex] != null
            && placedObject == coloredObjects[currentObjectIndex];

        bool bubbleMatchesCurrent = !enableBubbleTargets || (bubbleTargets != null
            && currentObjectIndex >= 0
            && currentObjectIndex < bubbleTargets.Length
            && bubbleTargets[currentObjectIndex] != null
            && string.Equals(bubbleTargets[currentObjectIndex].bubbleColor, bubbleColor, System.StringComparison.OrdinalIgnoreCase));

        return objectMatchesCurrent && bubbleMatchesCurrent;
    }

    private void HideCompletedPair(GameObject placedObject, string bubbleColor)
    {
        if (placedObject != null)
        {
            placedObject.SetActive(false);
        }

        if (!enableBubbleTargets || bubbleTargets == null)
        {
            return;
        }

        for (int i = 0; i < bubbleTargets.Length; i++)
        {
            if (bubbleTargets[i] == null)
            {
                continue;
            }

            bool isColorMatch = string.Equals(bubbleTargets[i].bubbleColor, bubbleColor, System.StringComparison.OrdinalIgnoreCase);
            bool isActive = bubbleTargets[i].gameObject.activeSelf;

            if (isColorMatch && isActive)
            {
                bubbleTargets[i].gameObject.SetActive(false);
                break;
            }
        }
    }

    private void DisableAllBubbleTargetsInScene()
    {
        BubbleTarget[] allBubbles = FindObjectsByType<BubbleTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allBubbles.Length; i++)
        {
            if (allBubbles[i] != null)
            {
                allBubbles[i].gameObject.SetActive(false);
            }
        }
    }
    
    void OnDestroy()
    {
        // Unsubscribe from events
        if (bubbleTargets != null)
        {
            foreach (BubbleTarget bubble in bubbleTargets)
            {
                if (bubble != null)
                {
                    bubble.OnObjectPlaced -= HandleObjectPlaced;
                }
            }
        }
    }
}
