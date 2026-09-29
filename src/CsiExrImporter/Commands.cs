using System;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CsiExrImporter
{
    static class FilePicker
    {
        public static string Pick()
        {
            using var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Pilih file EXR dari ETABS",
                Filter = "ETABS Exchange (*.exr)|*.exr|XML (*.xml)|*.xml|Semua file (*.*)|*.*",
            };
            return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.FileName : null;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ImportExrCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = data.Application.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument) { message = "Buka project Revit terlebih dahulu."; return Result.Failed; }

            string path = FilePicker.Pick();
            if (path == null) return Result.Cancelled;

            ExrModel model;
            try { model = ExrParser.Parse(path); }
            catch (Exception ex)
            {
                TaskDialog.Show("CSI EXR", "File EXR tidak bisa dibaca:\n" + ex.Message);
                return Result.Failed;
            }

            var td = new TaskDialog("CSI EXR - Konfirmasi")
            {
                MainInstruction = "Import data berikut ke Revit?",
                MainContent = $"Sumber: {model.Source ?? "-"}\nStory: {model.Stories.Count}\nGrid: {model.Grids.Count}\n" +
                              $"Section: {model.Sections.Count}\nFrame: {model.Frames.Count} " +
                              $"(kolom {model.Frames.Count(f => f.Kind == MemberKind.Column)}, " +
                              $"balok {model.Frames.Count(f => f.Kind == MemberKind.Beam)}, " +
                              $"bracing {model.Frames.Count(f => f.Kind == MemberKind.Brace)})",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
            };
            if (td.Show() != TaskDialogResult.Ok) return Result.Cancelled;

            var b = new RevitBuilder(doc, model);
            try { b.Build(); }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }

            TaskDialog.Show("CSI EXR - Selesai",
                $"Level baru: {b.Levels}\nGrid baru: {b.Grids}\nKolom: {b.Columns}\nBalok: {b.Beams}\nBracing: {b.Braces}\nDilewati: {b.Skipped}" +
                (b.Log.Length > 0 ? "\n\nCatatan:\n" + Truncate(b.Log, 1500) : ""));
            return Result.Succeeded;
        }

        static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "\n...";
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class InspectExrCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            string path = FilePicker.Pick();
            if (path == null) return Result.Cancelled;
            try
            {
                var m = ExrParser.Parse(path);
                var sb = new StringBuilder($"Sumber: {m.Source ?? "-"}\n\nStory ({m.Stories.Count}):\n");
                foreach (var s in m.Stories) sb.AppendLine($"  {s.Name}  {s.Elevation * 0.3048:0.###} m");
                sb.AppendLine($"\nGrid ({m.Grids.Count}): " + string.Join(", ", m.Grids.Select(g => g.Name)));
                sb.AppendLine($"\nSection ({m.Sections.Count}):");
                foreach (var s in m.Sections.Values)
                {
                    int n = m.Frames.Count(f => f.Section == s);
                    string dims = string.Join(" ", s.Dims.Select(kv => $"{kv.Key}={kv.Value * 304.8:0}mm"));
                    sb.AppendLine($"  {s.Name} x{n}  {s.Family ?? "(baja)"}  {dims}");
                }
                TaskDialog.Show("CSI EXR - Inspect", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
