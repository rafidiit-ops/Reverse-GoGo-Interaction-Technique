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
    
    void Start()
    {
        // Initialize data logger
        dataLogger.InitializeSession();
        
        // Get next participant ID automatically
        currentParticipantID = dataLogger.GetNextParticipantID();
        Debug.Log($"Starting study for Participant {currentParticipantID}");
        
        // Subscribe to bubble events
        foreach (BubbleTarget bubble in bubbleTargets)
        {
            bubble.OnObjectPlaced += HandleObjectPlaced;
        }
        
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
        totalAttempts++;
        
        if (isCorrect)
        {
            successfulPlacements++;
            float taskTime = Time.time - taskStartTime;
            taskTimes.Add(taskTime);
            selectionCounts.Add(currentSelectionCount);
            
            Debug.Log($"✅ Correct placement! Object placed in {bubbleColor} bubble. Time: {taskTime:F2}s, Selections: {currentSelectionCount}");
            
            // Move to next object
            currentObjectIndex++;
            
            // Check if all 4 objects are placed
            if (currentObjectIndex >= 4)
            {
                FinishStudy();
            }
            else
            {
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
        foreach (BubbleTarget bubble in bubbleTargets)
        {
            bubble.ResetVisual();
        }
        
        Debug.Log($"Study reset. Ready for Participant {currentParticipantID}");
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
