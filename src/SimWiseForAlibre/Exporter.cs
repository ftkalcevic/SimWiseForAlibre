using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using AlibreX;
using SimWiseExportLib;

namespace SimWiseForAlibre
{
    sealed class ExportReport
    {
        public string WmPath;
        public int Bodies;
        public int Constraints;
        public int RigidJoints;
        public bool SimWiseOpened;
        public readonly List<string> Problems = new List<string>();

        public string Text()
        {
            var s = new StringBuilder();
            s.AppendLine(SimWiseOpened ? "Sent to SimWise:" : "SimWise reported that the export failed.");
            s.AppendLine(WmPath);
            s.AppendLine();
            s.AppendLine(Bodies + (Bodies == 1 ? " body, " : " bodies, ") + Constraints +
                         (Constraints == 1 ? " constraint" : " constraints") +
                         (RigidJoints > 0 ? ", " + RigidJoints + " rigid joints for sub-assemblies" : "") + ".");
            if (Problems.Count > 0)
            {
                s.AppendLine();
                s.AppendLine("Not exported:");
                int shown = 0;
                foreach (string p in Problems)
                {
                    if (++shown > 20)
                    {
                        s.AppendLine("... and " + (Problems.Count - 20) + " more (see the log).");
                        break;
                    }
                    s.AppendLine("- " + p);
                }
            }
            return s.ToString();
        }
    }

    // Sends a part or an assembly to SimWise through its Export API (SimWiseExport.dll).
    // Every part occurrence becomes a top-level body (sub-assemblies are flattened): its geometry is
    // an ACIS file of the part in its own coordinates, placed by the occurrence's world transform.
    // Constraints become SimWise constraints between coords placed on the bodies, and SimWise
    // combines them into joints. A rigid sub-assembly's parts are held together by rigid joints.
    sealed class Exporter
    {
        const double kTolerance = 1e-7; // metres: what counts as touching

        readonly IADSession m_session;
        readonly IntPtr m_alibreWindow;
        readonly ExportReport m_report = new ExportReport();
        readonly Dictionary<string, BodyInfo> m_bodies = new Dictionary<string, BodyInfo>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> m_satByPart = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> m_tempFiles = new List<string>();
        SWExport m_sw;
        double m_assemblyMm;
        int m_nextId = 1;

        sealed class BodyInfo
        {
            public SWBody Body;
            public PartPlacement Placement;
            public string Path;
        }

        public Exporter(IADSession session, IntPtr alibreWindow)
        {
            m_session = session;
            m_alibreWindow = alibreWindow;
        }

        static bool Transposed => Environment.GetEnvironmentVariable("SIMWISEFORALIBRE_TRANSPOSE") == "1";

        static int SatVersion
        {
            get
            {
                string v = Environment.GetEnvironmentVariable("SIMWISEFORALIBRE_SAT_VERSION");
                return int.TryParse(v, out int n) ? n : 7;
            }
        }

        // Null when the user cancels.
        public ExportReport Run()
        {
            string cadPath = m_session.FilePath;
            if (string.IsNullOrEmpty(cadPath))
            {
                MessageBox.Show(AlibreWindow.Owner(m_alibreWindow), "Save the design first: SimWise names its model after the design's file.",
                                "SimWiseForAlibre", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            cadPath = cadPath.Replace('/', '\\');
            if (!(m_session is IADDesignSession design))
                throw new InvalidOperationException("This kind of document can't be exported.");

            m_sw = new SWExport();
            m_sw.Init(CADType.Alibre, 0, (ulong)m_alibreWindow.ToInt64());
            Log.Write("export", "SimWise export API version " + m_sw.SimWiseVersion + "; " + cadPath);

            string wmPath = ChooseWmPath(cadPath);
            if (wmPath == null)
            {
                m_sw.TerminateExport();
                return null;
            }
            m_report.WmPath = wmPath;

            try
            {
                m_assemblyMm = Units.MmPerUnit(design);
                m_sw.SetUnitSystem(DisplayUnits(design), false, false);
                m_sw.AutoComputeCOMs = true;
                m_sw.ExportConstraints = true;
                m_sw.ExportMates = true;

                if (m_session is IADAssemblySession assembly)
                {
                    IADOccurrence root = assembly.RootOccurrence;
                    AddChildren(root, "");
                    AddConstraints(assembly, "");
                }
                else
                {
                    BodyInfo body = AddBody(design, null, PartPlacement.Identity(m_assemblyMm),
                                            Path.GetFileNameWithoutExtension(cadPath), m_session.Name, null);
                    if (body == null)
                        throw new InvalidOperationException(m_report.Problems.Count > 0 ? m_report.Problems[0] : "The part couldn't be exported.");
                }

                Log.Write("export", "DoExport to " + wmPath);
                m_report.SimWiseOpened = m_sw.DoExport(cadPath, wmPath);
                Log.Write("export", "DoExport returned " + m_report.SimWiseOpened);
            }
            finally
            {
                try { m_sw.TerminateExport(); } catch (Exception ex) { Log.Write("export", "TerminateExport: " + ex.Message); }
                // SimWise reads the ACIS files during DoExport; they're ours to remove afterwards.
                foreach (string f in m_tempFiles)
                    try { File.Delete(f); } catch (Exception) { }
            }
            foreach (string p in m_report.Problems)
                Log.Write("skipped", p);
            Log.Write("export", m_report.Bodies + " bodies, " + m_report.Constraints + " constraints, " +
                                m_report.RigidJoints + " rigid joints, " + m_report.Problems.Count + " skipped");
            return m_report;
        }

        string ChooseWmPath(string cadPath)
        {
            string suggested = null;
            try { suggested = m_sw.GetWM3PathToSuggestToUser(cadPath); } catch (Exception) { }
            if (string.IsNullOrEmpty(suggested) || !suggested.EndsWith(".wm3", StringComparison.OrdinalIgnoreCase))
                suggested = Path.ChangeExtension(cadPath, ".wm3");
            using (var dialog = new SaveFileDialog
            {
                Title = "Export to SimWise",
                Filter = "SimWise models (*.wm3)|*.wm3",
                DefaultExt = "wm3",
                InitialDirectory = Path.GetDirectoryName(suggested),
                FileName = Path.GetFileName(suggested),
                OverwritePrompt = true,
            })
            {
                return dialog.ShowDialog(AlibreWindow.Owner(m_alibreWindow)) == DialogResult.OK ? dialog.FileName : null;
            }
        }

        static UnitSystem DisplayUnits(IADDesignSession design)
        {
            try
            {
                switch (design.DesignProperties.LengthDisplayUnits)
                {
                    case ADUnits.AD_MILLIMETERS: return UnitSystem.millimeters;
                    case ADUnits.AD_CENTIMETERS: return UnitSystem.CGS;
                    case ADUnits.AD_METERS: return UnitSystem.MKS;
                    case ADUnits.AD_INCHES: return UnitSystem.english;
                    case ADUnits.AD_FEET:
                    case ADUnits.AD_FEET_INCHES: return UnitSystem.feet;
                }
            }
            catch (Exception) { }
            return UnitSystem.millimeters;
        }

        // ---- bodies ----

        void AddChildren(IADOccurrence parent, string parentPath)
        {
            foreach (IADOccurrence o in parent.Occurrences)
            {
                string path = parentPath.Length == 0 ? o.Name : parentPath + "/" + o.Name;
                try
                {
                    if (o.IsSuppressed)
                    {
                        Log.Write("body", path + ": suppressed, left out");
                        continue;
                    }
                    if (o.IsHidden)
                    {
                        Log.Write("body", path + ": hidden, left out");
                        continue;
                    }
                    IADDesignSession design = o.DesignSession;
                    if (design is IADAssemblySession sub)
                    {
                        var added = new List<BodyInfo>();
                        AddChildren(o, path);
                        foreach (var pair in m_bodies)
                            if (pair.Key.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))
                                added.Add(pair.Value);
                        if (o.IsFlexible)
                            AddConstraints(sub, path);
                        else
                            HoldTogether(path, added);
                        continue;
                    }
                    var placement = new PartPlacement(o.WorldTransform, Units.MmPerUnit(design), m_assemblyMm);
                    BodyInfo body = AddBody(design, o, placement, path, path, o);
                    if (body != null)
                        m_bodies[path] = body;
                }
                catch (COMException ex)
                {
                    Problem(path + ": couldn't be read (" + ex.Message + ")");
                }
            }
        }

        BodyInfo AddBody(IADDesignSession design, IADOccurrence occurrence, PartPlacement placement,
                         string name, string path, IADOccurrence colourFrom)
        {
            string satFile = SatFileFor(design, path);
            if (satFile == null)
                return null;

            SWBody body = m_sw.NewBody();
            body.Name = name;
            body.id = m_nextId++;
            double[] location = placement.Location;
            body.SetLocation(ref location);
            double[] orientation = placement.Orientation(Transposed);
            body.SetOrientation(ref orientation);
            body.SetAcisFileName(satFile);
            body.SetUpdateStamp(UpdateStamp(design));

            SetColour(body, design, colourFrom, path);
            SetMaterial(body, design, path);
            if (occurrence != null)
            {
                try { body.Anchored = occurrence.IsAnchored; } catch (COMException) { }
            }

            Log.Write("body", path + ": id " + body.id + " at " + Fmt(location) + ", orientation " + Fmt(orientation) +
                              (body.Anchored ? ", anchored" : "") + ", " + satFile);
            ++m_report.Bodies;
            return new BodyInfo { Body = body, Placement = placement, Path = path };
        }

        // The doc asks for a separate file per instance, so each part is exported once and copied.
        string SatFileFor(IADDesignSession design, string path)
        {
            string key = ((IADSession)design).FilePath;
            if (string.IsNullOrEmpty(key))
                key = "unsaved:" + ((IADSession)design).Name;
            try
            {
                if (!m_satByPart.TryGetValue(key, out string master))
                {
                    master = TempSatName();
                    design.ExportSAT(master, SatVersion);
                    if (!File.Exists(master))
                        throw new IOException("Alibre wrote no file");
                    m_satByPart[key] = master;
                    Log.Write("sat", key + " -> " + master + " (ACIS " + SatVersion + ", " + new FileInfo(master).Length + " bytes)");
                }
                string copy = TempSatName();
                File.Copy(master, copy, true);
                return copy;
            }
            catch (Exception ex)
            {
                Problem(path + ": its geometry couldn't be exported as ACIS (" + ex.Message + ")");
                return null;
            }
        }

        string TempSatName()
        {
            string name = m_sw.GetTempFile();
            if (string.IsNullOrEmpty(name))
                name = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            if (!name.EndsWith(".sat", StringComparison.OrdinalIgnoreCase))
            {
                try { if (File.Exists(name)) File.Delete(name); } catch (Exception) { }
                name += ".sat";
            }
            m_tempFiles.Add(name);
            return name;
        }

        static string UpdateStamp(IADDesignSession design)
        {
            string file = ((IADSession)design).FilePath;
            try
            {
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                    return file + "|" + File.GetLastWriteTimeUtc(file).Ticks;
            }
            catch (Exception) { }
            return (file ?? "") + "|" + DateTime.UtcNow.Ticks;
        }

        static void SetColour(SWBody body, IADDesignSession design, IADOccurrence occurrence, string path)
        {
            int colour = -1, transparency = 0;
            try
            {
                if (occurrence != null)
                {
                    colour = occurrence.Color;
                    transparency = occurrence.Transparency;
                }
                else if (design is IADPartSession part)
                {
                    colour = part.Color;
                    transparency = part.Transparency;
                }
            }
            catch (COMException) { }
            Log.Write("colour", path + ": colour 0x" + colour.ToString("X8") + ", transparency " + transparency);
            if (colour < 0)
                return;
            // A COLORREF: 0x00BBGGRR.
            body.SetColor((colour & 0xFF) / 255.0, ((colour >> 8) & 0xFF) / 255.0, ((colour >> 16) & 0xFF) / 255.0);
            if (transparency > 0 && transparency <= 100)
                body.SetOpacity(1.0 - transparency / 100.0);
        }

        // SimWise works out the mass from the geometry (AutoComputeCOMs) and this density.
        static void SetMaterial(SWBody body, IADDesignSession design, string path)
        {
            try
            {
                IADPhysicalProperties properties = design.PhysicalProperties(ADAccuracySetting.AD_MEDIUM);
                double mmPerUnit = Units.MmPerUnit(design);
                double volume = properties.Volume * Math.Pow(mmPerUnit / 1000.0, 3); // m^3
                double mass = properties.Mass * Units.KgPerMassUnit(design);         // kg
                string units = "?", massUnits = "?", designDensity = "?";
                try { units = design.DesignProperties.ModelUnits.ToString(); } catch (Exception) { }
                try { massUnits = design.DesignProperties.MassUnits.ToString(); } catch (Exception ex) { massUnits = "hr=0x" + ex.HResult.ToString("X8"); }
                try { designDensity = design.DesignProperties.Density.ToString("G6"); } catch (Exception ex) { designDensity = "hr=0x" + ex.HResult.ToString("X8"); }
                Log.Write("mass", path + ": raw mass " + properties.Mass.ToString("G6") + ", raw volume " + properties.Volume.ToString("G6") +
                                  ", model units " + units + ", mass units " + massUnits + ", design density " + designDensity);
                string material = null;
                try { material = properties.Material; }
                catch (Exception ex) { Log.Write("mass", path + ": no material name (hr=0x" + ex.HResult.ToString("X8") + ")"); }
                if (string.IsNullOrEmpty(material))
                {
                    try { material = design.DesignProperties.Material; } catch (Exception) { }
                }
                if (!string.IsNullOrEmpty(material))
                    body.MaterialName = material;
                if (volume > 0 && mass > 0)
                {
                    double density = mass / volume;
                    body.Density = density;
                    body.MaterialDensity = density;
                    Log.Write("mass", path + ": " + material + ", " + mass.ToString("G6") + " kg, " +
                                      volume.ToString("G6") + " m^3, density " + density.ToString("G6") + " kg/m^3");
                }
                else
                {
                    Log.Write("mass", path + ": no mass or volume from Alibre (" + properties.Mass + ", " + properties.Volume + ")");
                }
            }
            catch (Exception ex)
            {
                Log.Write("mass", path + ": physical properties failed: " + ex.GetType().Name + " hr=0x" +
                                  ex.HResult.ToString("X8") + " " + ex.Message + " at " + ex.StackTrace?.Trim().Split('\n')[0]);
            }
        }

        // A rigid sub-assembly moves as one: its first body is held to each of the others.
        void HoldTogether(string path, List<BodyInfo> bodies)
        {
            if (bodies.Count < 2)
                return;
            BodyInfo first = bodies[0];
            var origin = new Vec3(first.Placement.Location[0], first.Placement.Location[1], first.Placement.Location[2]);
            for (int i = 1; i < bodies.Count; ++i)
            {
                SWCoord c1 = NewCoord(first, origin, new Vec3(0, 0, 1), SWFeatureType.swUnknownFeature);
                SWCoord c2 = NewCoord(bodies[i], origin, new Vec3(0, 0, 1), SWFeatureType.swUnknownFeature);
                SWConstraint c = m_sw.NewConstraint(true);
                c.SetCoords(c1, c2);
                c.SetMateType(SWMateType.swRigidJoint);
                c.Name = path + " rigid " + i;
                ++m_report.RigidJoints;
                Log.Write("rigid", c.Name + ": " + first.Path + " to " + bodies[i].Path);
            }
        }

        // ---- constraints ----

        void AddConstraints(IADAssemblySession assembly, string prefix)
        {
            foreach (IADAssemblyConstraint c in assembly.AssemblyConstraints)
            {
                string name = (prefix.Length == 0 ? "" : prefix + "/") + SafeName(c);
                try
                {
                    AddConstraint(c, prefix, name);
                }
                catch (COMException ex)
                {
                    Problem(name + ": couldn't be read (" + ex.Message + ")");
                }
            }
        }

        static string SafeName(IADAssemblyConstraint c)
        {
            try { return c.Name; } catch (COMException) { return "a constraint"; }
        }

        void AddConstraint(IADAssemblyConstraint c, string prefix, string name)
        {
            if (c.IsSuppressed)
            {
                Log.Write("constraint", name + ": suppressed, left out");
                return;
            }
            if (c.HasError)
            {
                Problem(name + ": it has an error in Alibre");
                return;
            }
            ADAssemblyConstraintType type = c.ConstraintType;
            switch (type)
            {
                case ADAssemblyConstraintType.AD_MATE_TYPE:
                case ADAssemblyConstraintType.AD_ALIGN_TYPE:
                case ADAssemblyConstraintType.AD_ORIENT_TYPE:
                case ADAssemblyConstraintType.AD_ANGLE_TYPE:
                    break;
                default:
                    Problem(name + ": " + TypeName(type) + " constraints aren't exported yet");
                    return;
            }

            var sides = new List<(BodyInfo body, Feature feature)>();
            foreach (object participant in ItemsOf(c.Participants))
            {
                if (!(participant is IADTargetProxy proxy))
                {
                    Problem(name + ": a participant isn't on a part (" + participant?.GetType().Name + ")");
                    return;
                }
                IADOccurrence occurrence = proxy.Occurrence;
                BodyInfo body = null;
                PartPlacement placement;
                if (occurrence == null || occurrence.NestLevel == 0)
                {
                    // The assembly's own planes, axes and points: fixed to the ground.
                    if (prefix.Length != 0)
                    {
                        Problem(name + ": it uses the sub-assembly's own geometry (" + proxy.DisplayName + ")");
                        return;
                    }
                    placement = PartPlacement.Identity(m_assemblyMm);
                }
                else
                {
                    string path = PathOf(occurrence);
                    if (prefix.Length != 0)
                        path = prefix + "/" + path;
                    if (!m_bodies.TryGetValue(path, out body))
                    {
                        Problem(name + ": " + path + " wasn't exported");
                        return;
                    }
                    placement = body.Placement;
                }
                Feature feature = Feature.Read(proxy.Target, placement, out string why);
                if (feature == null)
                {
                    Problem(name + ": " + proxy.DisplayName + " is " + why);
                    return;
                }
                sides.Add((body, feature));
            }
            if (sides.Count != 2)
            {
                Problem(name + ": it has " + sides.Count + " participants, not 2");
                return;
            }
            if (sides[0].body == null && sides[1].body == null)
            {
                Log.Write("constraint", name + ": between the assembly's own geometry, left out");
                return;
            }
            if (sides[0].body == null)
                sides.Reverse(); // the body first, the ground second

            var (b1, f1) = sides[0];
            var (b2, f2) = sides[1];
            if (!Plan(type, f1, f2, out SWMateType mate, out Vec3 z, out Vec3 origin1, out Vec3 origin2, out Vec3 z2, out string problem))
            {
                Problem(name + ": " + problem);
                return;
            }

            // SimWise makes a concentric constraint a sliding, turning joint only between cylinders: between two
            // design axes sent as edges it made a rigid joint (Frank's PistonInBore, 2026-11-10). So any axis,
            // whether from a cylinder, a circle, a straight edge or a design axis, goes as a cylinder here.
            SWFeatureType t1 = mate == SWMateType.swConcentric ? SWFeatureType.swCylinder : FeatureTypeOf(f1);
            SWFeatureType t2 = mate == SWMateType.swConcentric ? SWFeatureType.swCylinder : FeatureTypeOf(f2);
            SWCoord c1 = NewCoord(b1, origin1, z, t1);
            SWCoord c2 = NewCoord(b2, origin2, z2, t2);
            SWConstraint constraint = m_sw.NewConstraint(true);
            constraint.SetCoords(c1, c2);
            constraint.SetMateType(mate);
            constraint.Name = name;
            ++m_report.Constraints;
            Log.Write("constraint", name + ": " + TypeName(type) + " -> " + mate + "; " + (b1?.Path ?? "ground") + " " + f1 +
                                    " | " + (b2?.Path ?? "ground") + " " + f2);
        }

        // Which SimWise constraint, and where its two coords go. Both bodies are already where Alibre
        // put them, so the coords are placed to match: for Mate and Align both coords share Z and
        // the second sits at the nearest point of its feature to the first's origin.
        static bool Plan(ADAssemblyConstraintType type, Feature f1, Feature f2, out SWMateType mate,
                         out Vec3 z, out Vec3 origin1, out Vec3 origin2, out Vec3 z2, out string problem)
        {
            mate = SWMateType.swUnknownMate;
            problem = null;
            origin1 = f1.Origin;
            z = f1.Kind == FeatureKind.Point ? (f2.Kind == FeatureKind.Point ? new Vec3(0, 0, 1) : f2.Direction) : f1.Direction;
            origin2 = f2.Nearest(origin1);
            z2 = z;

            switch (type)
            {
                case ADAssemblyConstraintType.AD_ORIENT_TYPE:
                case ADAssemblyConstraintType.AD_ANGLE_TYPE:
                    if (f1.Kind == FeatureKind.Point || f2.Kind == FeatureKind.Point)
                    {
                        problem = "an orient or angle constraint on a point";
                        return false;
                    }
                    origin2 = f2.Origin;
                    z2 = f2.Direction;
                    mate = type == ADAssemblyConstraintType.AD_ORIENT_TYPE ? SWMateType.swParallel : SWMateType.swAngle;
                    return true;
            }

            // Mate and Align
            double gap = (origin2 - origin1).Length;
            if (f1.Kind == FeatureKind.Plane && f2.Kind == FeatureKind.Plane)
            {
                mate = gap < kTolerance ? SWMateType.swCoincident : SWMateType.swDistance;
                return true;
            }
            if (f1.Kind == FeatureKind.Axis && f2.Kind == FeatureKind.Axis)
            {
                if (Math.Abs(Math.Abs(f1.Direction.Dot(f2.Direction)) - 1) > 1e-6)
                {
                    problem = "its axes aren't parallel";
                    return false;
                }
                if (gap < kTolerance)
                {
                    mate = SWMateType.swConcentric;
                    return true;
                }
                problem = "its axes are offset (" + (gap * 1000).ToString("G4") + " mm), which isn't exported yet";
                return false;
            }
            if (f1.Kind == FeatureKind.Point && f2.Kind == FeatureKind.Point)
            {
                mate = gap < kTolerance ? SWMateType.swCoincident : SWMateType.swDistance;
                return true;
            }
            problem = "a " + f1.Kind.ToString().ToLowerInvariant() + " to " + f2.Kind.ToString().ToLowerInvariant() +
                      " constraint isn't exported yet";
            return false;
        }

        static SWFeatureType FeatureTypeOf(Feature f)
        {
            switch (f.Kind)
            {
                case FeatureKind.Plane: return SWFeatureType.swPlane;
                case FeatureKind.Axis: return f.Round ? SWFeatureType.swCylinder : SWFeatureType.swEdge;
                default: return SWFeatureType.swVertex;
            }
        }

        // A coord on a body (or on the ground when body is null), in the model in metres, Z along z.
        SWCoord NewCoord(BodyInfo body, Vec3 origin, Vec3 z, SWFeatureType featureType)
        {
            Frames.AxesFor(z, out Vec3 x, out Vec3 y);
            z = z.Unit();
            if (body != null)
            {
                // A coord on a body is in the body's own frame (the PDF: SetReferenceToBody "establishes the
                // specified body as the reference frame for the coord"); DST's examples only had bodies at the
                // origin, unrotated, where the two agree.
                origin = body.Placement.BodyPoint(origin);
                x = body.Placement.BodyDirection(x);
                y = body.Placement.BodyDirection(y);
                z = body.Placement.BodyDirection(z);
            }
            // The coord's axes as rows, the layout SWBody.SetOrientation was proven to take.
            double[] orientation = Transposed
                ? new[] { x.X, y.X, z.X, x.Y, y.Y, z.Y, x.Z, y.Z, z.Z }
                : new[] { x.X, x.Y, x.Z, y.X, y.Y, y.Z, z.X, z.Y, z.Z };
            SWCoord coord = m_sw.NewCoord(true);
            if (body != null)
                coord.SetReferenceToBody(body.Body);
            coord.SetFeatureType(featureType);
            double[] offset = { origin.X, origin.Y, origin.Z };
            coord.SetOffset(ref offset);
            coord.SetOrientation(ref orientation);
            Log.Write("coord", (body?.Path ?? "ground") + " " + featureType + ": offset " + Fmt(offset) + ", orientation " + Fmt(orientation));
            return coord;
        }

        // ---- helpers ----

        void Problem(string text) => m_report.Problems.Add(text);

        static string TypeName(ADAssemblyConstraintType type)
        {
            string s = type.ToString();
            if (s.StartsWith("AD_")) s = s.Substring(3);
            if (s.EndsWith("_TYPE")) s = s.Substring(0, s.Length - 5);
            s = s.Replace('_', ' ').ToLowerInvariant();
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        static string PathOf(IADOccurrence o)
        {
            var names = new List<string>();
            for (IADOccurrence cur = o; cur != null && cur.NestLevel > 0; cur = cur.ParentOccurrence)
                names.Insert(0, cur.Name);
            return string.Join("/", names);
        }

        static List<object> ItemsOf(IObjectCollector collector)
        {
            var list = new List<object>();
            if (collector == null)
                return list;
            int count = collector.Count;
            if (count == 0)
                return list;
            int first = 0;
            try { if (collector.Item(0) == null) first = 1; }
            catch (Exception) { first = 1; }
            for (int i = first; i < first + count; ++i)
                list.Add(collector.Item(i));
            return list;
        }

        static string Fmt(double[] a)
        {
            var parts = new string[a.Length];
            for (int i = 0; i < a.Length; ++i)
                parts[i] = a[i].ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
            return "[" + string.Join(", ", parts) + "]";
        }
    }
}
