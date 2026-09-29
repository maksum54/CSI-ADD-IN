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
        readonly StringBuilder _log = new();
        readonly HashSet<string> _logged = new();
        readonly List<Level> _levels = new();
        readonly Dictionary<string, FamilySymbol> _symbolCache = new(StringComparer.OrdinalIgnoreCase);

        public int Levels, Grids, Columns, Beams, Braces, Skipped;
        public string Log => _log.ToString();

        public RevitBuilder(Document doc, ExrModel m) { _doc = doc; _m = m; }

        static XYZ P(ExrPoint p) => new(p.X, p.Y, p.Z);   // EXR sudah dalam feet

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
                if (_levels.Any(l => Math.Abs(l.Elevation - s.Elevation) < 1e-4)) continue;
                var lvl = Level.Create(_doc, s.Elevation);
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
                var a = new XYZ(g.X1, g.Y1, 0); var b = new XYZ(g.X2, g.Y2, 0);
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
                    if (sym == null) { Skipped++; Note($"nofam|{cat}", $"Tidak ada family {Cat(cat)} di project, elemen dilewati."); continue; }

                    Level lvl = LevelBelow(Math.Min(a.Z, b.Z) + 1e-3);
                    var st = f.Kind switch
                    {
                        MemberKind.Column => StructuralType.Column,
                        MemberKind.Brace => StructuralType.Brace,
                        _ => StructuralType.Beam,
                    };
                    var inst = _doc.Create.NewFamilyInstance(Line.CreateBound(a, b), sym, lvl, st);
                    inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"ETABS {f.Label}");

                    if (f.Kind == MemberKind.Column) Columns++;
                    else if (f.Kind == MemberKind.Brace) Braces++;
                    else Beams++;
                }
                catch (Exception ex)
                {
                    Skipped++;
                    Note("err|" + ex.Message, $"[{f.Label}] gagal: {ex.Message}");
                }
            }
        }

        Level LevelBelow(double z) =>
            _levels.Where(l => l.Elevation <= z + 1e-4).OrderByDescending(l => l.Elevation).FirstOrDefault()
            ?? _levels.OrderBy(l => l.Elevation).First();

        /// <summary>
        /// Urutan pencarian type: (1) family EXR + nama section, (2) nama section di family manapun,
        /// (3) duplikat type dari family EXR (beton) lalu isi b/h, (4) type pertama di kategori.
        /// </summary>
        FamilySymbol GetSymbol(BuiltInCategory cat, ExrSection sec)
        {
            string key = cat + "|" + sec?.Name;
            if (_symbolCache.TryGetValue(key, out var cached)) return cached;

            var symbols = new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).OfCategory(cat)
                .Cast<FamilySymbol>().ToList();
            if (symbols.Count == 0) return null;

            FamilySymbol sym = null;
            if (sec != null)
            {
                var famSyms = sec.Family == null ? new List<FamilySymbol>()
                    : symbols.Where(s => Same(s.FamilyName, sec.Family)).ToList();
                sym = famSyms.FirstOrDefault(s => Same(s.Name, sec.Name))
                      ?? symbols.FirstOrDefault(s => Same(s.Name, sec.Name));

                if (sym == null && famSyms.Count > 0)
                {
                    sym = (FamilySymbol)famSyms[0].Duplicate(sec.Name);
                    SetDims(sym, sec);
                    Note("dup|" + sec.Name, $"Type baru '{sec.Family}: {sec.Name}' dibuat.");
                }
            }
            if (sym == null)
            {
                sym = symbols[0];
                Note("fb|" + sec?.Name, $"Section '{sec?.Name}'" + (sec?.Family != null ? $" (family {sec.Family})" : "") +
                     $" tidak ditemukan, pakai '{sym.FamilyName}: {sym.Name}'. Load family/type yang sesuai lalu import ulang.");
            }

            if (!sym.IsActive) sym.Activate();
            _symbolCache[key] = sym;
            return sym;
        }

        static void SetDims(FamilySymbol sym, ExrSection sec)
        {
            sec.Dims.TryGetValue("B", out double bw);
            sec.Dims.TryGetValue("H", out double h);
            if (h <= 0) h = bw;          // kolom persegi hanya punya B
            if (bw <= 0) bw = h;
            if (bw > 0) Set(sym, bw, "b", "B", "Width");
            if (h > 0) Set(sym, h, "h", "H", "Depth");
        }

        static void Set(FamilySymbol sym, double v, params string[] names)
        {
            foreach (var n in names)
            {
                var p = sym.LookupParameter(n);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double) { p.Set(v); return; }
            }
        }

        void Note(string key, string msg) { if (_logged.Add(key)) _log.AppendLine(msg); }
        static bool Same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
        static string Cat(BuiltInCategory c) => c == BuiltInCategory.OST_StructuralColumns ? "Structural Column" : "Structural Framing";

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
