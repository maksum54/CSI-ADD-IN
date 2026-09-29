using System.Reflection;
using Autodesk.Revit.UI;

namespace CsiExrImporter
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication app)
        {
            const string tab = "CSI EXR";
            try { app.CreateRibbonTab(tab); } catch { /* tab sudah ada */ }
            var panel = app.CreateRibbonPanel(tab, "ETABS Link");
            string dll = Assembly.GetExecutingAssembly().Location;

            panel.AddItem(new PushButtonData("ImportExr", "Import\nEXR", dll, typeof(ImportExrCommand).FullName)
            { ToolTip = "Import model struktur (level, grid, kolom, balok, bracing) dari file .exr ETABS." });
            panel.AddItem(new PushButtonData("InspectExr", "Inspect\nEXR", dll, typeof(InspectExrCommand).FullName)
            { ToolTip = "Tampilkan ringkasan isi file .exr (untuk cek/debug struktur file)." });
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
    }
}
