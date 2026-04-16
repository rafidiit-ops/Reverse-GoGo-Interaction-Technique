# AGENTS.md

## Purpose
This file helps coding agents work safely and quickly in this Unity VR user-study project.

## Project Snapshot
- Engine/tooling: Unity 6000.2.10f1, OpenXR + XR Interaction Toolkit.
- Language/runtime: C# scripts under Assets, generated solution/project files in repo root.
- Main domain: Reverse Go-Go style object pulling + user-study logging.

## High-Value References
- Study setup and metrics: [Assets/USER_STUDY_SETUP.md](Assets/USER_STUDY_SETUP.md)
- VR implementation workflow skill: [.github/skills/vr-interaction-technique/SKILL.md](.github/skills/vr-interaction-technique/SKILL.md)

## Where Core Logic Lives
- Interaction mapping and object pull behavior: [Assets/VirtualHandAttach.cs](Assets/VirtualHandAttach.cs)
- Arm-length calibration and depth scaling: [Assets/HandCalibrationDepthScale.cs](Assets/HandCalibrationDepthScale.cs)
- Alternative grab techniques: [Assets/ReverseGoGoGrab.cs](Assets/ReverseGoGoGrab.cs), [Assets/XRReverseGoGo.cs](Assets/XRReverseGoGo.cs)
- User-study flow orchestration: [Assets/UserStudyManager.cs](Assets/UserStudyManager.cs)
- Placement events/validation: [Assets/BubbleTarget.cs](Assets/BubbleTarget.cs)
- Data logging APIs and CSV writing: [Assets/DataLogger.cs](Assets/DataLogger.cs), [Assets/TrialData.cs](Assets/TrialData.cs)

## Build and Verification
Use lightweight compile checks before asking for Unity play-mode validation:

1. `msbuild Assembly-CSharp.csproj /p:Configuration=Debug`
2. `dotnet build Assembly-CSharp.csproj --framework net471`

When scene/runtime behavior changes, validate in Unity Play Mode with the relevant XR scene.

## Agent Guardrails
- Preserve existing VirtualHandAttach mapping behavior unless explicitly requested:
  - forward/backward bidirectional mapping behavior
  - current pulling mapping
- Preserve arm-length calibration trigger/threshold behavior unless explicitly requested.
- Do not edit unrelated scene files when the user asks for focused scene/script changes.
- Prefer InputActionProperty (XRI input actions) over introducing legacy XR.InputDevice patterns.
- Use DataLogger and TrialData for study data changes; do not add ad-hoc file writing.

## Working Conventions
- Keep script edits minimal and targeted; avoid broad refactors unless requested.
- Keep public APIs stable when used by UserStudyManager or other study scripts.
- If changing study flow, ensure BubbleTarget event wiring still reaches UserStudyManager.
- If introducing a new interaction technique, follow the workflow in [.github/skills/vr-interaction-technique/SKILL.md](.github/skills/vr-interaction-technique/SKILL.md).

## If Unsure
- Ask before changing calibration math, pull-direction mapping, or participant logging schema.
- Link to existing docs instead of duplicating long setup instructions.
