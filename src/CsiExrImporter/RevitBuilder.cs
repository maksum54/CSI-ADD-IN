using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace CsiExrImporter
{
    public class RevitBuilder
    {
        readonly Document _doc;
        readonly ExrModel _m;
        readonly ForgeTypeId _unit;
        readonly StringBuilder _log = new();
        readonly List<Level> _levels = new();
        readonly Dictionary<string, FamilySymbol> _symbolCache = new(StringComparer.OrdinalIgnoreCase);

        public int Levels, Grids, Columns, Beams, Braces, Skipped;
        public string Log => _log.ToString();

        public RevitBuilder(Document doc, ExrModel m)
        {
            _doc = doc; _m = m;
            _unit = UnitFromString(m.Units);
        }

        static ForgeTypeId UnitFromString(string u)
        {
            switch ((u ?? "").ToLowerInvariant())
            {
                case "mm": case "millimeter": case "millimeters": return UnitTypeId.Millimeters;
                case "cm": case "centimeter": case "centimeters": return UnitTypeId.Centimeters;
                case "ft": case "feet": case "foot": return UnitTypeId.Feet;
                case "in": case "inch": case "inches": return UnitTypeId.Inches;
                default:
                    if (u != null && u.ToLowerInvariant().Contains("mm")) return UnitTypeId.Millimeters;
                    if (u != null && u.ToLowerInvariant().Contains("ft")) return UnitTypeId.Feet;
                    if (u != null && u.ToLowerInvariant().Contains("in")) return UnitTypeId.Inches;
                    return UnitTypeId.Meters;
            }
        }

        double L(double v) => UnitUtils.ConvertToInternalUnits(v, _unit);
        XYZ P(ExrPoint p) => new XYZ(L(p.X), L(p.Y), L(p.Z));

        public void Build()
        {
            using var t = new Transaction(_doc, "Import ETABS EXR");
            t.Start();
            var fho = t.GetFailureHandlingOptions();
            fho.SetFailuresPreprocessor(new WarningSwallower());
            t.SetFailureHandlingOptions(fho);

            BuildLevels();
            BuildGrids();
            BuildFrames();

            t.Commit();
        }

        void BuildLevels()
        {
            _levels.AddRange(new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>());
            foreach (var s in _m.Stories.OrderBy(s => s.Elevation))
            {
                double z = L(s.Elevation);
                var existing = _levels.FirstOrDefault(l => Math.Abs(l.Elevation - z) < 1e-4);
                if (existing != null) continue;
                var lvl = Level.Create(_doc, z);
                TrySetName(lvl, s.Name);
                _levels.Add(lvl);
                Levels++;
            }
            if (_levels.Count == 0) { _levels.Add(Level.Create(_doc, 0)); Levels++; }
        }

        void BuildGrids()
        {
            var existing = new FilteredElementCollector(_doc).OfClass(typeof(Grid)).Cast<Grid>()
                .Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var g in _m.Grids)
            {
                if (g.Name != null && existing.Contains(g.Name)) continue;
                var a = new XYZ(L(g.X1), L(g.Y1), 0); var b = new XYZ(L(g.X2), L(g.Y2), 0);
                if (a.DistanceTo(b) < _doc.Application.ShortCurveTolerance) continue;
                var grid = Grid.Create(_doc, Line.CreateBound(a, b));
                TrySetName(grid, g.Name);
                Grids++;
            }
        }

        void BuildFrames()
        {
            foreach (var f in _m.Frames)
            {
                try
                {
                    XYZ a = P(f.Start), b = P(f.End);
                    if (a.DistanceTo(b) < _doc.Application.ShortCurveTolerance) { Skipped++; continue; }
                    if (f.Kind == MemberKind.Column && a.Z > b.Z) (a, b) = (b, a);

                    var cat = f.Kind == MemberKind.Column ? BuiltInCategory.OST_StructuralColumns : BuiltInCategory.OST_StructuralFraming;
                    var sym = GetSymbol(cat, f.Section);
                    if (sym == null) { Skipped++; _log.AppendLine($"[{f.Id}] tidak ada family {cat} di project, dilewati."); continue; }

                    Level lvl = f.Kind == MemberKind.Column ? LevelBelow(a.Z) : LevelBelow(Math.Min(a.Z, b.Z) + 1e-3);
                    var st = f.Kind switch
                    {
                        MemberKind.Column => StructuralType.Column,
                        MemberKind.Brace => StructuralType.Brace,
                        _ => StructuralType.Beam,
                    };
                    var inst = _doc.Create.NewFamilyInstance(Line.CreateBound(a, b), sym, lvl, st);
                    inst.LookupParameter("Comments")?.Set($"ETABS {f.Id}");

                    if (f.Kind == MemberKind.Column) Columns++;
                    else if (f.Kind == MemberKind.Brace) Braces++;
                    else Beams++;
                }
                catch (Exception ex)
                {
                    Skipped++;
                    _log.AppendLine($"[{f.Id}] gagal: {ex.Message}");
                }
            }
        }

        Level LevelBelow(double z) =>
            _levels.Where(l => l.Elevation <= z + 1e-4).OrderByDescending(l => l.Elevation).FirstOrDefault()
            ?? _levels.OrderBy(l => l.Elevation).First();

        /// <summary>Cari type dengan nama section ETABS; jika tidak ada, duplikat type pertama dan set dimensi b/h.</summary>
        FamilySymbol GetSymbol(BuiltInCategory cat, string section)
        {
            string key = cat + "|" + (section ?? "");
            if (_symbolCache.TryGetValue(key, out var cached)) return cached;

            var symbols = new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).OfCategory(cat)
                .Cast<FamilySymbol>().ToList();
            if (symbols.Count == 0) return null;

            FamilySymbol sym = section == null ? null
                : symbols.FirstOrDefault(s => string.Equals(s.Name, section, StringComparison.OrdinalIgnoreCase));

            if (sym == null)
            {
                _m.Sections.TryGetValue(section ?? "", out var sec);
                bool circle = sec?.Shape?.ToLowerInvariant().Contains("circ") == true;
                var template = symbols.FirstOrDefault(s => (s.LookupParameter("b") != null && s.LookupParameter("h") != null) && !circle)
                               ?? symbols.FirstOrDefault(s => circle && s.LookupParameter("d") != null)
                               ?? symbols.First();
                if (section != null)
                {
                    sym = (FamilySymbol)template.Duplicate(section);
                    if (sec != null)
                    {
                        if (sec.Width > 0) sym.LookupParameter("b")?.Set(L(sec.Width));
                        if (sec.Depth > 0) sym.LookupParameter("h")?.Set(L(sec.Depth));
                        if (circle && sec.Depth > 0) sym.LookupParameter("d")?.Set(L(sec.Depth));
                    }
                    _log.AppendLine($"Type baru '{section}' dibuat dari '{template.FamilyName}: {template.Name}'.");
                }
                else sym = template;
            }

            if (!sym.IsActive) sym.Activate();
            _symbolCache[key] = sym;
            return sym;
        }

        static void TrySetName(Element e, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            try { e.Name = name; } catch { /* nama sudah dipakai */ }
        }

        class WarningSwallower : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                foreach (var f in fa.GetFailureMessages())
                    if (f.GetSeverity() == FailureSeverity.Warning) fa.DeleteWarning(f);
                return FailureProcessingResult.Continue;
            }
        }
    }
}
