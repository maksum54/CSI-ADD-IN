# CSI EXR Importer — Add-in Revit 2025

Import file **.exr** hasil export ETABS (CSiXRevit) ke Revit 2025:
level (story), grid, kolom, balok, dan bracing struktural.

## Build
Syarat: Windows, .NET 8 SDK, Revit 2025.
```
dotnet build src/CsiExrImporter -c Release
```

## Install
1. Copy `CsiExrImporter.addin` ke `%AppData%\Autodesk\Revit\Addins\2025\`
2. Copy isi `src/CsiExrImporter/bin/Release/net8.0-windows/` ke
   `%AppData%\Autodesk\Revit\Addins\2025\CsiExrImporter\`
3. Buka Revit 2025 → tab **CSI EXR**.

## Pemakaian
- **Import EXR**: pilih file .exr → konfirmasi → elemen dibuat dalam satu transaksi (bisa di-Undo).
- **Inspect EXR**: menampilkan tag-tag yang ada di file (untuk cek kalau hasil import kosong).

## Format EXR (biner, dari ETABS)
Format di-reverse-engineer dari contoh `OldTrafford_Simple.exr` (ETABS 22.3.0):
- Setiap objek diawali GUID, lalu Int32 id dan label.
- `id = -1` → Story (elevasi); `id = 0` → Grid (X1,Y1,Z1,X2,Y2,Z2);
  `id > 0` → Frame (X1,X2,Y1,Y2,Z1,Z2, rotasi, jenis 1=kolom/2=balok/3=bracing,
  nama section, nama family Revit, dimensi B/H).
- Semua panjang sudah dalam **feet** (satuan internal Revit), jadi tidak perlu konversi.

## Cara kerja
- Level dibuat dari Story (dilewati kalau elevasi sudah ada), grid sesuai nama.
- Type dicari berurutan: family EXR + nama section → nama section di family mana pun →
  duplikat type dari family EXR (beton, mis. `M_Concrete-Rectangular Beam`) dan isi `b`/`h` →
  fallback ke type pertama di kategori (dicatat di laporan).
- **Sebelum import:** load family beton (`M_Concrete-Square-Column`, `M_Concrete-Rectangular Beam`)
  dan buat/load type baja dengan nama persis section ETABS (`S-MAST`, `S-CHORD`, `S-WEB`, `S-PURLIN`, ...).
- Setiap elemen diberi `Comments = "ETABS <label>"`.
- Semua dalam satu transaksi → bisa Undo.

## Belum didukung
Rotasi penampang, pelat/dinding (area object), pondasi.
