using UnityEngine;
using System;

[System.Serializable]
public class TrialData
{
    public string ParticipantID;
    public string DateTime;
    public float SuccessRate;
    public float ErrorRate;
    public float AverageTaskTime;
    public float PullingAccuracy;
    
    public TrialData()
    {
        DateTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        SuccessRate = 0f;
        ErrorRate = 0f;
        AverageTaskTime = 0f;
        PullingAccuracy = 0f;
    }
    
    // Convert to CSV row
    public string ToCSV()
    {
        return $"{ParticipantID},{DateTime},{SuccessRate:F2},{ErrorRate:F2},{AverageTaskTime:F2},{PullingAccuracy:F2}";
    }
    
    // CSV Header
    public static string GetCSVHeader()
    {
        return "ParticipantID,DateTime,SuccessRate,ErrorRate,AverageTaskTime,PullingAccuracy";
    }
}
