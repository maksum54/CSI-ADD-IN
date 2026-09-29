using System.Collections.Generic;

namespace CsiExrImporter
{
    public enum MemberKind { Column = 1, Beam = 2, Brace = 3 }

    // Semua koordinat dalam feet (satuan internal Revit), sesuai isi file EXR.
    public class ExrStory { public string Name; public double Elevation; }
    public class ExrGrid { public string Name; public double X1, Y1, X2, Y2; }
    public class ExrPoint { public double X, Y, Z; }

    public class ExrSection
    {
        public string Name;       // nama section ETABS, mis. C-COL900
        public string Family;     // nama family Revit, mis. M_CONCRETE-SQUARE-COLUMN (kosong untuk baja)
        public Dictionary<string, double> Dims = new();   // mis. B, H (feet)
    }

    public class ExrFrame
    {
        public int Id;
        public string Label;
        public ExrSection Section;
        public MemberKind Kind;
        public ExrPoint Start, End;
        public double Rotation;   // radian
    }

    public class ExrModel
    {
        public string Source;     // mis. "ETABS 22.3.0"
        public List<ExrStory> Stories = new();
        public List<ExrGrid> Grids = new();
        public Dictionary<string, ExrSection> Sections = new(System.StringComparer.OrdinalIgnoreCase);
        public List<ExrFrame> Frames = new();
    }
}
