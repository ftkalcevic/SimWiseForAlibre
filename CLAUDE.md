# SimWiseForAlibre

An Alibre Design 27.0.1 add-on (C#, .NET Framework 4.8, x64) that exports parts and assemblies to
SimWise 4D through DST's Export API. `README.md` covers building, installing and what's exported;
`docs\ExportAPI.md` covers the SimWise API and what's been proven about it.

## Working with Frank

- Frank commits and pushes himself. Don't commit unless he asks.
- Frank tests in Alibre and SimWise by hand. End a change with a short numbered list of what to
  test, and say plainly what hasn't been built or run. Check the log before asking what happened.
- Ask before making a product decision that isn't recorded here. Propose options when you do.

## Facts

- Alibre 27 hosts .NET Framework 4.8, so the add-on can't target .NET Core or .NET 5+ (Frank asked about .NET 10).
- Alibre's API is in model units (cm for new files); SimWise's API takes SI (metres, kg) for everything.
- Alibre world transforms are row-major with the part's axes as rows and translation in row 4.
  `SWBody.SetOrientation` takes the same layout, axes as rows (proven by Frank on test1); `SWbvect33`
  puts axes in columns, so it isn't used for placements. Coords use `SWCoord.SetOrientation` like bodies.
- SimWise's type library isn't registered for x64: use the embedded interop (`lib\`), never late binding.
- Sub-assemblies are flattened into top-level bodies; rigid ones get rigid joints (Claude's choice, untested).
- The Alibre API docs and samples are in `C:\AlibreToolBox\MyAddon - Claude Reference\` (read-only).
  The 27.0.1 interop (`AlibreX.dll`) is the authority.
- `D:\Projects\alibre\MCPForAlibre` is Frank's other C# add-on, with the same add-on structure and install script.

## Locations

| What | Where |
|---|---|
| Log | `%LOCALAPPDATA%\SimWiseForAlibre\SimWiseForAlibre.log` |
| Installed add-on | `C:\ProgramData\Alibre AddOns\SimWiseForAlibre\` |
| SimWise | `C:\Program Files\DST\SimWise 4D for Alibre V9.86\` |
| Test assemblies | `D:\Projects\alibre\camtest\examples\` |

## Current state

The first version (bodies and Mate/Align/Orient/Angle constraints) has run. Rotations are proven correct (Frank, test1). `IADPhysicalProperties.Material` is E_NOTIMPL (0x80004001) in 27.0.1; the name comes from `DesignProperties.Material`, which matched the density on simwise-simple ("Aluminum - cast-hammered", 2,643 kg/m^3). Open: test1's parts (no material) came out at 1e6 kg/m^3, so the mass units are suspect; the raw units are now logged (built, not installed). Steel was simply not set on that model. Coords on bodies are given in the body's frame, which Frank proved on `simwise-hinge-test` (2026-11-10): the coaxial constraint and the coincident ones to the assembly's planes simulate correctly. Concentric between two design axes sent as `swEdge` coords became a rigid joint in SimWise (PistonInBore); concentric now always sends both coords as `swCylinder`, and the piston slides in its bore (proven by Frank, 2026-11-10). The add-on's boxes are modal to Alibre, owned by its main window (`AlibreWindow`, proven by Frank). Still untested: sub-assemblies (rigid joints, flexible ones' constraints), Orient, Angle, distance (offset) mates, point constraints, and the mass units for parts with no material.
