---
mode: agent
description: 'Implement a new VR interaction technique in this Unity project. Guides through script creation, calibration wiring, spatial gain mapping, UserStudyManager integration, and DataLogger hookup following project conventions.'
---

Implement a new VR interaction technique in this Unity project: **${input:techniqueName}**.

Project root: `c:\Users\rafid_irlab\Documents\GitHub\Practice\Reverse-GoGo-Interaction-Technique`

Load and follow the full checklist in [.github/skills/vr-interaction-technique/SKILL.md](../skills/vr-interaction-technique/SKILL.md).

Key conventions to honour:
- Input via `InputActionProperty` (XRI asset bindings) — never legacy `XR.InputDevice`.
- Spatial gain pattern from `VirtualHandAttach.CalculateSpatialGain()`.
- Guard `StartGrab` with `depthScale.IsArmLengthRecorded()` if the technique is distance-based.
- Log trial data via `DataLogger.LogParticipantData(TrialData)` — never write CSV directly.
- Do **not** modify `VirtualHandAttach`, calibration trigger logic, or existing study scripts unless explicitly requested.

After implementation, run a lightweight compile check:
```
msbuild Assembly-CSharp.csproj /p:Configuration=Debug
```
and report any errors before asking for Unity Play-Mode validation.
