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

Domain itu juga dipakai tombol **Buka web app** di add-in. Nilai bawaannya
(`https://revitgrouping.vercel.app`) ikut terkompilasi ke DLL dan belum tentu
cocok dengan deployment Anda, jadi panel punya kolom **Alamat web app** — tempel
domain yang benar di situ, tersimpan di `settings.json` dan tidak perlu build ulang.
Tombolnya membuka halaman project yang terikat ke dokumen, bukan beranda. Untuk
memasangnya sekali untuk semua pengguna, pakai `CIRCUITSYNC_WEB_URL` atau
`circuitsync.json`.

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

### Model terkirim sendiri, halaman melihat lagi sendiri

Dua arah sinkronisasi punya pemicu yang berbeda, dan dulu hanya satu yang otomatis.
Arah web → Revit sudah punya polling; arah Revit → web selalu manual, menunggu
seseorang menekan "Tarik model ke cloud". Akibatnya lampu atau family yang baru
ditambahkan tidak pernah sampai ke web, dan yang terlihat adalah web yang "tidak
cocok dengan Revit" — padahal web belum pernah diberi tahu.

Sekarang tiga hal bekerja bersama:

1. `CircuitSyncApp` berlangganan `ControlledApplication.DocumentChanged` dan menandai
   model kotor. Handler-nya hanya menandai — event itu berjalan pada setiap transaksi
   Revit, termasuk milik add-in lain.
2. Detak timer di `SyncController.Tick` mengirim ulang model kalau ada tanda itu, lalu
   mengambil rencana dari web. Urutannya disengaja: rencana divalidasi terhadap model
   terbaru, bukan terhadap model yang sudah berubah sejak tarikan terakhir.
3. `AutoRefresh` di web menarik ulang data halaman secara berkala, dan hanya saat tab
   benar-benar terlihat.

Penyaringnya penting: `TouchesElectrical` hanya menandai kotor kalau perubahan
menyentuh lighting fixture, electrical fixture, atau electrical equipment. Memindahkan
dinding tidak perlu menghasilkan tarikan model seukuran gudang. Perubahan yang memuat
lebih dari 500 elemen langsung dianggap relevan — memeriksanya satu per satu di thread
utama Revit lebih mahal daripada satu tarikan yang mungkin sia-sia.

#### Kenapa tidak berat

`ModelReader.Read` berjalan di thread utama Revit: ia memindai seluruh fixture,
menghitung ruangnya, dan menghitung isi tiap view denah. Menjalankannya tiap detak
selama user menggambar akan terasa sebagai Revit yang tersendat berkala. Empat rem
menahannya:

| Rem | Efek |
| --- | --- |
| **Jeda tenang** 10 detik | Model harus diam dulu. Tarikan terjadi di sela pekerjaan, bukan di tengahnya. |
| **Jarak minimum** 60 detik | Sesi panjang yang penuh jeda pendek tidak menghasilkan tarikan beruntun. |
| **Lewati saat sibuk** | Snapshot model besar bisa lebih lama daripada intervalnya; menumpuknya hanya membuat request saling mendahului. |
| **Sidik jari** | `ModelSnapshot.Fingerprint()` membandingkan isi terhadap tarikan terakhir yang berhasil. Sama berarti unggahan beserta lima DELETE sapuannya dibatalkan. |

Sidik jari dihitung di luar thread Revit, dan barisnya diurutkan lebih dulu karena
`FilteredElementCollector` tidak menjamin urutan. Hash-nya dihitung sendiri, bukan
lewat `string.GetHashCode()`, yang di .NET diacak per proses.

Tanda kotor baru dibersihkan setelah tarikan benar-benar selesai — kegagalan jaringan
tidak boleh membuat perubahan hilang diam-diam. Tarikan manual selalu jadi, sekalipun
sidik jarinya sama: user yang menekan tombol berhak melihat sesuatu terjadi, dan itu
satu-satunya jalan memulihkan cloud yang pernah diubah dari luar.

Di sisi web, yang menanggung sebagian besar pekerjaan adalah kembalinya fokus tab,
bukan timer. Alur kerja sebenarnya adalah menggambar di Revit lalu berpindah ke
browser, jadi menyegarkan tepat saat tab kembali terlihat terasa seketika dan tidak
berongkos apa pun selama tab ditinggalkan. Timer hanya jaring pengaman, dan sengaja
lambat — 60 detik, atau 5 detik selagi menunggu Revit mengerjakan antrean.

### Hak tabel tidak ikut lahir bersama tabelnya

`init.sql` memberi `grant select, insert, update, delete on all tables in schema
public to authenticated`. Itu berlaku **sekali, untuk tabel yang ada saat itu** —
tabel yang lahir di migrasi berikutnya tidak ikut.

Di Supabase kelalaian ini tidak pernah terlihat: project-nya sudah punya
`ALTER DEFAULT PRIVILEGES` bawaan yang memberi hak otomatis pada tabel baru. Di
Postgres kosong — termasuk yang dipakai workflow `database` — tidak ada itu, dan
bedanya muncul sebagai `permission denied for table` yang sama sekali tidak menyebut
RLS, sehingga mudah disalahartikan sebagai policy yang salah.

`layouts` sempat kehilangan haknya karena ini; disusulkan di migrasi
`20260729020000`. Aturannya sekarang: **setiap migrasi yang membuat tabel harus
memuat grant-nya sendiri**, sebaris di sebelah `enable row level security`.

Uji RLS di `smoke.sql` tidak menangkapnya karena hanya menyentuh tabel dari migrasi
pertama.

### Level device yang di-host harus disimpulkan

Device yang diletakkan di lantai punya `LevelId` sendiri. Yang di-host di dinding
atau ceiling sering tidak: family berbasis face menyimpan levelnya di parameter
`Schedule Level`, dan kadang tidak menyimpannya sama sekali. Dulu semua kasus itu
jatuh ke `unassigned`, dan karena halaman denah menyaring per level, device tersebut
**terbaca dari model tapi tidak pernah muncul di web**. Gejalanya persis seperti yang
dilaporkan: stop kontak lantai terlihat, stop kontak dinding hilang.

`ModelReader.LevelKeyOf` sekarang mencari bertingkat: `LevelId` → level host →
parameter `Schedule Level` → simpulan dari ketinggian. Langkah terakhir hidup di
`LevelFinder` di Core, karena ini keputusan yang bisa salah dan karena itu harus bisa
dites tanpa Revit.

Aturannya "level tertinggi yang masih di bawah titik ini", bukan "elevasi terdekat".
Saklar di 3.700 mm tetap milik lantai di kakinya; mencari yang terdekat akan memilih
lantai di atas kepalanya. Toleransi 300 mm ke atas menjaga stop kontak lantai yang
tertanam sedikit di bawah pelat tetap milik level itu.

Dua penopang di sisi web, supaya kegagalan penyimpulan tidak lagi berarti device
hilang. Pertama, keanggotaan layout menang atas `level_key` — kalau view Revit bilang
device itu ada di denah, ia ditampilkan berapa pun levelnya. Kedua, halaman project
menyebut berapa device yang tidak tampak di denah mana pun lewat
`devices_without_layout`, jadi "jumlahnya kurang" punya petunjuk alih-alih sekadar
terasa janggal.

Pengambilan device juga dihalaman sekarang: seribu baris adalah batas potong PostgREST,
dan tanpa halaman model besar kehilangan titik tanpa error dan tanpa tanda apa pun.

### Fitur dari migrasi baru tidak boleh menjatuhkan halaman

`classifyError` memperlakukan `PGRST202` — fungsi tidak ditemukan — sama dengan tabel
yang hilang: `schema`, yang berarti layar "Database belum disiapkan". Begitu halaman
project mulai memanggil `layout_device_counts`, database yang sudah berisi tapi belum
menerima migrasi terbaru langsung tampil sebagai database kosong. Halaman `/setup` di
sebelahnya tetap bilang tabelnya ada, karena ia hanya menyentuh `projects` — dua layar
yang saling membantah, dan tidak satu pun menyebut penyebabnya.

Aturannya sekarang: **hanya tabel inti yang boleh memicu `SetupNeeded`.** Apa pun yang
datang dari migrasi lebih baru dibaca lewat `optional()`, yang mengembalikan null saat
query gagal, dan halaman kembali ke perilaku sebelum fitur itu ada.

Halaman `/setup` juga memeriksa migrasi terbaru sendiri, dan menyebut nama berkas yang
harus dijalankan. Ditandai tanda tanya, bukan silang: kekurangan ini bukan kegagalan —
aplikasi tetap jalan, hanya kembali ke perilaku lama.

### Isi denah ditentukan view, bukan pasangan (level, kind)

Satu lantai punya lebih dari satu denah lighting. Di model FG WAREHOUSE ada
`… - LIGHTING SYSTEM LAYOUT PLAN` dan `… - EMERGENCY & EXIT LIGHTING SYSTEM
LAYOUT PLAN`, dan keduanya berlantai sama serta — karena `LayoutFilter` melihat
kata "LIGHTING" — berjenis sama. Selama web memilih device lewat pasangan
(`level_key`, `kind`), kedua halaman itu menampilkan isi yang **persis sama**,
termasuk lampu emergency dan exit yang di Revit justru disembunyikan dari denah
lighting biasa.

Yang menentukan isi sebuah denah adalah view Revit-nya: filter view, visibility
per kategori, crop region, dan fase. `ModelReader.VisibleDeviceIds` membacanya
lewat `FilteredElementCollector` yang dibatasi `view.Id` — penyaringnya sama
persis dengan yang dilihat mata di Revit — dan hasilnya disimpan di tabel
`layout_devices`.

Foreign key-nya composite dan cascade dua arah, jadi sapuan snapshot yang
menghapus layout atau device ikut membawa keanggotaannya; tidak ada baris yatim
yang menunjuk view atau device yang sudah tidak ada.

Web jatuh kembali ke perilaku lama **hanya** kalau project itu belum punya satu
pun baris `layout_devices` — tanda model terakhir ditarik add-in versi lama.
Pemeriksaannya per project, bukan per layout, supaya denah yang memang kosong
tidak diam-diam berubah jadi "tampilkan seluruh isi lantai".

Konsekuensi yang diterima: device yang tampak di sebuah view tapi levelnya beda
tidak ikut, karena query dasarnya masih disaring `level_key` milik layout. Itu
menjaga jumlah baris tetap terbatas — PostgREST memotong hasil di seribu baris
tanpa memberi tahu, dan menarik seluruh device satu project bisa melewatinya.

### Write-back setelah apply memakai PATCH, bukan upsert

Konsekuensi langsung dari keputusan di atas, dan sempat merusak data.

Setelah apply, add-in memperbarui status device supaya denah di web berubah hijau
tanpa menunggu tarikan model berikutnya. Dulu itu dikirim sebagai upsert baris
`devices` yang hanya diisi status dan nomor circuit. Karena null ikut ditulis dan
upsert PostgREST (`Prefer: resolution=merge-duplicates`) menimpa **seluruh** kolom
yang ada di body, setiap device yang baru saja di-circuit kehilangan `x_mm`,
`y_mm`, `level_key`, `family_key`, `room_name`, dan `va`. Gejalanya: titik yang
sudah tersambung menumpuk di koordinat 0,0 atau hilang dari denah, dan satu-satunya
cara memulihkannya adalah menarik ulang model dari Revit.

Sekarang yang dikirim adalah `DeviceConnection` — tiga field, lewat PATCH yang
disaring `revit_unique_id=in.(…)`. PATCH hanya menyentuh kolom yang benar-benar ada
di body, jadi geometri tidak pernah ikut tersentuh. Dijaga oleh
`Device_connection_carries_only_the_two_columns_that_change` di `ContractTests`.

Aturan turunannya: **jangan pernah memakai `DeviceRow` untuk pembaruan parsial.**
`DeviceRow` adalah baris penuh, dan hanya sah dipakai oleh snapshot.

### Device tanpa `LocationPoint`

Tidak semua fixture punya `LocationPoint` — yang di-host di face atau berbasis garis
memakai `LocationCurve`, dan sebagian tidak punya location sama sekali. Dulu semuanya
dibaca sebagai 0,0, yang di web tampak sebagai setumpuk simbol menindih di pojok
denah. `ModelReader.PointOf` sekarang turun bertingkat: `LocationPoint` → titik tengah
`LocationCurve` → pusat bounding box.

### Mengubah circuit berarti membongkar lalu membuat ulang

Tombol ubah di web menulis isi baru ke baris `circuits`, mengembalikan statusnya ke
`draft`, tapi **mempertahankan `revit_unique_id`**. Field itulah penanda bagi add-in
bahwa circuit ini sudah hidup di model: sebelum membuat yang baru, `CircuitApplier`
menghapus `ElectricalSystem` lama lewat `Document.Delete`. Tanpa itu Revit menolak
seluruh rencana, karena satu device tidak boleh berada di dua power circuit.

Yang harus Anda tahu sebagai pemakai: **nomornya bisa berubah.** `SelectPanel`
memilih slot kosong sendiri, dan slot yang baru saja dilepas belum tentu yang
dipakainya lagi. Mempertahankan nomor butuh `AddToCircuit`/`RemoveFromCircuit`, yang
belum diverifikasi terhadap reference assembly 2025 — lihat `addin/docs/api-verified.md`.

`PlanValidator` ikut tahu soal ini: device berstatus `connected` biasanya ditolak,
kecuali kalau ia memang anggota `ElectricalSystem` yang sedang dibangun ulang.
Pemetaannya dibawa `ModelSnapshot.DeviceSystems`, yang sengaja tidak masuk database —
itu bahan validasi, bukan kolom tabel.

### Semua job antrean dikerjakan, bukan yang pertama saja

`SyncController.CheckJobs` dulu mengambil `jobs[0]`. Akibatnya dua kali "Kirim ke
Revit" dari web berarti dua kali klik "Ambil rencana" di add-in, dan sisanya diam di
antrean tanpa penjelasan. Sekarang seluruh job dikerjakan berurutan sampai habis.

Model dibaca ulang untuk **setiap** job, bukan sekali di awal: job sebelumnya baru
saja mengubah model, dan memvalidasi job berikutnya terhadap snapshot basi akan
menolak device yang sebenarnya sah.

Alur itu berpindah-pindah antara thread Revit dan jaringan, jadi `RevitTaskQueue`
punya `PostAsync` yang bisa di-`await`. `TaskCreationOptions.RunContinuationsAsynchronously`
di dalamnya wajib — tanpa itu panggilan HTTP setelahnya ikut berjalan di thread utama
Revit dan membekukan aplikasi.

### Pesan Revit ikut disimpan, bukan hanya kunci kita

`apply.panel_rejected` adalah pesan **kami**, dan ia benar tapi tidak berguna:
"Revit menolak sambungan ke panel itu" tidak memberi tahu apa yang harus diperbaiki.
Yang membedakan panel penuh dari tegangan yang tidak cocok hanya ada di kalimat
Revit sendiri — dan kalimat itu dulu ditangkap di `CircuitApplier` lalu dibuang saat
write-back, yang hanya mengirim `ErrorKey`.

Sekarang `circuits.error` memuat dua bagian: **kunci di baris pertama, penjelasan
mentah Revit di baris berikutnya.** Web menerjemahkan baris pertama dan menampilkan
sisanya apa adanya di bawahnya. Dipisah baris, bukan kolom baru, supaya tidak perlu
migrasi hanya untuk memindahkan informasi yang sudah ada di tangan.

Penjelasannya diperkaya keadaan panel saat ditolak — nama, slot terpakai, jumlah titik
— karena tiga angka itu biasanya sudah cukup membedakan sebabnya tanpa membuka Revit.
Dropdown panel di web juga menyebut slot, supaya panel yang hampir penuh terlihat
sebelum dipilih, bukan sesudah ditolak.

### Kunci pesan, bukan teks, di kolom `error`

Add-in menulis kunci seperti `plan.panel_not_usable` ke `circuits.error`, bukan
kalimat. Web menerjemahkannya lewat namespace `revitErrors` di `messages/*.json`.
Konsekuensinya: **kunci baru di sisi C# harus ditambahkan ke kedua file
terjemahan pada commit yang sama**, kalau tidak yang muncul di layar adalah
pesan umum, bukan penjelasan yang berguna.

### Memilih titik yang berdesakan

Tiga hal yang berbeda menyamar sebagai satu keluhan "susah memilih titik yang
bertumpuk", dan masing-masing butuh jawaban sendiri.

**Simbolnya kebesaran.** Jari-jari dulu dihitung dari besar denah
(`max(lebar, tinggi) / 90`), jadi gudang yang lampunya berjarak rapat mendapat simbol
yang saling menindih — bukan karena titiknya bertumpuk, tapi karena gambarnya terlalu
gemuk. Sekarang `densityRadius` memakai **median** jarak ke tetangga terdekat, dengan
batas bawah 55% ukuran dasar. Perbandingannya dibatasi contoh 300 titik, jadi biayanya
tidak tumbuh bersama denah.

Percobaan pertama memakai persentil ke-20 dan hasilnya terlalu kecil untuk dilihat —
titik sebesar debu. Sebabnya: persentil bawah didominasi pasangan yang hampir berimpit,
jadi beberapa titik yang menempel membuat seluruh denah ikut menciut. Median tidak
peduli pada ekor itu. Pelajarannya: simbol yang terbaca lebih penting daripada simbol
yang dijamin tidak bersinggungan, karena tumpang tindih sudah punya obatnya sendiri
(zoom dan klik berulang) sedangkan titik yang tak terlihat tidak.

**Tidak bisa mendekat.** Kanvas sekarang bisa di-zoom dengan scroll dan digeser dengan
tombol tengah, seperti di Revit. Tinggi viewBox selalu mengikuti lebarnya, jadi
rasionya tidak pernah berubah — itulah yang menjaga pemetaan layar ke koordinat model
tetap linear, tanpa perlu memikirkan letterbox. Tetap SVG polos; tidak ada library
kanvas yang ditambahkan.

**Ada yang benar-benar di titik yang sama.** Zoom tidak menolong dua device di
koordinat identik. Klik sekarang ditangani di tingkat SVG, bukan per simbol: ia
mengumpulkan semua yang tersentuh, terdekat lebih dulu, dan klik berikutnya di tempat
yang sama berpindah ke yang di bawahnya.

Ambang klik dan besar sasaran diukur dalam **piksel layar**, bukan milimeter model.
Jari-jari simbol mengecil saat denah dilihat utuh, dan ambang yang ikut mengecil
membuat getaran tangan beberapa piksel terbaca sebagai tarikan seleksi yang tidak
memilih apa pun.

Listener `wheel` dipasang sendiri lewat `addEventListener` karena React memasangnya
sebagai passive, dan listener passive tidak boleh memanggil `preventDefault` — tanpa
itu halaman ikut ter-scroll setiap kali denah di-zoom.

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

### Isi panel dibaca dari model, bukan disimpulkan dari tabel `circuits`

Tabel `circuits` hanya memuat circuit yang pernah dibuat lewat web. Model sungguhan
hampir selalu sudah punya circuit yang dibuat langsung di Revit, jauh sebelum
project di web ada, dan circuit seperti itu tidak punya baris di sana sama sekali.
Kalau isi panel disimpulkan dari tabel itu saja, panel yang di Revit penuh tampak
kosong di web — jawaban yang bukan sekadar kurang, tapi menyesatkan.

Karena itu ada `devices.panel_unique_id`, diisi add-in dari model. Jalur
menyimpulkannya dari `circuit_number` sengaja **tidak** diambil: nomor seperti
`(LC)1` memang berawalan prefix panel, tapi dua panel boleh punya prefix yang sama,
dan tebakan yang salah di sini menghasilkan angka yang kelihatan masuk akal —
kesalahan yang paling sulit ketahuan.

`panel_contents` tetap punya jalur cadangan lewat circuit berstatus `applied`,
supaya model yang belum ditarik ulang add-in versi ini tidak kehilangan apa yang
sudah dikerjakan dari web. Yang belum ditarik ulang hanya kehilangan circuit yang
dibuat langsung di Revit.

### Add-in lebih baru daripada database adalah keadaan biasa

Keduanya dipasang terpisah: ZIP add-in dipasang user dari Actions, migrasi
ditembakkan lewat `supabase db push`. Selisih di antara keduanya bukan kemungkinan
jauh — ia terjadi pada percobaan pertama kolom `panel_unique_id`, dan gejalanya
seluruh tarikan model gagal dengan

```
http_400 — Could not find the 'panel_unique_id' column of 'devices' in the schema cache
```

Satu kolom baru menjatuhkan 639 device yang tidak ada hubungannya dengan kolom itu.

`SupabaseClient.WriteAsync` sekarang membaca `PGRST204`, membuang kolom yang ditolak
dari body, lalu mengulang permintaannya. Sisanya tetap masuk, dan begitu migrasinya
diterapkan payload penuh kembali terkirim dengan sendirinya — tanpa memasang ulang
add-in. Batasnya empat kolom, supaya database yang benar-benar salah tetap berhenti
sebagai kegagalan alih-alih terkelupas kolom demi kolom.

Yang dibuang **disebutkan** di log aktivitas, tidak dibuang diam-diam: fitur yang
tidak jalan tanpa satu pun petunjuk kenapa adalah kegagalan yang paling lama tidak
ketahuan. Logikanya ada di `PostgrestSchema` di Core — tidak menyentuh HTTP, jadi
bisa dites di runner Linux, dan justru perilaku ini yang perlu dites karena ia hanya
berjalan pada keadaan yang jarang ada di mesin orang yang menulis kodenya.

### Sisa pekerjaan dihitung dua kali, dengan sengaja

`unconnected_devices` memecah per denah, dan satu lampu bisa tampak di dua denah
sekaligus — denah lighting dan denah emergency. Menjumlahkan barisnya akan
menghitungnya dua kali, jadi angka di judul kartu datang dari `unconnected_total`
yang menghitung device, bukan kemunculannya. Dua angka yang berbeda di satu kartu
terlihat seperti kesalahan; yang sebenarnya salah adalah memakai satu angka untuk
dua pertanyaan yang berbeda.

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
