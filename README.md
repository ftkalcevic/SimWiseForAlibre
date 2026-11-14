# SimWiseForAlibre

An Alibre Design 27 add-on that exports the open part or assembly to SimWise 4D, with its
assembly constraints, which SimWise turns into joints. This is a compatibility project 
as DST's own plugin only works with the version of Alibre it came with.

## Needs

- Alibre Design 27.0.1 (the project references its `AlibreX.dll` and `AlibreAddOn.dll` in
  `C:\Program Files\Alibre Design 27.0.1.27039\Program`; set `AlibreProgram` to build against another).
- SimWise 4D installed (for `SimWiseExport.dll`).
- The .NET SDK to build (the add-on targets .NET Framework 4.8, because that's the runtime Alibre hosts).

## Build and install

Close Alibre, then:

```
powershell -ExecutionPolicy Bypass -File deploy\install.ps1
```

It builds, asks for administrator rights, copies the add-on to
`C:\ProgramData\Alibre AddOns\SimWiseForAlibre\` and registers it. Start Alibre again.

## Use

Open a saved part or assembly and choose **SimWise > Export to SimWise...**. Pick where to save the
`.wm3`; SimWise starts and opens it. A summary lists what was exported and what was left out.
**SimWise > Show Log** opens the log (`%LOCALAPPDATA%\SimWiseForAlibre\SimWiseForAlibre.log`).

## What's exported

- Every visible, unsuppressed part occurrence becomes a body, placed where it is in Alibre, with its
  colour, material and density (SimWise works out the mass). Anchored (fixed) parts are anchored.
- Sub-assemblies are flattened. A rigid sub-assembly's parts are held together with rigid joints; a
  flexible one's own constraints are exported.
- Constraints: Mate and Align between planes (coincident, or distance when offset), between axes
  (concentric) and between points; Orient (parallel); Angle. Constraints to the assembly's own
  planes, axes and points attach to the ground.
- Not yet: tangent, gear, rack, screw and fastener constraints, Mate or Align between a plane and an
  axis or point, and offset axes. They're listed in the summary.

## To do

### Built but not yet tested
- Sub-assemblies: a rigid one's parts held together with rigid joints (coords marked `swUnknownFeature`).
- Sub-assemblies: a flexible one's own constraints exported, and constraints that use the sub-assembly's own planes, axes and points (reported as not exported).
- Orient constraints, exported as parallel.
- Angle constraints.
- Mate or Align with an offset between planes or points, exported as a distance constraint.
- Mate or Align between two points (vertices, design points, sphere centres).
- Mass for parts with no material: test1's parts came out at 1,000,000 kg/m³. The log's `[mass]` lines now give the raw mass, volume and units to find why.
- Parts in other model units (mm, inches): placement, ACIS scale and mass.
- Hidden and suppressed parts and suppressed constraints, which are left out.
- A constraint with an error in Alibre, which is reported and left out.
- Exporting a single part (only the first test, before the fixes, so retest).
- Exporting again over an existing `.wm3`: SimWise should update only the bodies whose files changed (update stamps).

### Not built yet
- Tangent inside and outside constraints (SimWise has `swTangent`).
- Gear, rack and screw constraints, and fasteners.
- Mate or Align between a plane and an axis, or a plane and a point.
- Axes aligned with an offset between them (now reported and left out).
- Constraint limits (at least, at most, between) as SimWise joint limits.
- Sub-assemblies kept as SimWise sub-assemblies (`NewSubAssembly`) instead of flattened.
- A camera matching Alibre's current view (`SWCamera`).
- The progress dialog the API offers (`StartProgressDialog`, `BumpProgress`) for large assemblies.
- Parasolid instead of ACIS (`SetParasolidFileName`), if ACIS turns out to lose geometry.
- Configurations: export the active configuration's parts and colours.
- An option to send Alibre's own mass properties (`AutoComputeCOMs` off, `SetMass`, `SetInertia`, `SetCMLocation`) rather than having SimWise compute them.
- A ribbon icon for Export to SimWise.
- Motors, actuators and springs defined in Alibre (Frank asked, deferred). The Export API can't make them, but SimWise's
  automation API can, after `DoExport` has opened the model: `GetObject(, "WM3D.Application")`, `ActiveDocument`,
  `doc.Find(coord name)`, `doc.Constraints.CreateConstraint(wmMotor, wmJoinInPlace, c1, c2)`,
  `MotorValue.Formula` (a value, or `input[n]` for a slider from `doc.Inputs.Add`). Unproven: that the calls
  reach SimWise from Alibre's process, and how to choose the motor type (`MotorType` is read-only). A motor is a
  revolute joint, so the Alibre constraints that already make that joint may need leaving out. First step: a probe
  that adds a velocity motor to the first concentric constraint. Still to decide: where motors are kept in Alibre
  (a dialog saved in the assembly with the add-on's data, a constraint naming convention, or a sidecar file).
  Frank's concern: exporting again updates the existing `.wm3`, so motors added on an earlier export (by the
  add-on or by hand in SimWise) would have to be found and kept or replaced, not added twice.
- Check the SimWise version and say plainly when SimWise isn't installed.

## Files

| File | What it does |
|---|---|
| `src\SimWiseForAlibre\AlibreAddOn.cs` | The entry points Alibre calls |
| `src\SimWiseForAlibre\AddOnMenu.cs` | The SimWise menu |
| `src\SimWiseForAlibre\AlibreWindow.cs` | Alibre's main window, as the owner that makes the add-on's boxes modal |
| `src\SimWiseForAlibre\Exporter.cs` | Walks the design and calls the SimWise Export API |
| `src\SimWiseForAlibre\Geometry.cs` | Units, placements, and reading constraint geometry |
| `lib\Interop.SimWiseExportLib.dll` | The imported SimWise type library (embedded at build) |
| `tools\MakeInterop.ps1` | Regenerates it from a new SimWise install |
| `docs\ExportAPI.md` | Notes on the SimWise Export API |
