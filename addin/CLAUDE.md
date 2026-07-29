# CircuitSync — Revit add-in

Add-in Revit yang mengirim data kelistrikan model ke Supabase, menerima rencana
grouping circuit dari web app, lalu menerapkannya ke model: membuat circuit,
menyambung ke panel, dan menempatkan tag nomor circuit.

Repo pasangannya: `circuit-web` (Next.js). Kontrak data di bawah harus identik
di kedua repo — kalau berubah di sini, ubah juga di sana pada commit yang sama.

## Stack

- Revit 2025 API — target `net8.0-windows`, bukan .NET Framework
- C# 12, WPF untuk UI
- HTTP ke Supabase PostgREST pakai `HttpClient` + `System.Text.Json`

## Struktur solution

```
src/
  CircuitSync.Core/      C# murni. TIDAK BOLEH referensi RevitAPI.
  CircuitSync.Cloud/     Klien Supabase. TIDAK BOLEH referensi RevitAPI.
  CircuitSync.Revit/     Semua kode yang menyentuh Revit API.
  CircuitSync.Ui/        Window WPF, view model.
tests/
  CircuitSync.Core.Tests/
docs/
  api-verified.md        Signature Revit API yang sudah terbukti compile.
```

Arah dependensi satu arah: `Ui` → `Revit` → `Cloud` → `Core`. Core tidak
mengenal siapa pun. Kalau sebuah logika butuh referensi RevitAPI untuk bisa
ditulis, tanyakan dulu apakah logikanya bisa dipindah ke Core dengan menerima
DTO biasa — hampir selalu bisa, dan itu yang membuat kode ini bisa dites.

## Aturan keras

Delapan hal ini tidak boleh dilanggar tanpa persetujuan eksplisit.

**1. Jangan mengarang anggota Revit API.** Nama `BuiltInParameter`, signature
method, dan nama class sering berbeda antar versi. Sebelum memakai anggota API
yang belum tercatat di `docs/api-verified.md`, berhenti dan tanya. Jangan tulis
kode yang "kelihatannya benar". Setiap anggota baru yang sudah terbukti compile
dicatat ke file itu beserta versi Revit-nya.

**2. `ElementId.Value`, bukan `IntegerValue`.** Di Revit 2024+ ElementId sudah
64-bit dan `IntegerValue` deprecated. Tipenya `long`.

**3. Semua panggilan Revit API dari thread utama.** Window WPF kita non-modal,
jadi tidak boleh memanggil API langsung dari event handler UI. Semua aksi lewat
`IExternalEventHandler` + `ExternalEvent.Raise()`. Polling timer juga tunduk
aturan ini — timer hanya boleh menyiapkan payload lalu Raise.

**4. Satu `TransactionGroup` per operasi apply, lalu `Assimilate()`.** User harus
bisa membatalkan seluruh hasil apply dengan sekali Ctrl+Z. Tiap circuit dibungkus
`SubTransaction` sendiri supaya satu kegagalan tidak menggugurkan sisanya.

**5. `UniqueId` untuk identitas lintas sistem.** Apa pun yang disimpan ke
Supabase memakai `Element.UniqueId` (string), tidak pernah `ElementId`. Saat
apply, resolve lewat `doc.GetElement(uniqueId)` dan tangani hasil null sebagai
"elemen sudah dihapus" — dilaporkan, bukan dilempar.

**6. Tidak ada rahasia di dalam DLL.** DLL .NET bisa didekompilasi dalam
hitungan detik. Yang boleh ada di kode hanya URL project dan `anon` key.
`service_role` key tidak pernah menyentuh repo ini. Refresh token user disimpan
lewat DPAPI (`ProtectedData` scope `CurrentUser`), bukan file JSON polos.

**7. Jangan tulis nomor circuit.** Nomor seperti `(LC)1` dihasilkan Revit sendiri
saat `SelectPanel()` berhasil, berdasarkan slot kosong di panel. Tugas kita hanya
membacanya dari `ElectricalSystem.CircuitNumber` setelah apply dan mengirimnya
balik ke Supabase.

**8. Jangan pakai TextNote untuk label circuit.** Label di samping lampu adalah
`IndependentTag` yang membaca parameter `Circuit Number`. TextNote menghasilkan
data mati yang tidak ikut berubah saat circuit diubah.

## Dependensi pihak ketiga

Revit memuat semua add-in ke AppDomain yang sama, jadi dua add-in yang memakai
versi berbeda dari library yang sama akan bentrok dan gagal dengan pesan yang
menyesatkan. Konsekuensinya: **tambah NuGet package hanya kalau benar-benar
perlu, dan tanyakan dulu.** Untuk Supabase, `HttpClient` biasa terhadap
PostgREST sudah cukup — jangan tarik SDK penuh hanya untuk beberapa endpoint.

`System.Security.Cryptography.ProtectedData` adalah pengecualian yang sudah
disetujui; DPAPI bukan bagian dari .NET 8 secara default.

Referensi RevitAPI di-set `Private=false` supaya DLL Revit tidak ikut ter-copy
ke folder output.

## Glosarium domain

Istilah ini dipakai konsisten di kode, komentar, dan UI. Jangan bikin sinonim.

| Istilah | Arti |
| --- | --- |
| device | Lampu atau receptacle yang punya electrical connector |
| kind | `lighting` atau `receptacle` — dasar pemisahan tab dan circuit |
| panel | Electrical Equipment yang jadi tujuan circuit |
| prefix | `Circuit Prefix` di type panel, misal `(LC)` |
| distribution system | Wajib terisi di panel; kalau `None`, panel tidak layak dipakai |
| snapshot | Sekali tarik seluruh device, panel, level, family ke Supabase |
| plan | Usulan grouping dari web, belum diterapkan |
| apply | Menerapkan plan ke model Revit |

Empat status koneksi device, dipakai sama persis di web:

`unwired` belum di-circuit · `no_panel` sudah circuit tapi panel kosong ·
`connected` circuit dan panel terisi · `no_connector` tidak punya connector listrik

## Kontrak data

Field ini harus sama persis dengan repo web. Snake case di JSON, PascalCase di C#.

```
device:   revit_unique_id, kind, level_key, room_name, family_key,
          x_mm, y_mm, va, status, circuit_number, panel_unique_id
panel:    revit_unique_id, name, prefix, distribution_system,
          voltage, phase, slots_total, slots_used, is_usable
circuit:  id, panel_unique_id, kind, device_unique_ids[], circuit_number, status
sync_job: id, project_id, direction, status, payload, error, applied_at
```

`family_key` = `"{FamilyName}::{TypeName}"`. Ini kunci mapping simbol 2D di web.

`sync_job.status`: `queued` → `applied` | `failed`. Add-in hanya mengambil job
berstatus `queued` milik project yang terikat ke dokumen yang sedang terbuka.

## Pengikatan dokumen ke project

Setiap model menyimpan satu GUID project di `ProjectInformation` lewat
Extensible Storage. Itu satu-satunya sumber kebenaran tentang "model ini milik
project Supabase yang mana". Kalau GUID belum ada, dialog setting menawarkan
buat project baru atau hubungkan ke yang sudah ada. Nama project boleh diubah
dari setting add-in, tapi yang menyimpan nama adalah Supabase.

## Perintah

```bash
dotnet build                      # wajib lolos sebelum bilang selesai
dotnet test tests/CircuitSync.Core.Tests
```

Tidak ada test otomatis untuk lapisan Revit — harus dicoba manual di Revit 2025.
Karena itu jaga lapisan `CircuitSync.Revit` setipis mungkin: dia menerjemahkan,
tidak memutuskan.

## Selesai artinya

Sebuah perubahan dianggap selesai kalau: `dotnet build` bersih tanpa warning
baru, test Core hijau, anggota API baru sudah tercatat di `docs/api-verified.md`,
dan tidak ada rahasia baru yang masuk repo. Untuk perubahan yang menyentuh
lapisan Revit, sebutkan langkah uji manualnya di deskripsi PR.

## Fase saat ini

**Fase 0 — spike.** Target: satu command tanpa UI yang mendaftar panel beserta
kelayakan distribution system-nya, membuat satu circuit ke satu panel, dan
menempatkan satu tag. Belum ada auth, belum ada polling, belum ada WPF.

Jangan mengerjakan fase berikutnya sebelum fase ini terbukti jalan di Revit.
