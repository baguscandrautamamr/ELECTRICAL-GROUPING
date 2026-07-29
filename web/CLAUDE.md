# CircuitSync — web

Web app tempat engineer melihat device kelistrikan dari model Revit sebagai
denah 2D, mengelompokkannya jadi circuit, lalu mengantre hasilnya untuk
diterapkan kembali ke Revit.

Repo pasangannya: `revit-circuit-addin`. Kontrak data di bawah harus identik di
kedua repo — kalau berubah di sini, ubah juga di sana pada commit yang sama.

## Stack

- Next.js App Router, TypeScript, deploy di Vercel
- Supabase — Postgres, Auth, RLS. Klien lewat `@supabase/ssr`
- Tailwind, `lucide-react`, `next-themes`, `next-intl`

## Struktur

```
app/
  [locale]/
    (auth)/login/
    setup/                                         diagnosa penyiapan, tanpa login
    (app)/projects/
    (app)/projects/[projectId]/layouts/[layoutId]/ halaman denah
components/
  plan/          kanvas SVG, marker device, seleksi, system browser
  circuits/      daftar circuit, panel picker
lib/
  supabase/      client browser dan server, config, klasifikasi error query
  contract.ts    tipe bersama, cerminan repo add-in
messages/
  id.json  en.json
supabase/
  migrations/
```

Halaman denah dikunci satu **layout** — cerminan view denah Revit, yang sudah
memuat lantai dan jenis sekaligus beserta crop region dan skalanya. Kuncinya
`revit_unique_id` view, jadi tautan tetap sah antar tarikan model. Jangan simpan
pilihan itu hanya di state React.

## Aturan keras

**1. Website tidak pernah menulis langsung ke Revit.** Tombol "kirim ke Revit"
hanya menyisipkan baris ke tabel `sync_jobs` dengan status `queued`. Add-in yang
akan mengambilnya lewat polling. Jangan pernah menulis copy UI yang menjanjikan
perubahan instan — lihat bagian penulisan di bawah.

**2. Jangan pernah menghasilkan nomor circuit.** Nomor seperti `(LC)1` dibuat
Revit, bukan kita. Sebelum diterapkan, tampilkan sebagai usulan tanpa nomor.
Nomor asli baru muncul setelah add-in menulisnya balik.

**3. `service_role` key tidak pernah masuk kode klien.** Kalau butuh operasi
lintas user, buat Route Handler di server dan tetap pakai session user. Kalau
merasa perlu bypass RLS, berhenti dan tanya — biasanya itu tanda policy-nya yang
salah.

**4. RLS aktif di semua tabel sejak migrasi pertama.** Jangan pernah membuat
tabel baru tanpa policy di migrasi yang sama.

**5. Tidak ada string bahasa Inggris atau Indonesia yang di-hardcode di JSX.**
Semua lewat `next-intl`, kunci ditambahkan ke `id.json` dan `en.json` sekaligus.
Ini murah kalau dilakukan sejak awal dan menyakitkan kalau ditunda.

**6. Terang adalah default, gelap wajib bekerja.** `defaultTheme="light"`. Semua
warna lewat token Tailwind yang punya varian gelap — tidak ada hex mentah di
komponen. Cek setiap layar baru di kedua mode sebelum bilang selesai.

**7. Jangan tambah library kanvas.** SVG di React sudah cukup sampai sekitar 2000
titik per lantai. Kalau sudah terasa berat, ukur dulu, baru diskusikan.

**8. Query yang gagal tidak boleh berakhir jadi layar kosong.** `data ?? []` di
atas `error` yang tidak diperiksa membuat database yang belum dimigrasi tampil
persis seperti akun baru. Periksa `error` lewat `lib/supabase/errors.ts` dan
tampilkan `<SetupNeeded>`, bukan ajakan bertindak yang tidak akan jalan.

## Kontrak data

Cerminan dari repo add-in. Definisikan sekali di `lib/contract.ts`, jangan
duplikasi tipenya di komponen.

```
device:   revit_unique_id, kind, level_key, room_name, family_key,
          x_mm, y_mm, va, status, circuit_number
panel:    revit_unique_id, name, prefix, distribution_system,
          voltage, phase, slots_total, slots_used, is_usable
circuit:  id, panel_unique_id, kind, device_unique_ids[], circuit_number, status
sync_job: id, project_id, direction, status, payload, error, applied_at
```

`family_key` = `"{FamilyName}::{TypeName}"`, kunci untuk memilih simbol 2D.

## Aturan tampilan

Status device menentukan warna marker. Warna saja tidak cukup — setiap status
juga punya bentuk atau garis yang berbeda, supaya terbaca oleh pengguna dengan
buta warna dan saat dicetak hitam putih.

| Status | Arti | Warna |
| --- | --- | --- |
| `unwired` | belum di-circuit | merah |
| `no_panel` | circuit ada, panel kosong | kuning |
| `connected` | circuit dan panel terisi | hijau |
| `no_connector` | tidak ada connector listrik | abu, tidak bisa dipilih |

Simbol 2D dibedakan per `family_key`. Default-nya dihasilkan deterministik dari
hash nama family supaya stabil antar sesi; user bisa menimpanya, dan override
disimpan per project di database.

Panel dengan `is_usable` false tidak boleh muncul di dropdown pemilihan. Tampilkan
terpisah beserta alasannya — biasanya distribution system belum diisi di Revit.
Menyembunyikannya tanpa penjelasan bikin user bingung mencari panelnya.

## Penulisan antarmuka

Sentence case, kalimat aktif, sebut yang dikendalikan user bukan cara sistem
bekerja. Nama aksi konsisten dari tombol sampai notifikasi: tombol "Kirim ke
Revit" menghasilkan pesan "Terkirim ke antrean", bukan "Berhasil dikirim!".

Error menjelaskan apa yang terjadi dan apa yang bisa dilakukan, tanpa minta maaf
dan tanpa menyalin pesan exception mentah. Layar kosong adalah ajakan bertindak,
bukan permintaan maaf — "Belum ada circuit di lantai ini. Pilih beberapa titik
untuk mulai." bukan "Tidak ada data."

Hindari kata "berhasil", "silakan", dan tanda seru di pesan sistem.

## Perintah

```bash
pnpm dev
pnpm build           # wajib lolos sebelum bilang selesai
pnpm lint
pnpm typecheck
supabase db push
```

## Selesai artinya

`pnpm build`, `lint`, dan `typecheck` bersih. Layar baru sudah dicek di mode
terang dan gelap serta di kedua bahasa. Tabel baru punya policy RLS di migrasi
yang sama. Tidak ada string yang belum masuk file terjemahan.

## Fase saat ini

**Fase 0 — belum mulai.** Web app baru dikerjakan setelah round-trip di sisi
Revit terbukti jalan. Kalau diminta mengerjakan sesuatu di repo ini sekarang,
konfirmasi dulu apakah fase Revit sudah selesai.
