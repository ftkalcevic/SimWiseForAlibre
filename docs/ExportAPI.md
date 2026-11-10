# SimWise Export API: what we use and what we found

The reference is DST's `SimWise Export API.pdf` (2012) in
`C:\Program Files\DST\SimWise 4D for Alibre V9.86\SDK\Export API\`, with three C# examples.
The library is `SimWiseExport.dll` (x64, ProgID `SimWiseExport.SWExport`, type library
`{9C9B00F5-D918-4ED5-A7A1-0AE2B5501798}`), the same one DST's own Alibre plugin (`SWAlibre.dll`) uses.

## Flow

1. `new SWExport()`, `Init(CADType, resourceInstance, window)`. `CADType.Alibre` (10) exists, though the PDF doesn't list it.
2. `SetUnitSystem(UnitSystem, false, false)` sets the units SimWise shows. **Every value passed to the API is SI** (metres, kg).
3. `NewBody()` per part instance: `Name`, `id`, `SetLocation(double[3])`, `SetOrientation(double[9])`, `SetAcisFileName` (or Parasolid, STEP, IGES), `SetColor` (0 to 1), `SetOpacity`, `Anchored`, `SetUpdateStamp` (SimWise updates a body only when its stamp changes), `Density`, `MaterialName`.
   The PDF says each instance needs its own geometry file.
4. `NewCoord(true)` per constraint side: `SetReferenceToBody`, `SetFeatureType` (`swPlane`, `swCylinder`, `swEdge`, `swVertex`, `swCircle`, `swUnknownFeature`), `SetOffset(double[3])`, `SetXForm(SWbvect33, transpose)`.
   A coord on a body is given in **the body's own frame** (its origin and axes, in metres), as the PDF's
   description of `SetReferenceToBody` says. The PDF's examples only have bodies at the origin, unrotated,
   where world and body frames agree. Giving world values put Frank's hinge coords in the wrong place; in the
   body's frame the hinge simulates correctly (proven by Frank, 2026-11-10). `SWCoord.SetOrientation` takes
   the coord's axes as rows in the body's frame, like `SWBody.SetOrientation`.
   A coord with no body is in world coordinates.
5. `NewConstraint(isTopLevel)`, `SetCoords(c1, c2)`, `SetMateType` (`swCoincident`, `swConcentric`, `swParallel`, `swAngle`, `swDistance`, `swRigidJoint`, ...), `Name`. SimWise combines constraints into joints (for example coincident planes plus concentric cylinders make a revolute joint).
6. `DoExport(cadPath, wm3Path)` writes the `.wm3`, starts SimWise and opens it. Then `TerminateExport()`.

`AutoComputeCOMs = true` makes SimWise compute the mass properties from the geometry and density.

## Proven by probing the library (2026-11-10)

- `SWbvect33` stores its 9 numbers by rows (`Init(1..9)` then `ExtractRow(0)` gives 1, 2, 3).
- `VectorToXForm("z", v)` puts `v` in the **third column** of an `SWbvect33`.
- `SWBody.SetOrientation(double[9])` takes the body's X, Y and Z axes as **rows**, Alibre's own layout
  (proven by Frank on test1: the transpose turned a part rotated +32 degrees to -32). So `SWbvect33` is
  not used for placements; coords use `SWCoord.SetOrientation` with the same row layout (not yet proven).
  `SIMWISEFORALIBRE_TRANSPOSE=1` gives the other layout for both.
- SimWise doesn't register the type library for x64, so late binding (PowerShell, `dynamic`) fails with TYPE_E_LIBNOTREGISTERED.
  Early binding through `lib\Interop.SimWiseExportLib.dll` (made by `tools\MakeInterop.ps1`) works.
- The objects can be created without SimWise running. `SimWiseVersion` reads 8.

- `swConcentric` between two coords marked `swEdge` (design axes) makes a rigid joint; marked `swCylinder`
  it makes the expected sliding, turning joint (Frank's piston, 2026-11-10). Concentric always uses `swCylinder`.

## Not yet proven

- That SimWise reads Alibre's ACIS files at the right scale. Version 7 is used by default; `SIMWISEFORALIBRE_SAT_VERSION` overrides it (Alibre takes 5, 7, 10, 13 to 18).
- Mass: `PhysicalProperties(AD_MEDIUM)` threw a COMException with no message for every part in Frank's first runs, so no density was sent. The log now records the HRESULT.
- That a coord with no body is fixed to the ground.
- That rigid joints between coords marked `swUnknownFeature` hold a sub-assembly together.
- Colour: Alibre's colour is assumed to be a COLORREF (0x00BBGGRR) and transparency 0 to 100.
