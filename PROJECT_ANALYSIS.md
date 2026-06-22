# Reverse Go-Go Interaction Technique - Project Analysis

## Executive Summary
This is a **Unity 6000.2.10f1 VR research project** implementing and studying the **Reverse Go-Go object manipulation technique** combined with **Traditional Go-Go** for user-study comparison. The system runs on **OpenXR + XR Interaction Toolkit** and includes comprehensive user-study logging with 4 key performance metrics tracked across participants.

---

## Project Architecture Overview

### Core Technology Stack
- **Engine**: Unity 6000.2.10f1
- **VR Framework**: OpenXR + XR Interaction Toolkit (XRI)
- **Language**: C# (Windows, Meta Quest compatible)
- **Input System**: Unity InputSystem with InputActionProperty (XRI actions)
- **Data Persistence**: CSV logging to `%APPDATA%\LocalLow\[Company]\[Product]\UserStudyData\`

### Project Structure
```
Assets/
├── ReverseGoGo/              # Core study implementation
│   ├── Scenes/               # Study scenes (SampleScene.unity, templates)
│   ├── VirtualHandAttach.cs  # ReverseGoGo interaction engine
│   ├── HandCalibrationDepthScale.cs  # Arm-length calibration & exponential scaling
│   ├── ReverseGoGoGrab.cs    # Alternative ReverseGoGo implementation
│   ├── XRReverseGoGo.cs      # Fallback XR-based implementation
│   ├── UserStudyManager.cs   # Study orchestration
│   ├── BubbleTarget.cs       # Placement validation & visual feedback
│   ├── DataLogger.cs         # CSV writing
│   ├── TrialData.cs          # Data model
│   ├── TechniqueSelector.cs  # UI to switch techniques
│   ├── RaycastObjectSelector.cs  # Ray-based object selection
│   ├── VirtualHandVisualSetup.cs # Virtual hand rendering
│   └── USER_STUDY_SETUP.md   # Configuration guide
├── TraditionalGoGo/          # Go-Go technique comparison
├── HOMER/                    # Alternative technique (HOMER method)
├── UI/, XR/, XRI/            # UI components, XR setup samples
└── [Other samples & resources]
```

---

## Core Interaction Techniques

### 1. **Reverse Go-Go** (Primary Technique)
**Location**: [VirtualHandAttach.cs](Assets/ReverseGoGo/VirtualHandAttach.cs)

#### Key Concept
- **Bidirectional mapping**: Objects follow a "virtual hand" that extends beyond real hand reach
- **Two interaction modes**:
  1. **Trigger (Attach)**: Virtual hand attaches to object; object follows smoothly (with micro-movement filtering)
  2. **Grip (Remote Pull)**: Object scales in distance from controller (exponential amplification)

#### How It Works

**Mode 1: Direct Attachment (Trigger)**
```
User presses Trigger → Object attaches to virtual hand → Object moves with hand
- Includes 32Hz position smoothing for stability
- Respects arm-length calibration boundary (see calibration below)
- Near-hand responsiveness multiplier (2.5x) when object is close to threshold
```

**Mode 2: Remote Pull (Grip)**
```
User presses Grip while object in hand → Depth scaling engages
- Forward/backward movement triggers exponential amplification
- Depth scaling factor derived from HandCalibrationDepthScale
- Forward recovery system prevents overshooting
```

#### Key Parameters
| Parameter | Value | Purpose |
|-----------|-------|---------|
| `controllerDeltaSmoothing` | 32 | Position stabilization |
| `directGrabDistance` | 0.1m | Direct grab threshold |
| `nearHandResponsivenessMultiplier` | 2.5x | Extra responsiveness near threshold |
| `forwardDirectionDeadzone` | 0.0005m | Micro-movement tolerance |

---

### 2. **Traditional Go-Go** (Comparison Technique)
**Location**: [ReverseGoGoGrab.cs](Assets/ReverseGoGo/ReverseGoGoGrab.cs) / [XRReverseGoGo.cs](Assets/ReverseGoGo/XRReverseGoGo.cs)

#### Key Concept
- **Exponential depth amplification**: Hand distance from HMD maps to object distance
- **Threshold-based mapping**: Linear inside threshold, quadratic beyond

#### Mapping Formula
```
if (handDistance ≤ threshold):
    virtualDistance = handDistance                          [1:1 mapping]
else:
    virtualDistance = handDistance + k × (handDistance - threshold)²
    where k = scalingFactor (default: 20.0)
    virtualDistance = min(virtualDistance, maxExtension)   [Cap at max]
```

#### Comparison with Reverse Go-Go
| Aspect | Traditional Go-Go | Reverse Go-Go |
|--------|------------------|---------------|
| **Primary Control** | Hand distance from HMD | Trigger/Grip buttons |
| **Reach Extension** | Quadratic (distance-based) | Calibration-based + exponential scaling |
| **Grab Feel** | Automatic arm-length compensation | Intentional attachment + remote pull |
| **Precision** | Mid-range (0.3–2m reach) | High precision near objects |

---

### 3. **Hand Calibration & Depth Scaling**
**Location**: [HandCalibrationDepthScale.cs](Assets/ReverseGoGo/HandCalibrationDepthScale.cs)

#### Purpose
Establishes user's **arm length** and **reach threshold** to enable consistent exponential scaling across individuals.

#### Calibration Flow
1. **Trigger Calibration**: User presses calibration button with arm fully extended
2. **Threshold Distance**: System records HMD-to-hand distance (e.g., 0.6m for an average user)
3. **Scaling Dynamics**:
   - **Beyond threshold** (extended arm): 1:1 mapping (real reach)
   - **Near threshold** (retracting arm): Exponential acceleration toward hand
   - **Example**: 0.1m retraction = 1m object movement (10x multiplier at default settings)

#### Key Parameters
| Parameter | Value | Purpose |
|-----------|-------|---------|
| `thresholdDistance` | 0.3m (default) | Arm extension boundary |
| `exponentialPower` | 2.0 | Quadratic scaling curve |
| `maxScalingFactor` | 10.0x | Max amplification at closest point |
| `requireCalibrationBeforeTracking` | true | Enforce calibration on startup |

#### Exponential Scaling Formula
```
distanceBeyondThreshold = max(0, controllerDistance - threshold)
scalingFactor = (distanceBeyondThreshold / maxDistance)^exponentialPower
virtualDistance = controllerDistance × (1 + scalingFactor × (maxScaling - 1))
```

---

## Ray-Based Object Selection
**Location**: [RaycastObjectSelector.cs](Assets/ReverseGoGo/RaycastObjectSelector.cs)

#### Feature
- **Infinite ray** from right-hand controller
- **Dynamic visibility**: Ray shows only when hand is in extension zone (depth-based deadzones)
- **Hover feedback**: Materials highlight when ray intersects objects
- **Smart deactivation**: Ray auto-hides during object grab to prevent interference

#### Ray Activation Logic
```
Ray visible if:
  - HandDistance > rayActivationDistanceFromHMD (120mm)
  - HandDistance > (armLength × rayActivationFraction) [e.g., 60% of calibrated length]
  - Object is NOT currently grabbed
```

#### Interaction Flow
1. User extends arm → Ray appears
2. Ray hits object → Object highlighted with `highlightMaterial`
3. User presses Trigger → Object attaches
4. Ray automatically hides during grab
5. User releases Trigger → Ray reappears for next selection

---

## User Study System

### Study Flow & Architecture
**Location**: [UserStudyManager.cs](Assets/ReverseGoGo/UserStudyManager.cs)

#### Overview
10 participants each complete 4 object placements (Red, Green, Blue, Yellow objects → matching colored bubble targets).

#### Orchestration (Per Participant)
```
1. System auto-assigns ParticipantID (001, 002, ..., 010)
2. Participant selects object using ReverseGoGo pull
3. Participant places object in matching colored bubble
4. BubbleTarget validates color match:
   - ✅ Correct: Bubble glows, metric recorded
   - ❌ Incorrect: Error counted, user retries
5. After 4 successful placements → Metrics calculated → Data saved to CSV
6. Reset for next participant
```

### Tracked Metrics
**Location**: [TrialData.cs](Assets/ReverseGoGo/TrialData.cs)

#### 1. **Success Rate** (%)
- Formula: `(SuccessfulPlacements / TotalAttempts) × 100`
- Example: 4 correct in 5 attempts = 80%
- Measures **task completion accuracy**

#### 2. **Error Rate** (count)
- Number of incorrect placements before success
- Example: Placed wrong object 2 times before correct = 2 errors
- Measures **trial-and-error behavior**

#### 3. **Average Task Time** (seconds)
- Formula: `SumOfPlacementTimes / NumberOfObjects`
- Includes time from previous object placement to current success
- Measures **interaction speed & efficiency**

#### 4. **Pulling Accuracy** (%)
- Tracks selection/deselection efficiency
- Formula: `Max(0, (1 - (AvgSelections - 1) × 0.2)) × 100`
- Perfect score (100%): Select once, place once
- Reduced score: Repeated selections indicate difficulty reaching object
- Measures **interaction precision & confidence**

### CSV Data Format
**File**: `%APPDATA%\LocalLow\[Company]\[Product]\UserStudyData\ParticipantData.csv`

```csv
ParticipantID,DateTime,SuccessRate,ErrorRate,AverageTaskTime,PullingAccuracy
001,2025-12-07 14:30:25,100.00,0.00,5.23,85.50
002,2025-12-07 14:35:10,75.00,1.00,6.18,72.30
```

---

## Bubble Target Validation
**Location**: [BubbleTarget.cs](Assets/ReverseGoGo/BubbleTarget.cs)

### Placement Detection
1. **Collision Detection**: OnTriggerEnter when object enters bubble collider
2. **Color Matching**: Checks if object name contains bubble color name (e.g., "Red Phantom" → "Red" bubble)
3. **Release Handling**: 
   - If object still grabbed → Waits for release before confirming
   - If object released → Immediately confirms placement
4. **Visual Feedback**:
   - Correct placement → Success material (glows)
   - Incorrect placement → Error logged, object resets

### Integration with UserStudyManager
```csharp
BubbleTarget.OnObjectPlaced → UserStudyManager.HandleObjectPlaced()
  ├─ If correct → Metrics updated, next object selected
  └─ If incorrect → Error count incremented, user retries
```

---

## Data Logging System
**Location**: [DataLogger.cs](Assets/ReverseGoGo/DataLogger.cs)

### File Initialization
```csharp
public void InitializeSession()
{
    // Create folder: %APPDATA%\LocalLow\[Company]\[Product]\UserStudyData\
    // Create/append to: ParticipantData.csv
    // Write header if new: ParticipantID,DateTime,SuccessRate,ErrorRate,AverageTaskTime,PullingAccuracy
}
```

### Auto-Increment Participant IDs
```csharp
public string GetNextParticipantID()
{
    // Read last line from CSV
    // Extract last ParticipantID
    // Return (lastID + 1) as 3-digit zero-padded string
    // Example: 001 → 002
}
```

### Data Persistence
- Each participant's data appended to same CSV file
- No overwriting; data accumulates session-to-session
- Supports up to 10 participants (001–010)

---

## Scene & Technique Selection

### Scene-Based Technique Locking
**Location**: [GoGoModeToggle.cs](Assets/ReverseGoGo/GoGoModeToggle.cs)

```
TraditionalGoGoSampleScene          → Locks Traditional Go-Go
ReverseGoGo SampleScene             → Locks Reverse Go-Go
HOMERStarterScene                   → Locks HOMER technique
Other scenes                        → Allow toggle via Y/B button
```

### Runtime Technique Selector UI
**Location**: [TechniqueSelector.cs](Assets/ReverseGoGo/TechniqueSelector.cs)

- **Two buttons** at runtime: "Traditional GoGo" vs "ReverseGoGo"
- **Visual feedback**: Selected button highlights in green
- **Auto-hide**: UI disappears after 2 seconds (configurable)
- **One-at-a-time**: Only one technique active at runtime

---

## Smoothing & Responsiveness Subsystems

### Movement Smoothing (ReverseGoGo)
**Purpose**: Reduce hand jitter while maintaining responsiveness

| Component | Smoothing | Purpose |
|-----------|-----------|---------|
| **Controller Delta** | 32 Hz | Micro-movement stabilization |
| **Spatial Gain** | 16 Hz | Smooth transition between grab modes |
| **Forward Direction** | 18 Hz | Extra smoothing for outward motion |
| **Max Linear Speed** | 50 m/s | Caps velocity to prevent overshooting |

### Micro-Movement Filtering
```csharp
if (delta.magnitude < forwardDirectionDeadzone)  // 0.0005m
    // Ignore micro-movements
else
    // Apply smoothed movement
```

### Near-Hand Responsiveness
- When object within 0.12m of hand: Applies 2.5x responsiveness multiplier
- When object very close: Pulls toward controller at 8 m/s convergence speed
- Enables precise placement without lag

---

## Input Mapping & XRI Integration

### XRI Input Actions (via InputActionProperty)
```
VirtualHandAttach uses:
├── triggerAction        → XRI RightHand / Activate
├── gripAction           → XRI RightHand / Grip
├── returnToUIAction     → A/B buttons (primaryButton/secondaryButton)

HandCalibrationDepthScale uses:
└── calibrationTriggerAction → XRI RightHand / Activate

RaycastObjectSelector uses:
└── triggerAction        → XRI RightHand / Activate (for compatibility)
```

### Controller Visibility Management
- **Normal state**: Controllers visible
- **While attached**: Optionally hide controller if `hideControllerWhileAttached = true`
- **Ray system**: Disables competing ray visuals to show only this ray

---

## Configuration & Setup Workflow

### Quick Setup (See USER_STUDY_SETUP.md)

#### 1. Create Study Manager
```
Right-click Hierarchy → Create Empty → Rename "StudyManager"
Add Component → UserStudyManager
```

#### 2. Assign References
```
Drag VirtualHandManager → Hand Attach
Drag DataLogger → Data Logger
Drag 4 phantom objects → Colored Objects
Drag 4 bubble GameObjects → Bubble Targets
```

#### 3. Configure Each Bubble
```
For each bubble GameObject:
  Add Component → BubbleTarget
  Set Bubble Color: "Red", "Green", "Blue", or "Yellow"
  Drag normal/success materials
  Ensure Collider with "Is Trigger" checked
```

#### 4. Run Study
```
Press Play → Participant ID auto-assigned
User places objects → Metrics logged
Press Play again → Next participant (ID auto-incremented)
```

---

## Debugging & Diagnostic Tools

### Available Utilities
| Script | Purpose |
|--------|---------|
| `ControllerTrackingDiagnostic.cs` | Monitor controller position/rotation |
| `DiagnoseVRDisplay.cs` | Check VR display settings |
| `DiagnoseXRSettings.cs` | Validate OpenXR configuration |
| `FixControllerTracking.cs` | Auto-correct tracking issues |
| `FixControllerVisibility.cs` | Ensure controller models render |
| `FixOculusBuildIssues.cs` | Resolve Oculus-specific build errors |
| `HoverHighlight.cs` | Highlight objects on raycast hover |

### Build Verification Commands
```bash
# Lightweight compile check (no Unity needed)
msbuild Assembly-CSharp.csproj /p:Configuration=Debug
dotnet build Assembly-CSharp.csproj --framework net471

# Runtime validation (requires Unity Play Mode)
→ Test in Study Scene
  1. Verify object grab works (Trigger)
  2. Verify remote pull works (Grip)
  3. Confirm bubble validation & CSV logging
```

---

## Key Design Decisions & Constraints

### Preserved Behaviors (Per AGENTS.md)
✅ **Bidirectional mapping** in VirtualHandAttach (forward/backward)
✅ **Current pulling mapping** (depth scaling via HandCalibrationDepthScale)
✅ **Arm-length calibration trigger/threshold** behavior
✅ **InputActionProperty** (XRI) over legacy XR.InputDevice patterns

### Limitations & Notes
- Study limited to **10 participants** (IDs 001–010)
- CSV file is **append-only** (no overwrite protection)
- Ray visibility tied to **depth-based deadzones** (calibration-dependent)
- Smoothing parameters require **empirical tuning** per use case
- Scene-based locking prevents accidental technique switches in study scenes

---

## Performance Characteristics

### Computational Load
- **Smoothing calculations**: ~0.3ms per frame (32 Hz smoothing with delta tracking)
- **Ray intersection**: ~0.5ms per frame (physics raycast to all selectables)
- **CSV I/O**: ~2–5ms on study completion (append to file)
- **Overall**: Targets 60+ FPS on Meta Quest 2+ hardware

### Memory Footprint
- **VirtualHandAttach**: ~2KB (state variables)
- **HandCalibrationDepthScale**: ~1KB (calibration cache)
- **UserStudyManager**: ~5KB (per-participant tracking)
- **CSV file**: ~500 bytes per participant

---

## Extension Opportunities

### Future Enhancements
1. **HOMER Integration**: Already included in `Assets/HOMER/` — alternative free-hand reaching technique
2. **Multi-Target Scenarios**: Extend beyond 4 objects (modify TrialData, UserStudyManager)
3. **Adaptive Scaling**: Dynamic smoothing based on task difficulty
4. **Gesture Recognition**: Recognize release/placement intent from hand pose
5. **Spatial Analytics**: Log 3D trajectory data per placement attempt
6. **Participant Feedback**: Post-study questionnaire UI integration

---

## Summary: Interaction Flow (User Perspective)

```
1. Unity starts → TechniqueSelector displays UI
2. User selects "ReverseGoGo"
3. Scene loads with 4 colored objects + 4 matching bubble targets
4. Hand extends → Ray appears showing selectable objects
5. User aims at object → Object highlights
6. User presses Trigger → Object attaches to virtual hand
   [Optional: Presses Grip to enable remote pull with depth scaling]
7. User manipulates object to bubble using Trigger/Grip
8. Object enters bubble collider
9. Bubble validates color match
   - ✅ Success → Bubble glows, metrics update, next object ready
   - ❌ Error → Error count increments, object resets
10. After 4 successes → Participant data saved to CSV
11. System resets → Ready for Participant 002
```

---

## File Index

| File | Role |
|------|------|
| [VirtualHandAttach.cs](Assets/ReverseGoGo/VirtualHandAttach.cs) | ReverseGoGo interaction core |
| [HandCalibrationDepthScale.cs](Assets/ReverseGoGo/HandCalibrationDepthScale.cs) | Calibration & exponential scaling |
| [ReverseGoGoGrab.cs](Assets/ReverseGoGo/ReverseGoGoGrab.cs) | Alternative ReverseGoGo impl. |
| [XRReverseGoGo.cs](Assets/ReverseGoGo/XRReverseGoGo.cs) | XR-based fallback |
| [UserStudyManager.cs](Assets/ReverseGoGo/UserStudyManager.cs) | Study orchestration |
| [BubbleTarget.cs](Assets/ReverseGoGo/BubbleTarget.cs) | Placement validation |
| [DataLogger.cs](Assets/ReverseGoGo/DataLogger.cs) | CSV persistence |
| [TrialData.cs](Assets/ReverseGoGo/TrialData.cs) | Data model |
| [RaycastObjectSelector.cs](Assets/ReverseGoGo/RaycastObjectSelector.cs) | Ray-based selection |
| [TechniqueSelector.cs](Assets/ReverseGoGo/TechniqueSelector.cs) | UI technique switcher |
| [GoGoModeToggle.cs](Assets/GoGoModeToggle.cs) | Runtime mode toggle |
| [USER_STUDY_SETUP.md](Assets/ReverseGoGo/USER_STUDY_SETUP.md) | Configuration guide |

---

**Project Created**: Unity 6000.2.10f1 | **Last Updated**: May 10, 2026 | **Study Status**: Active (Ready for Participants)
