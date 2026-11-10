using System;
using AlibreX;

namespace SimWiseForAlibre
{
    readonly struct Vec3
    {
        public readonly double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public Vec3 Cross(Vec3 b) => new Vec3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Length => Math.Sqrt(Dot(this));
        public Vec3 Unit() { double l = Length; return l > 1e-15 ? this * (1 / l) : this; }
        public override string ToString() => FormattableString.Invariant($"({X:G6}, {Y:G6}, {Z:G6})");

        public static Vec3 Of(IADPoint p) => new Vec3(p.X, p.Y, p.Z);
        public static Vec3 Of(IADVector v) => new Vec3(v.X, v.Y, v.Z);
    }

    static class Units
    {
        // Millimetres per model unit of a design (the API works in model units, cm for new files).
        public static double MmPerUnit(IADDesignSession d)
        {
            try
            {
                switch (d.DesignProperties.ModelUnits)
                {
                    case ADUnits.AD_MILLIMETERS: return 1.0;
                    case ADUnits.AD_CENTIMETERS: return 10.0;
                    case ADUnits.AD_METERS: return 1000.0;
                    case ADUnits.AD_INCHES: return 25.4;
                    case ADUnits.AD_FEET:
                    case ADUnits.AD_FEET_INCHES: return 304.8;
                }
            }
            catch (Exception) { }
            Log.Write("units", "model units unknown, assuming centimetres");
            return 10.0;
        }

        public static double KgPerMassUnit(IADDesignSession d)
        {
            try
            {
                switch (d.DesignProperties.MassUnits)
                {
                    case ADUnits.AD_GRAMS: return 0.001;
                    case ADUnits.AD_POUNDMASS: return 0.45359237;
                    case ADUnits.AD_KILOGRAMS: return 1.0;
                }
            }
            catch (Exception) { }
            return 1.0;
        }
    }

    // Where a part sits in the exported model: from its own coordinates (its model units) to
    // the top assembly's coordinates in metres, which is what SimWise takes.
    // Alibre's world transform is 16 numbers by rows, points as row vectors (p * M), the translation
    // in the fourth row, in the top assembly's model units (proven in CAMForAlibre).
    sealed class PartPlacement
    {
        readonly double[] m_m = new double[16];
        readonly double m_partToAssembly; // part units to assembly units
        readonly double m_assemblyToMetres;

        public static PartPlacement Identity(double partMm) => new PartPlacement(null, partMm, partMm);

        public PartPlacement(IADTransformation worldTransform, double partMm, double assemblyMm)
        {
            m_partToAssembly = partMm / assemblyMm;
            m_assemblyToMetres = assemblyMm / 1000.0;
            m_m[0] = m_m[5] = m_m[10] = m_m[15] = 1;
            if (worldTransform == null)
                return;
            Array a = worldTransform.Array();
            int i = 0;
            foreach (object v in a)
            {
                if (i >= 16) break;
                m_m[i++] = Convert.ToDouble(v);
            }
            if (i == 12) // a 4 x 3 array: no last column
            {
                var m = new double[16];
                for (int r = 0; r < 4; ++r)
                    for (int c = 0; c < 3; ++c)
                        m[r * 4 + c] = m_m[r * 3 + c];
                m[15] = 1;
                Array.Copy(m, m_m, 16);
            }
        }

        // A point in the part's coordinates, in the model in metres.
        public Vec3 Point(Vec3 p)
        {
            p = p * m_partToAssembly;
            double[] m = m_m;
            return new Vec3(p.X * m[0] + p.Y * m[4] + p.Z * m[8] + m[12],
                            p.X * m[1] + p.Y * m[5] + p.Z * m[9] + m[13],
                            p.X * m[2] + p.Y * m[6] + p.Z * m[10] + m[14]) * m_assemblyToMetres;
        }

        public Vec3 Direction(Vec3 d)
        {
            double[] m = m_m;
            return new Vec3(d.X * m[0] + d.Y * m[4] + d.Z * m[8],
                            d.X * m[1] + d.Y * m[5] + d.Z * m[9],
                            d.X * m[2] + d.Y * m[6] + d.Z * m[10]).Unit();
        }

        // A point in the model in metres, in the body's own frame (its origin and axes, in metres).
        // SimWise reads a coord on a body in the body's frame (Frank's hinge test, 2026-11-10).
        public Vec3 BodyPoint(Vec3 world)
        {
            double[] t = Location;
            return BodyDirection(world - new Vec3(t[0], t[1], t[2]));
        }

        // A direction in the model, in the body's frame: its parts along the body's axes,
        // which are the rows of Alibre's matrix.
        public Vec3 BodyDirection(Vec3 world)
        {
            double[] m = m_m;
            return new Vec3(world.X * m[0] + world.Y * m[1] + world.Z * m[2],
                            world.X * m[4] + world.Y * m[5] + world.Z * m[6],
                            world.X * m[8] + world.Y * m[9] + world.Z * m[10]);
        }

        // The part's origin in the model, metres.
        public double[] Location => new[] { m_m[12] * m_assemblyToMetres, m_m[13] * m_assemblyToMetres, m_m[14] * m_assemblyToMetres };

        // For SWBody.SetOrientation: the part's X, Y and Z axes as the rows of a 3 x 3 matrix, which is
        // Alibre's own layout. Proven by Frank on test1 (2026-11-10): the transpose turned a part
        // rotated +32 degrees to -32. (SWbvect33.VectorToXForm puts its vector in a column, so
        // SWbvect33 uses the other layout; it isn't used for placements.)
        public double[] Orientation(bool transpose)
        {
            var r = new double[9];
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    r[i * 3 + j] = transpose ? m_m[j * 4 + i] : m_m[i * 4 + j];
            return r;
        }
    }

    enum FeatureKind { Plane, Axis, Point }

    // A participant of a constraint, in the model in metres: a plane (origin, normal),
    // an axis (a point on it, its direction) or a point.
    sealed class Feature
    {
        public FeatureKind Kind;
        public Vec3 Origin;
        public Vec3 Direction; // unit; unused for a point
        public bool Round;     // an axis from a cylinder, cone, circle or arc rather than a straight line
        public string What;

        public override string ToString() => What + " at " + Origin + (Kind == FeatureKind.Point ? "" : " along " + Direction);

        // The nearest point of this feature to p.
        public Vec3 Nearest(Vec3 p)
        {
            switch (Kind)
            {
                case FeatureKind.Plane: return p - Direction * (p - Origin).Dot(Direction);
                case FeatureKind.Axis: return Origin + Direction * (p - Origin).Dot(Direction);
                default: return Origin;
            }
        }

        // Reads a face, edge, vertex or design plane, axis or point; null with why if it can't.
        public static Feature Read(object target, PartPlacement placement, out string why)
        {
            why = null;
            switch (target)
            {
                case IADFace face:
                {
                    IADSurface surface = face.Geometry;
                    switch (surface)
                    {
                        case IADPlane plane:
                        {
                            Vec3 normal = Vec3.Of(plane.Normal);
                            if (face.IsSenseReversed)
                                normal = -normal;
                            return Make(FeatureKind.Plane, placement.Point(Vec3.Of(plane.RootPoint)),
                                        placement.Direction(normal), false, "flat face");
                        }
                        case IADCylinder cylinder:
                            return Make(FeatureKind.Axis, placement.Point(Vec3.Of(cylinder.BasePoint)),
                                        placement.Direction(Vec3.Of(cylinder.Axis)), true, "cylindrical face");
                        case IADCone cone:
                            return Make(FeatureKind.Axis, placement.Point(Vec3.Of(cone.BasePoint)),
                                        placement.Direction(Vec3.Of(cone.Axis)), true, "conical face");
                        case IADTorus torus:
                            return Make(FeatureKind.Axis, placement.Point(Vec3.Of(torus.Center)),
                                        placement.Direction(Vec3.Of(torus.Axis)), true, "toroidal face");
                        case IADSphere sphere:
                            return Make(FeatureKind.Point, placement.Point(Vec3.Of(sphere.Center)), default, false, "spherical face");
                    }
                    why = "a face that isn't flat, cylindrical, conical, toroidal or spherical";
                    return null;
                }
                case IADEdge edge:
                {
                    IADCurve curve = edge.Geometry;
                    switch (curve)
                    {
                        case IADLine line:
                            return Make(FeatureKind.Axis, placement.Point(Vec3.Of(line.StartPoint)),
                                        placement.Direction(Vec3.Of(line.DirectionVector)), false, "straight edge");
                        case IADCircle circle:
                            return Make(FeatureKind.Axis, placement.Point(Vec3.Of(circle.Center)),
                                        placement.Direction(Vec3.Of(circle.Axis)), true, "circular edge");
                        case IADCircularArc arc:
                            return Make(FeatureKind.Axis, placement.Point(Vec3.Of(arc.Center)),
                                        placement.Direction(Vec3.Of(arc.Axis)), true, "arc edge");
                    }
                    why = "an edge that isn't straight or circular";
                    return null;
                }
                case IADVertex vertex:
                    return Make(FeatureKind.Point, placement.Point(Vec3.Of(vertex.Point)), default, false, "vertex");
                case IADDesignPlane designPlane:
                {
                    designPlane.GetGeometry(out IADPoint p1, out IADPoint p2, out IADPoint p3);
                    return Make(FeatureKind.Plane, placement.Point(Vec3.Of(p1)),
                                placement.Direction(Vec3.Of(designPlane.Normal)), false, "design plane " + designPlane.Name);
                }
                case IADDesignAxis designAxis:
                {
                    designAxis.GetGeometry(out IADPoint p1, out IADPoint p2);
                    Vec3 a = Vec3.Of(p1), b = Vec3.Of(p2);
                    return Make(FeatureKind.Axis, placement.Point(a), placement.Direction(b - a), false, "design axis " + designAxis.Name);
                }
                case IADDesignPoint designPoint:
                    return Make(FeatureKind.Point, placement.Point(Vec3.Of(designPoint.Geometry)), default, false,
                                "design point " + designPoint.Name);
            }
            why = "an unsupported kind of geometry";
            return null;
        }

        static Feature Make(FeatureKind kind, Vec3 origin, Vec3 direction, bool round, string what) =>
            new Feature { Kind = kind, Origin = origin, Direction = direction, Round = round, What = what };
    }

    // A right-handed frame with its Z along a direction, for SimWise coords.
    static class Frames
    {
        public static void AxesFor(Vec3 z, out Vec3 x, out Vec3 y)
        {
            z = z.Unit();
            Vec3 helper = Math.Abs(z.X) < 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
            x = (helper - z * helper.Dot(z)).Unit();
            y = z.Cross(x);
        }
    }
}
