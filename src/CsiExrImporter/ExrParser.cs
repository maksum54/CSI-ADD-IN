using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace CsiExrImporter
{
    /// <summary>
    /// Parser toleran untuk file .exr (XML, CSiXRevit/ETABS). Nama tag dan atribut dicocokkan
    /// tanpa peduli huruf besar/kecil dan dengan beberapa alias, karena skema berbeda antar versi.
    /// </summary>
    public static class ExrParser
    {
        public static ExrModel Parse(string path)
        {
            var doc = XDocument.Load(path);
            var all = doc.Descendants().ToList();
            var m = new ExrModel();

            var unitsEl = all.FirstOrDefault(e => Is(e, "Units", "LengthUnits", "UnitsLength"));
            string u = unitsEl != null ? unitsEl.Value : Get(doc.Root, "Units", "LengthUnits");
            if (!string.IsNullOrWhiteSpace(u)) m.Units = u.Trim();

            foreach (var e in all.Where(e => Is(e, "Story", "Level", "Storey")))
            {
                double? z = Num(e, "Elevation", "Elev", "Z", "Height");
                if (z == null) continue;
                m.Stories.Add(new ExrStory { Name = Get(e, "Name", "Label", "ID") ?? $"Story {m.Stories.Count + 1}", Elevation = z.Value });
            }

            foreach (var e in all.Where(e => Is(e, "Grid", "GridLine", "Gridline")))
            {
                var x1 = Num(e, "X1", "StartX"); var y1 = Num(e, "Y1", "StartY");
                var x2 = Num(e, "X2", "EndX"); var y2 = Num(e, "Y2", "EndY");
                if (x1 == null || y1 == null || x2 == null || y2 == null) continue;
                m.Grids.Add(new ExrGrid { Name = Get(e, "Name", "Label", "ID"), X1 = x1.Value, Y1 = y1.Value, X2 = x2.Value, Y2 = y2.Value });
            }

            foreach (var e in all.Where(e => Is(e, "Section", "FrameSection", "FrameSect", "Property", "FrameProperty")))
            {
                string name = Get(e, "Name", "Label", "ID");
                if (name == null) continue;
                m.Sections[name] = new ExrSection
                {
                    Name = name,
                    Material = Get(e, "Material", "Mat"),
                    Shape = Get(e, "Shape", "Type", "SectionType"),
                    Depth = Num(e, "Depth", "t3", "D", "H", "Height") ?? 0,
                    Width = Num(e, "Width", "t2", "B", "W") ?? 0,
                };
            }

            // Titik/joint (untuk frame yang referensi ke ID titik)
            var points = new Dictionary<string, ExrPoint>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in all.Where(e => Is(e, "Point", "Joint", "Node")))
            {
                string id = Get(e, "ID", "Name", "Label");
                var x = Num(e, "X"); var y = Num(e, "Y"); var z = Num(e, "Z");
                if (id != null && x != null && y != null && z != null)
                    points[id] = new ExrPoint { X = x.Value, Y = y.Value, Z = z.Value };
            }

            foreach (var e in all.Where(e => Is(e, "Frame", "Member", "Column", "Beam", "Brace", "FrameObject", "Line")))
            {
                ExrPoint s = Pt(e, "1", "I", "Start") ?? Ref(e, points, "Point1", "PointI", "JointI", "StartPoint", "Node1");
                ExrPoint t = Pt(e, "2", "J", "End") ?? Ref(e, points, "Point2", "PointJ", "JointJ", "EndPoint", "Node2");
                if (s == null || t == null) continue;

                string type = (Get(e, "Type", "DesignType", "Kind", "ObjectType") ?? e.Name.LocalName).ToLowerInvariant();
                MemberKind kind =
                    type.Contains("col") ? MemberKind.Column :
                    type.Contains("brac") ? MemberKind.Brace :
                    type.Contains("beam") ? MemberKind.Beam : Classify(s, t);

                m.Frames.Add(new ExrFrame
                {
                    Id = Get(e, "ID", "Name", "Label"),
                    Section = Get(e, "Section", "SectionName", "Property", "Prop", "SectProp"),
                    Kind = kind, Start = s, End = t,
                });
            }

            return m;
        }

        static MemberKind Classify(ExrPoint a, ExrPoint b)
        {
            double dz = Math.Abs(b.Z - a.Z);
            double dxy = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
            if (dxy < 1e-6) return MemberKind.Column;
            if (dz < 1e-6) return MemberKind.Beam;
            return MemberKind.Brace;
        }

        static ExrPoint Pt(XElement e, params string[] suffixes)
        {
            foreach (var s in suffixes)
            {
                var x = Num(e, "X" + s, s + "X"); var y = Num(e, "Y" + s, s + "Y"); var z = Num(e, "Z" + s, s + "Z");
                if (x != null && y != null && z != null) return new ExrPoint { X = x.Value, Y = y.Value, Z = z.Value };
            }
            return null;
        }

        static ExrPoint Ref(XElement e, Dictionary<string, ExrPoint> pts, params string[] names)
        {
            string id = Get(e, names);
            return id != null && pts.TryGetValue(id, out var p) ? p : null;
        }

        static bool Is(XElement e, params string[] names) =>
            names.Any(n => string.Equals(e.Name.LocalName, n, StringComparison.OrdinalIgnoreCase));

        /// <summary>Nilai dari atribut atau child element langsung.</summary>
        internal static string Get(XElement e, params string[] names)
        {
            if (e == null) return null;
            foreach (var n in names)
            {
                var a = e.Attributes().FirstOrDefault(x => string.Equals(x.Name.LocalName, n, StringComparison.OrdinalIgnoreCase));
                if (a != null && !string.IsNullOrWhiteSpace(a.Value)) return a.Value.Trim();
                var c = e.Elements().FirstOrDefault(x => string.Equals(x.Name.LocalName, n, StringComparison.OrdinalIgnoreCase) && !x.HasElements);
                if (c != null && !string.IsNullOrWhiteSpace(c.Value)) return c.Value.Trim();
            }
            return null;
        }

        static double? Num(XElement e, params string[] names)
        {
            string s = Get(e, names);
            if (s == null) return null;
            s = s.Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        }
    }
}
