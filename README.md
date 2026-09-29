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

## Cara kerja
- Satuan panjang dibaca dari tag `Units` (default meter) dan dikonversi ke satuan internal Revit.
- Section ETABS dicocokkan dengan nama Type di project. Jika tidak ada, Type pertama
  di kategori tsb diduplikat dengan nama section dan parameter `b`/`h` (atau `d` untuk lingkaran) diisi.
  **Load family kolom & balok beton/baja ke project dulu sebelum import.**
- Jenis frame diambil dari atribut `Type`; jika tidak ada, ditentukan dari geometri
  (vertikal = kolom, datar = balok, miring = bracing).
- Setiap elemen diberi `Comments = "ETABS <ID>"` untuk penelusuran.

## Catatan format
Parser membaca EXR sebagai XML dan toleran terhadap variasi nama tag/atribut
(`Story/Level`, `Grid`, `Point/Joint`, `Frame/Column/Beam/Brace`, `Section/FrameSection`,
koordinat `X1..Z2` atau referensi `Point1/Point2`). Kalau hasil import tidak lengkap,
jalankan **Inspect EXR** dan kirim hasilnya agar mapping disesuaikan.
