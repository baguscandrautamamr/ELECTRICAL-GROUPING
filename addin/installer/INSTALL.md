# Memasang CircuitSync di Revit 2025

## Isi ZIP

```
CircuitSync.addin      manifest yang dibaca Revit
CircuitSync/           DLL add-in
Install.ps1            penyalin otomatis
INSTALL.md             file ini
```

## Cara cepat

1. Tutup Revit. Revit memegang DLL selama aplikasinya terbuka.
2. Ekstrak ZIP ini ke folder mana pun.
3. Klik kanan `Install.ps1` → **Run with PowerShell**.
   Kalau Windows menolak menjalankan skrip, buka PowerShell di folder itu lalu:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Install.ps1
   ```

4. Buka Revit 2025. Tab **Electrical** → panel **CircuitSync** → tombol
   **Grouping Circuit**.

Untuk memasang bagi semua user di satu mesin (butuh PowerShell as administrator):

```powershell
.\Install.ps1 -AllUsers
```

Melepas kembali:

```powershell
.\Install.ps1 -Uninstall
```

## Cara manual

Salin dua hal ini ke folder Addins Revit:

| Dari ZIP | Ke |
| --- | --- |
| `CircuitSync.addin` | `%APPDATA%\Autodesk\Revit\Addins\2025\` |
| folder `CircuitSync\` | `%APPDATA%\Autodesk\Revit\Addins\2025\CircuitSync\` |

Struktur akhirnya:

```
%APPDATA%\Autodesk\Revit\Addins\2025\
  CircuitSync.addin
  CircuitSync\
    CircuitSync.dll
    CircuitSync.Core.dll
    CircuitSync.Cloud.dll
    CircuitSync.Revit.dll
    System.Security.Cryptography.ProtectedData.dll
```

## Pemakaian pertama

1. **Masuk.** Pakai email dan kata sandi, atau minta kode enam angka dikirim ke
   email. Sesi tersimpan lokal dan terenkripsi per akun Windows, jadi tidak perlu
   masuk lagi setiap kali membuka Revit.
2. **Ikat dokumen ke project.** Buat project baru dari panel, atau hubungkan ke
   project yang sudah ada. GUID project disimpan di dalam file Revit — dokumen
   yang sama akan tetap terikat di komputer siapa pun.
3. **Tarik model ke cloud.** Ini yang mengisi denah 2D di web app.
4. Kelompokkan circuit di web, tekan **Kirim ke Revit**.
5. Kembali ke Revit, tekan **Ambil rencana dari web** — atau nyalakan pengambilan
   otomatis.

## Kalau tombolnya tidak muncul

- Pastikan versinya Revit **2025**. Add-in ini dibangun untuk `net8.0-windows`;
  Revit 2024 dan sebelumnya memakai .NET Framework dan tidak akan memuatnya.
- Periksa `CircuitSync.addin` benar-benar berada langsung di dalam folder
  `Addins\2025`, bukan di dalam subfolder.
- Buka `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit 2025\Journals` dan cari
  baris yang menyebut CircuitSync; Revit mencatat kegagalan memuat add-in di situ.

## Kalau tag nomor circuit tidak muncul

Tag hanya bisa ditempatkan kalau:

- view aktif adalah view denah, dan
- family tag untuk Lighting Fixture Tags atau Electrical Fixture Tags sudah
  di-load di project, dengan label yang menunjuk parameter `Circuit Number`.

Tanpa itu, circuit tetap dibuat dan disambung ke panel — hanya labelnya yang
tidak ada. Itu urusan template project, bukan sesuatu yang bisa dibuat add-in
dari API.
