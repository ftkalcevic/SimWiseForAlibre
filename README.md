# SimWiseForAlibre

An Alibre Design 27 add-on that exports the open part or assembly to SimWise 4D, with its
assembly constraints, which SimWise turns into joints. It replaces DST's own plugin,
which refuses to run on anything newer than Alibre 2017.

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
