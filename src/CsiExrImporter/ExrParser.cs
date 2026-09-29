using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CsiExrImporter
{
    /// <summary>
    /// Pembaca file .exr biner dari ETABS (CSiXRevit). Format hasil reverse-engineering:
    /// setiap objek diawali GUID (string UInt16-length + 36 char), lalu Int32 id dan string label.
    ///   id == -1 : Story  -> double elevasi
    ///   id ==  0 : Grid   -> 48 byte, lalu X1,Y1,Z1,X2,Y2,Z2
    ///   id  >  0 : Frame  -> X1,X2,Y1,Y2,Z1,Z2, rotasi, ..., Int32 jenis (1 kolom, 2 balok, 3 bracing),
    ///                         string section, string family, pasangan (double nilai, 6 byte, string nama param)
    /// Semua panjang dalam feet.
    /// </summary>
    public static class ExrParser
    {
        public static ExrModel Parse(string path)
        {
            byte[] d = File.ReadAllBytes(path);
            var m = new ExrModel();
            var starts = FindGuids(d);

            for (int i = 0; i < starts.Count; i++)
            {
                int end = i + 1 < starts.Count ? starts[i + 1] : d.Length;
                int p = starts[i] + 38;
                if (p + 6 > end) continue;
                int id = BitConverter.ToInt32(d, p);
                if (!TryStr(d, p + 4, end, out string label, out int q)) continue;

                if (id == -1)
                {
                    if (q + 8 <= end) m.Stories.Add(new ExrStory { Name = label, Elevation = Dbl(d, q) });
                }
                else if (id == 0)
                {
                    if (q + 48 + 48 > end) continue;
                    double x1 = Dbl(d, q + 48), y1 = Dbl(d, q + 56), x2 = Dbl(d, q + 72), y2 = Dbl(d, q + 80);
                    if (Sane(x1, y1, x2, y2) && Math.Abs(x2 - x1) + Math.Abs(y2 - y1) > 1e-6)
                        m.Grids.Add(new ExrGrid { Name = label, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 });
                }
                else
                {
                    if (q + 70 > end) continue;
                    int kind = BitConverter.ToInt32(d, q + 66);
                    if (kind < 1 || kind > 3) continue;   // material, load pattern, dll.
                    double x1 = Dbl(d, q), x2 = Dbl(d, q + 8), y1 = Dbl(d, q + 16), y2 = Dbl(d, q + 24),
                           z1 = Dbl(d, q + 32), z2 = Dbl(d, q + 40);
                    if (!Sane(x1, x2, y1, y2, z1, z2)) continue;

                    m.Frames.Add(new ExrFrame
                    {
                        Id = id, Label = label, Kind = (MemberKind)kind,
                        Start = new ExrPoint { X = x1, Y = y1, Z = z1 },
                        End = new ExrPoint { X = x2, Y = y2, Z = z2 },
                        Rotation = Dbl(d, q + 48),
                        Section = ReadSection(d, q + 70, end, m),
                    });
                }
            }

            int etabs = IndexOf(d, Encoding.ASCII.GetBytes("ETABS"), 0);
            if (etabs > 0 && etabs + 9 < d.Length)
            {
                int n = BitConverter.ToInt32(d, etabs + 5);   // di footer panjang string memakai Int32
                if (n > 0 && n < 32 && etabs + 9 + n <= d.Length) m.Source = "ETABS " + Encoding.ASCII.GetString(d, etabs + 9, n);
            }
            return m;
        }

        static ExrSection ReadSection(byte[] d, int pos, int end, ExrModel m)
        {
            var names = new List<string>();
            while (pos < end - 2 && names.Count < 2)
            {
                if (TryStr(d, pos, end, out string s, out int next) && s.Length >= 2 && char.IsLetterOrDigit(s[0]))
                { names.Add(s); pos = next; }
                else pos++;
            }
            if (names.Count == 0) return null;
            string secName = names[0];
            if (m.Sections.TryGetValue(secName, out var sec)) return sec;

            sec = new ExrSection { Name = secName, Family = names.Count > 1 ? names[1] : null };
            // Kalau string kedua ternyata bukan family (baja tidak punya family), abaikan.
            if (sec.Family != null && sec.Family.Length < 3) sec.Family = null;

            // Parameter dimensi: double(8) + 6 byte nol + string panjang 1 (huruf besar)
            for (int j = pos + 14; j < end - 3; j++)
            {
                if (d[j] == 1 && d[j + 1] == 0 && d[j + 2] >= 'A' && d[j + 2] <= 'Z' && Zeros(d, j - 6, 6))
                {
                    double v = Dbl(d, j - 14);
                    if (v > 0 && v < 50) sec.Dims[((char)d[j + 2]).ToString()] = v;
                }
            }
            m.Sections[secName] = sec;
            return sec;
        }

        static List<int> FindGuids(byte[] d)
        {
            var res = new List<int>();
            for (int i = 0; i + 38 <= d.Length; i++)
            {
                if (d[i] != 0x24 || d[i + 1] != 0) continue;
                if (d[i + 10] == '-' && d[i + 15] == '-' && d[i + 20] == '-' && d[i + 25] == '-' && Hex(d, i + 2, 8))
                { res.Add(i); i += 37; }
            }
            return res;
        }

        static bool TryStr(byte[] d, int p, int end, out string s, out int next)
        {
            s = null; next = p;
            if (p + 2 > end) return false;
            int n = BitConverter.ToUInt16(d, p);
            if (n < 1 || n > 256 || p + 2 + n > end) return false;
            for (int k = 0; k < n; k++) if (d[p + 2 + k] < 32 || d[p + 2 + k] > 126) return false;
            s = Encoding.ASCII.GetString(d, p + 2, n); next = p + 2 + n;
            return true;
        }

        static double Dbl(byte[] d, int p) => BitConverter.ToDouble(d, p);
        static bool Sane(params double[] v) { foreach (var x in v) if (double.IsNaN(x) || Math.Abs(x) > 1e6) return false; return true; }
        static bool Zeros(byte[] d, int p, int n) { for (int k = 0; k < n; k++) if (d[p + k] != 0) return false; return true; }
        static bool Hex(byte[] d, int p, int n)
        {
            for (int k = 0; k < n; k++) { byte c = d[p + k]; if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false; }
            return true;
        }
        static int IndexOf(byte[] d, byte[] pat, int from)
        {
            for (int i = from; i <= d.Length - pat.Length; i++)
            {
                int k = 0; while (k < pat.Length && d[i + k] == pat[k]) k++;
                if (k == pat.Length) return i;
            }
            return -1;
        }
    }
}
