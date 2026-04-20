using UnityEngine;
using System.IO;
using System;

public class DataLogger : MonoBehaviour
{
    private string dataFolderPath;
    private string participantDataPath;
    
    public void InitializeSession()
    {
        // Create data folder in persistent data path
        dataFolderPath = Path.Combine(Application.persistentDataPath, "UserStudyData");
        
        if (!Directory.Exists(dataFolderPath))
        {
            Directory.CreateDirectory(dataFolderPath);
        }
        
        // Single file for all participants
        participantDataPath = Path.Combine(dataFolderPath, "ParticipantData.csv");
        
        // Create file with header if it doesn't exist
        if (!File.Exists(participantDataPath))
        {
            File.WriteAllText(participantDataPath, TrialData.GetCSVHeader() + "\n");
            Debug.Log($"Created new data file at: {participantDataPath}");
        }
        else
        {
            Debug.Log($"Using existing data file at: {participantDataPath}");
        }
    }
    
    public void LogParticipantData(TrialData data)
    {
        try
        {
            // Append data to CSV
            File.AppendAllText(participantDataPath, data.ToCSV() + "\n");
            Debug.Log($"Logged data for Participant {data.ParticipantID}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to log data: {e.Message}");
        }
    }
    
    public string GetNextParticipantID()
    {
        if (!File.Exists(participantDataPath))
        {
            return "001";
        }
        
        try
        {
            string[] lines = File.ReadAllLines(participantDataPath);
            
            // If only header exists, start from 001
            if (lines.Length <= 1)
            {
                return "001";
            }
            
            // Get last participant ID from last line
            string lastLine = lines[lines.Length - 1];
            string[] fields = lastLine.Split(',');
            
            if (fields.Length > 0 && int.TryParse(fields[0], out int lastID))
            {
                int nextID = lastID + 1;
                return nextID.ToString("D3"); // Format as 3 digits (001, 002, etc.)
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to read participant ID: {e.Message}");
        }
        
        return "001";
    }
    
    public string GetDataFilePath()
    {
        return participantDataPath;
    }
}
