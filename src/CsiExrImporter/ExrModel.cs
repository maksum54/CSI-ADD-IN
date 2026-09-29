using System.Collections.Generic;

namespace CsiExrImporter
{
    public enum MemberKind { Column, Beam, Brace }

    public class ExrStory { public string Name; public double Elevation; }
    public class ExrGrid { public string Name; public double X1, Y1, X2, Y2; }
    public class ExrPoint { public double X, Y, Z; }

    public class ExrSection
    {
        public string Name;
        public string Material;
        public string Shape;      // Rectangular, Circle, I, dll.
        public double Depth;      // t3 / h
        public double Width;      // t2 / b
    }

    public class ExrFrame
    {
        public string Id;
        public string Section;
        public MemberKind Kind;
        public ExrPoint Start, End;
    }

    public class ExrModel
    {
        public string Units = "m";   // satuan panjang file
        public List<ExrStory> Stories = new();
        public List<ExrGrid> Grids = new();
        public Dictionary<string, ExrSection> Sections = new(System.StringComparer.OrdinalIgnoreCase);
        public List<ExrFrame> Frames = new();
    }
}
