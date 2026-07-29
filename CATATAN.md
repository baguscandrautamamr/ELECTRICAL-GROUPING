# Catatan

Keputusan teknis, hal yang masih manual, dan alasan di balik pilihan yang tidak
jelas dari kode. Tambahkan ke sini, jangan ke komentar kode.

---

## Yang sudah jalan

| | Status |
| --- | --- |
| Skema Supabase + RLS | Ditulis dan **diuji di Postgres sungguhan** (bukan hanya di-review) |
| Add-in Revit 2025 | `dotnet build` bersih tanpa warning, 43 test Core hijau |
| Web app | `pnpm lint`, `typecheck`, `build` bersih; layar dicek di dua tema dan dua bahasa |
| Auto build ZIP di GitHub | 3 workflow: add-in, web, database |
| Ikon | Satu SVG sumber, raster dihasilkan skrip |

---

## Yang masih perlu Anda lakukan sendiri

Tiga hal ini tidak bisa diselesaikan dari sesi ini.

### 1. Terapkan migrasi ke project Supabase

Host `fotpduvbevnncugruaom.supabase.co` **diblokir kebijakan jaringan** di
lingkungan tempat repo ini dikerjakan, jadi migrasi belum pernah dijalankan ke
project Anda. Isinya sudah diuji di Postgres 16 lokal beserta uji RLS-nya, tapi
Anda yang harus menembakkannya.

Pilih salah satu:

```bash
# lewat Supabase CLI
supabase link --project-ref fotpduvbevnncugruaom
supabase db push
```

atau buka **SQL Editor** di dashboard Supabase, tempel seluruh isi
`supabase/migrations/20260728000000_init.sql`, jalankan.

Setelah itu, di dashboard: **Authentication → Providers → Email**, pastikan
email provider aktif. Kalau ingin orang bisa mendaftar sendiri, biarkan
"Confirm email" menyala; kalau akun dibuat manual, matikan pendaftaran terbuka.

Untuk kode enam angka yang dipakai add-in, **Authentication → Email Templates →
Magic Link** harus memuat `{{ .Token }}` di badan email. Template bawaan Supabase
hanya berisi tautan, dan tautan tidak bisa dipakai dari dalam Revit.

### 2. Hubungkan Vercel

Web app adalah Next.js dengan proxy dan render sisi server, jadi **tidak bisa**
di GitHub Pages. GitHub dipakai untuk sumber kode dan CI; Vercel yang menampung.

Di Vercel: **Add New → Project → Import** repo ini, lalu:

| Setelan | Nilai |
| --- | --- |
| Root Directory | `web` |
| Framework Preset | Next.js (terdeteksi sendiri) |
| Build Command | bawaan |

Environment variable **tidak wajib** — `web/lib/supabase/config.ts` sudah memuat
URL project dan publishable key sebagai nilai bawaan, dan keduanya memang publik.
Kalau diisi, ingat bahwa `NEXT_PUBLIC_*` dibaca **saat build**: mengubahnya di
dashboard tidak berpengaruh sampai ada deploy berikutnya.

`NEXT_PUBLIC_SITE_URL` juga tidak wajib lagi. Tautan masuk disusun di browser dari
asal halaman yang sedang dibuka (lihat `siteUrl()`), jadi ia selalu menunjuk domain
yang benar-benar dipakai user. Isi hanya kalau ingin memaksa satu domain tertentu —
dan jangan pernah mengisinya dengan `http://localhost:3000` di Vercel.

Terakhir, di Supabase: **Authentication → URL Configuration**, isi Site URL dengan
`https://domain-anda.vercel.app` dan tambahkan `https://domain-anda.vercel.app/**`
ke daftar Redirect URLs. Tanpa itu tautan masuk ditolak.

### Kalau yang keluar 404 dari Vercel, bukan dari app

Bedakan dulu dua 404 yang kelihatan mirip:

| Yang terlihat | Asalnya | Artinya |
| --- | --- | --- |
| Kotak putih, `404: NOT_FOUND`, `Code: NOT_FOUND`, ada `ID: sin1::...` | Vercel | Tidak ada deployment di **hostname itu** |
| Halaman bertema CircuitSync, "Halaman itu tidak ada" | app kita | Deployment jalan, rutenya saja salah |

404 yang pertama hampir selalu berarti **hostname-nya keliru**, bukan app-nya
rusak. Nama domain `*.vercel.app` mengikuti nama **project di Vercel**, yang belum
tentu sama dengan nama repo, dan berubah kalau project pernah di-rename.

Cara memastikan domain yang benar:

- Vercel → project → **Domains**; atau ambil dari kartu deployment terbaru.
- Atau lihat field **homepage** di halaman GitHub repo ini — integrasi Vercel
  mengisinya sendiri dengan domain production.

`app/[locale]` tidak punya rute `/` sendiri; `/` dilayani redirect di `proxy.ts`
menuju `/{locale}/login`. Jadi kalau hostname-nya benar dan deployment-nya jadi,
membuka `/` akan berakhir di halaman login — bukan 404. Sudah diperiksa di build
production: `/` menjawab 307 ke `/id/login`.

Kalau tab **Deployments** kosong atau semuanya berlabel *Preview*, yang salah
Production Branch. Kalau ada Production berlabel **Error**, biasanya Root
Directory belum diarahkan ke `web`.

### Kalau web-nya jalan tapi isinya kosong atau gagal

Buka `https://domain-anda.vercel.app/id/setup` — tautannya juga ada di bawah kotak
login. Halaman itu memeriksa empat hal dari dalam deployment yang bermasalah:
apakah env var terbaca atau diam-diam jatuh ke nilai bawaan, apakah Auth Supabase
menjawab dengan key itu, apakah tabelnya sudah ada, dan redirect URL apa yang
sebenarnya dipakai tautan masuk. Halaman ini sengaja tidak dijaga login — ia justru
dibutuhkan saat login belum bisa dilewati.

Dua kegagalan paling sering, dan tandanya di halaman itu:

| Tanda | Sebab | Perbaikan |
| --- | --- | --- |
| "Tabel database sudah ada" merah, kode `42P01`/`PGRST205` | migrasi belum dijalankan | langkah 1 di atas |
| "Auth Supabase menjawab" merah | URL atau key salah | salin ulang dari Project Settings → API |

### 3. Uji add-in di Revit 2025

Tidak ada test otomatis untuk lapisan Revit, dan tidak ada Revit di lingkungan
ini. Yang sudah dipastikan: seluruh anggota API yang dipakai **terbukti compile**
terhadap reference assembly Revit 2025 (lihat `addin/docs/api-verified.md`).
Yang belum: perilakunya di aplikasi sungguhan.

Urutan uji manual yang disarankan:

1. Buka model kelistrikan, jalankan **Electrical → CircuitSync**.
2. Masuk, buat project, **Tarik model ke cloud**. Periksa jumlah device dan panel
   di log panel cocok dengan model.
3. Di web, pilih beberapa lampu, pilih panel, buat circuit, **Kirim ke Revit**.
4. Di Revit, **Ambil rencana dari web**. Yang harus terjadi:
   - circuit terbentuk dan tersambung ke panel,
   - nomor seperti `(LC)1` muncul di web setelah beberapa detik,
   - tag nomor muncul di samping lampu kalau view aktif adalah denah,
   - **satu Ctrl+Z membatalkan seluruh hasil apply.**

Poin terakhir itu yang paling penting diperiksa; kalau butuh lebih dari satu
Ctrl+Z, ada `TransactionGroup` yang tidak di-`Assimilate()`.

---

## Kenapa begini

### WPF tanpa XAML

Seluruh UI add-in dibangun dari C#, tanpa satu pun file `.xaml`.

Alasannya: XAML butuh `Microsoft.NET.Sdk.WindowsDesktop`, yang **tidak ada** di
paket .NET SDK versi Linux. Dengan XAML, `dotnet build` hanya jalan di Windows —
dan itu berarti agent, CI Linux, dan mesin siapa pun yang bukan Windows tidak bisa
memverifikasi apa pun sebelum push.

Ganti rugi teknisnya: reference assembly WPF diambil dari NuGet
(`Microsoft.WindowsDesktop.App.Ref`) dengan `ExcludeAssets="all"`, lalu
di-`<Reference>` manual. Hasil DLL-nya sama; yang hilang hanya kenyamanan
XAML designer.

Konsekuensi yang perlu diingat saat menambah layar: gaya seragam datang dari
`UiKit`, dan dua bahasa serta dua tema bekerja lewat dua daftar callback di sana
(`Restyle` dan `Retext`). Karena itu **jangan membangun ulang pohon visual** untuk
perubahan state — callback akan menumpuk. Sembunyikan dan tampilkan bagian saja;
`MainWindow` sudah mengikuti pola itu.

### Reference assembly Revit dari NuGet

`Nice3point.Revit.Api.RevitAPI` dan `...RevitAPIUI`, bukan `<Reference>` ke
`C:\Program Files\Autodesk\...`. Tanpa ini, build di GitHub Actions mustahil —
runner tidak punya Revit, dan DLL Revit tidak boleh didistribusikan.

Keduanya `PrivateAssets="all" ExcludeAssets="runtime"`, yang setara `Private=false`:
DLL Revit tidak ikut ke folder output, karena saat jalan yang dipakai adalah DLL
milik instalasi Revit. Sudah diperiksa — isi ZIP hanya lima DLL, tidak ada
RevitAPI di dalamnya.

### `CopyLocalLockFileAssemblies` di project Ui

Project library **tidak** menyalin DLL NuGet ke output secara bawaan; itu hanya
berlaku untuk executable. Tanpa properti ini,
`System.Security.Cryptography.ProtectedData.dll` tidak ikut terbungkus, dan
add-in meledak saat pertama kali menyimpan sesi login — jauh setelah build hijau.
Ini sudah pernah terjadi sekali di sesi ini dan diperbaiki.

### Nomor circuit tidak pernah ditulis

Nomor seperti `(LC)1` dibuat Revit sendiri saat `SelectPanel()` berhasil,
berdasarkan slot kosong di panel. Add-in hanya membacanya dari
`ElectricalSystem.CircuitNumber` setelah `doc.Regenerate()`, lalu mengirimnya
balik. Web menampilkan usulan tanpa nomor sampai balikan itu masuk.

Di Revit 2025 **tidak ada** `ElectricalSystem.BaseEquipment`. Kelayakan panel
dibaca dari `PanelName` yang kosong. Sudah diverifikasi lewat refleksi, bukan
diingat-ingat.

### `updated_at` dan sapuan snapshot

Add-in menyapu baris yang sudah tidak ada di model dengan membandingkan
`updated_at` terhadap stempel waktu awal snapshot. Itu jauh lebih murah daripada
mengirim daftar `not.in.(...)` berisi dua ribu id di URL.

Yang membuatnya benar adalah trigger `devices_touch`, `panels_touch`, dan
`levels_touch` di migrasi. **Kalau trigger itu dihapus, sapuan akan menghapus
device yang masih hidup.** Jangan sentuh yang satu tanpa yang lain.

### Null ikut dikirim ke PostgREST

`CircuitSyncJson.Options` **tidak** memakai `JsonIgnoreCondition.WhenWritingNull`,
dan itu bukan kelalaian.

PostgREST menolak bulk insert yang objek-objeknya tidak sekunci, dengan
`400 PGRST102 "All object keys must match"`. Kalau null dibuang saat serialisasi,
device yang punya `room_name` dan device yang tidak akan menghasilkan bentuk objek
yang berbeda di dalam satu array — dan **seluruh tarikan model gagal**, tanpa satu
baris pun masuk. Gejalanya di add-in cuma `Rencana ditolak: http_400`, yang tidak
menyebut kolom apa pun. Model nyata hampir pasti memicunya: cukup satu panel tanpa
distribution system, atau satu lampu tanpa nilai VA.

Alasan kedua: snapshot adalah pengganti penuh. Revit yang jadi sumber kebenaran
untuk kolom-kolom ini, jadi device yang room-nya dihapus di Revit harus menjadi
null di database. Membuang null membuat nilai lama menetap selamanya — dan membuat
patch yang bermaksud mengosongkan kolom, seperti membersihkan `error` saat job
akhirnya berhasil, diam-diam tidak berefek.

Dijaga oleh `Devices_with_and_without_nulls_carry_the_same_keys` dan
`Panels_with_and_without_nulls_carry_the_same_keys` di `ContractTests`.

### Kunci pesan, bukan teks, di kolom `error`

Add-in menulis kunci seperti `plan.panel_not_usable` ke `circuits.error`, bukan
kalimat. Web menerjemahkannya lewat namespace `revitErrors` di `messages/*.json`.
Konsekuensinya: **kunci baru di sisi C# harus ditambahkan ke kedua file
terjemahan pada commit yang sama**, kalau tidak yang muncul di layar adalah
pesan umum, bukan penjelasan yang berguna.

### Simbol 2D dari hash nama family

Bentuk bawaan tiap `family_key` dihitung dari FNV-1a nama family, bukan dari
urutan baris database. Kalau dari urutan, denah yang sama akan berubah bentuk
setiap kali data ditarik ulang, dan kalimat seperti "lampu segi enam di koridor"
jadi tidak berarti. Override manual per project disimpan di
`symbol_overrides`.

Status memakai warna **dan** bentuk garis sekaligus: putus-putus untuk belum
di-circuit, titik tengah untuk panel kosong, isian penuh untuk tersambung,
garis miring untuk tanpa connector. Denah yang dicetak hitam putih tetap terbaca.

### `proxy.ts`, bukan `middleware.ts`

Next.js 16 mengganti nama konvensi itu. File lama masih jalan tapi memunculkan
peringatan deprecation di setiap build.

Isinya mengerjakan dua hal berurutan: memilih locale, lalu menyegarkan session
Supabase. Urutannya penting — cookie baru ditulis ke response milik next-intl,
dan saat redirect terjadi cookie itu ikut dibawa. Tanpa itu, user seolah-olah
keluar sendiri beberapa menit sekali.

---

## Alat

`addin/tools/ApiProbe` membaca reference assembly Revit lewat refleksi metadata.
Dipakai untuk memastikan anggota API ada **sebelum** menulis kode yang memakainya,
tanpa perlu membuka Revit:

```bash
cd addin
dotnet run --project tools/ApiProbe -- type ElectricalSystem
dotnet run --project tools/ApiProbe -- enum BuiltInParameter RBS_ELEC PREFIX
dotnet run --project tools/ApiProbe -- find IndependentTag
```

Hasil yang sudah dipastikan dicatat di `addin/docs/api-verified.md`.

`supabase/tests/` berisi tiruan skema `auth` Supabase, jadi migrasi bisa diuji di
Postgres kosong:

```bash
createdb cs_test
psql -d cs_test -v ON_ERROR_STOP=1 \
  -f supabase/tests/harness.sql \
  -f supabase/migrations/20260728000000_init.sql \
  -f supabase/tests/smoke.sql
```

Workflow `database.yml` menjalankan ini di setiap perubahan pada `supabase/`.

---

## Cara mendapatkan ZIP add-in

Tiga jalan, semuanya menghasilkan isi yang sama:

1. **Artifact tiap build.** Buka tab **Actions → Add-in Revit 2025 → run terbaru**,
   ambil artifact `CircuitSync-Revit2025-1.0.0-build.N`. Tersimpan 90 hari.
2. **Release.** Push tag berawalan `v`, misalnya:

   ```bash
   git tag v1.0.0 && git push origin v1.0.0
   ```

   ZIP-nya otomatis dilampirkan ke release.
3. **Lokal.**

   ```bash
   cd addin && dotnet build -c Release
   # isi src/CircuitSync.Ui/bin/Release/net8.0-windows + installer/
   ```

---

## Fase

Kedua panduan di `addin/CLAUDE.md` dan `web/CLAUDE.md` masih menyebut **Fase 0**.
Isi repo ini sudah melewatinya: round-trip lengkap, auth, polling, WPF, dan web
app sekaligus — karena itu yang diminta.

Dua panduan itu belum diperbarui dengan sengaja; keduanya berisi aturan keras yang
masih berlaku, dan hanya bagian "Fase saat ini" yang sudah tidak akurat. Perbarui
bagian itu setelah uji manual di Revit 2025 selesai, supaya statusnya
mencerminkan sesuatu yang sudah terbukti, bukan sesuatu yang sudah ditulis.
