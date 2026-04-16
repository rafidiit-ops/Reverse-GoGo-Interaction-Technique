---
name: vr-interaction-technique
description: 'Implement a new VR interaction technique (Go-Go, Reverse Go-Go, ray-casting, or custom) in this Unity project. Use when adding a grabbing, pulling, or mapping technique, integrating with the XR input system, wiring calibration, or connecting to DataLogger/UserStudyManager. Triggers on: new interaction script, distance-based grab, depth scaling, arm-length calibration, virtual hand, XRI RightHand/Activate.'
argument-hint: 'Name or description of the technique (e.g., "elastic Go-Go", "scaled ray-cast grab")'
---

# Implement a VR Interaction Technique

## Project Conventions

- All technique scripts inherit **`MonoBehaviour`** — no custom base class.
- Composition over inheritance: reference `HandCalibrationDepthScale` if depth scaling is needed.
- Input via **`InputActionProperty`** (XRI asset bindings), not legacy `XR.InputDevice`.
- Data logging goes through `DataLogger` → `TrialData`; never write to files directly.

---

## Checklist

### 1. Create the Script
- [ ] Add `Assets/<TechniqueName>.cs`, inheriting `MonoBehaviour`.
- [ ] Declare `[SerializeField]` references:
  - `controllerTransform (Transform)` — right-hand controller
  - `triggerAction (InputActionProperty)` — XRI RightHand/Activate
  - `depthScale (HandCalibrationDepthScale)` — if technique uses depth scaling
  - `selector (RaycastObjectSelector)` — if technique targets objects via raycast
  - `virtualHand (Transform)` — if a ghost-hand visual is needed

### 2. Implement Core Methods
- [ ] `StartGrab(GameObject target)` — store initial positions; disable target physics (`Rigidbody.isKinematic = true`).
- [ ] `ApplyMovement()` — called from `Update()`; compute mapped position/rotation and assign.
- [ ] `EndGrab()` — restore physics; reset state; re-enable selector highlighting.
- [ ] `IsAttached() → bool` — public accessor (used by `UserStudyManager`).

### 3. Wire Calibration (if distance-based)
- [ ] Reference `HandCalibrationDepthScale` component on the same GameObject or via inspector.
- [ ] Guard `StartGrab` with `depthScale.IsArmLengthRecorded()` — don't grab before calibration.
- [ ] Use `depthScale.GetDepthScalingFactor()` to scale the depth axis (Z) in `ApplyMovement()`.
- [ ] **Do not modify** `TryRecordArmLength()` or the trigger threshold logic.

### 4. Spatial Gain / Mapping Pattern
Follow the pattern from `VirtualHandAttach.CalculateSpatialGain()`:
```
gain = 1.0  when objectDist <= directGrabDistance
gain = exponential curve from 1→N  when objectDist > directGrabDistance
smoothedGain = Lerp(smoothedGain, targetGain, gainSmoothing * Time.deltaTime)
```
Apply filtered `controllerDelta * gain` to the object's world position each frame.

### 5. Connect to UserStudyManager
- [ ] Expose `GetCurrentObject() → GameObject` and `IsAttached()` so `UserStudyManager` can query state.
- [ ] If task completion detection is needed, subscribe to `BubbleTarget.OnObjectPlaced`.

### 6. Data Logging
- [ ] Populate a `TrialData` struct at trial end (task time, errors, technique name).
- [ ] Call `DataLogger.LogParticipantData(trialData)` — never write CSV directly.

### 7. Scene Setup
- [ ] Add the new script as a component on the **XR Rig / Right Hand Controller** GameObject.
- [ ] Assign inspector references: controller, virtualHand, depthScale, selector.
- [ ] Bind `triggerAction` to **XRI RightHand/Activate** in the Input Action asset.
- [ ] Disable any conflicting grabbers (`ReverseGoGoGrab`, `XRReverseGoGo`) on the same object.

### 8. Smoke-Test
- [ ] Calibration required before grabbing (technique blocks grab if not calibrated).
- [ ] Object moves with controller while trigger held.
- [ ] Object released cleanly on trigger up (physics restored, no jitter).
- [ ] Data row appears in `Application.persistentDataPath` CSV after trial.

---

## Key Files for Reference

| File | Purpose |
|------|---------|
| [VirtualHandAttach.cs](../../Assets/VirtualHandAttach.cs) | Full Go-Go implementation — copy patterns from here |
| [HandCalibrationDepthScale.cs](../../Assets/HandCalibrationDepthScale.cs) | Arm-length calibration + exponential depth scale |
| [ReverseGoGoGrab.cs](../../Assets/ReverseGoGoGrab.cs) | Simpler distance-ratio grab — lighter starting point |
| [XRReverseGoGo.cs](../../Assets/XRReverseGoGo.cs) | Z-threshold pull approach |
| [UserStudyManager.cs](../../Assets/UserStudyManager.cs) | How techniques are queried during a study session |
| [DataLogger.cs](../../Assets/DataLogger.cs) | CSV logging API |
