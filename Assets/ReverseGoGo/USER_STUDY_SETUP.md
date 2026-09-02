# User Study Setup Guide - Simplified Version

## Overview
This simplified user study tracks 4 key metrics for 10 participants as they place colored objects into matching colored bubbles.

## Metrics Tracked
1. **Success Rate** - % of correct placements
2. **Error Rate** - Count of incorrect placements
3. **Average Task Time** - Mean time to complete each placement (seconds)
4. **Pulling Accuracy** - Measures how many times user selects/deselects objects (lower = better)

## Data File
- **Location**: `C:\Users\[YourUsername]\AppData\LocalLow\[CompanyName]\[ProductName]\UserStudyData\ParticipantData.csv`
- **Format**: Single CSV file containing all 10 participants
- **Columns**: ParticipantID, DateTime, SuccessRate, ErrorRate, AverageTaskTime, PullingAccuracy
- **Participant IDs**: Automatically numbered 001-010 based on previous entries

## Setup Steps

### 1. Create Study Manager GameObject
1. In Unity Hierarchy, right-click → Create Empty
2. Rename to "StudyManager"
3. Add Component → User Study Manager script

### 2. Configure Study Manager
In the Inspector for StudyManager:

**References:**
- **Hand Attach**: Drag your VirtualHandManager GameObject here
- **Data Logger**: Drag the DataLogger component (create GameObject with DataLogger if needed)

**Objects and Bubbles:**
- **Colored Objects (Size: 4)**: Add your 4 phantom objects in any order
  - Element 0: Red Phantom
  - Element 1: Green Phantom
  - Element 2: Blue Phantom
  - Element 3: Yellow Phantom
  
- **Bubble Targets (Size: 4)**: Add your 4 bubble GameObjects in any order
  - Element 0: Red Bubble
  - Element 1: Green Bubble
  - Element 2: Blue Bubble
  - Element 3: Yellow Bubble

### 3. Configure Each Bubble
For each of the 4 bubble GameObjects:

1. Add Component → Bubble Target script
2. Set **Bubble Color**: Type the color name exactly ("Red", "Green", "Blue", or "Yellow")
3. Set **Normal Material**: Drag the transparent bubble material
4. Set **Success Material**: Drag a brighter/glowing material for correct placement feedback
5. Ensure the bubble has a Collider with "Is Trigger" checked

### 4. Study Flow

**For Each Participant (Automatic):**
1. Press Play in Unity
2. Participant ID is automatically assigned (001, 002, 003... up to 010)
3. User places all 4 objects in matching colored bubbles
4. After 4th object placed correctly, data is saved to CSV
5. Study automatically resets for next participant

**How It Works:**
- User selects an object using ReverseGoGo pull
- User places object in a bubble
- If correct: Bubble glows with success material, placement counted
- If incorrect: Error is counted, user tries again
- System tracks how many times user selects/deselects each object
- After 4 successful placements, metrics are calculated and saved

### 5. Accessing Data

**Find Your CSV File:**
1. Press `Windows + R`
2. Type: `%userprofile%\AppData\LocalLow`
3. Navigate to: `[CompanyName]\[ProductName]\UserStudyData\`
4. Open `ParticipantData.csv` in Excel

**Data Format:**
```
ParticipantID,DateTime,SuccessRate,ErrorRate,AverageTaskTime,PullingAccuracy
001,2025-12-07 14:30:25,100.00,0.00,5.23,85.50
002,2025-12-07 14:35:10,75.00,1.00,6.18,72.30
...
```

## Metrics Explanation

**Success Rate:**
- Percentage of correct placements
- Formula: (Successful Placements / Total Attempts) × 100
- Example: 4 correct out of 5 attempts = 80%

**Error Rate:**
- Total number of incorrect placement attempts
- Example: If user places wrong object in bubble 2 times before correct = 2 errors

**Average Task Time:**
- Average time (in seconds) to place each object correctly
- Measured from when previous object placed to when current object placed
- Example: 4 objects in 20 seconds total = 5.0s average

**Pulling Accuracy:**
- Measures efficiency of object selection
- Based on how many times user selects/deselects objects
- Perfect score (100%): Select once, place once
- Reduced score: Select, release, select again (indicates difficulty reaching object)
- Formula: Max(0, (1 - (AvgSelections - 1) × 0.2)) × 100

## Notes
- Study automatically progresses through all 10 participants
- Each participant must complete all 4 placements
- Data is saved immediately after each participant completes
- Previous participant data is never overwritten
- Participant IDs increment automatically based on CSV file
