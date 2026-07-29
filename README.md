<p align="center">
  <img src="assets/icon.svg" width="72" height="72" alt="">
</p>

<h1 align="center">CircuitSync</h1>

<p align="center">
  Kelompokkan device kelistrikan Revit jadi circuit di browser,<br>
  lalu terapkan hasilnya kembali ke model.
</p>

---

Engineer melihat lampu dan receptacle dari model Revit sebagai denah 2D di web,
memilihnya dengan tarikan mouse, memasangkannya ke panel, lalu mengantre hasilnya.
Add-in di Revit mengambil antrean itu dan mengerjakannya: membuat circuit,
menyambung ke panel, menempatkan tag nomor circuit.

Website tidak pernah menulis langsung ke Revit, dan tidak pernah mengarang nomor
circuit. Nomor seperti `(LC)1` selalu datang dari Revit.

## Isi repo

| Folder | Isi |
| --- | --- |
| `addin/` | Add-in Revit 2025, C# `net8.0-windows` |
| `web/` | Next.js App Router, deploy di Vercel |
| `supabase/` | Migrasi Postgres beserta policy RLS, plus uji RLS |
| `assets/` | Ikon sumber dan generatornya |

Panduan per bagian ada di `addin/CLAUDE.md` dan `web/CLAUDE.md`.
Catatan keputusan teknis dan langkah yang masih manual ada di
[`CATATAN.md`](CATATAN.md) — **baca itu dulu sebelum deploy.**

## Kemampuan

- **Dua bahasa** — Indonesia dan English, di web maupun di add-in
- **Dua tema** — terang sebagai default, gelap wajib bekerja
- **Login email** — tautan masuk atau kata sandi di web, kode enam angka di add-in
- **Per project** — satu project satu model Revit, dipisah RLS di database
- **Auto build** — GitHub Actions membangun ZIP add-in di setiap push

## Mulai

### Database

```bash
supabase link --project-ref <ref>
supabase db push
```

Migrasi bisa diuji tanpa Supabase, di Postgres kosong:

```bash
createdb cs_test
psql -d cs_test -v ON_ERROR_STOP=1 \
  -f supabase/tests/harness.sql \
  -f supabase/migrations/20260728000000_init.sql \
  -f supabase/tests/smoke.sql
```

### Web

```bash
cd web
pnpm install
pnpm dev            # http://localhost:3000
pnpm build && pnpm lint && pnpm typecheck
```

### Add-in

```bash
cd addin
dotnet build
dotnet test tests/CircuitSync.Core.Tests
```

Build ini jalan di Linux dan Windows. Memasangnya ke Revit — lihat
[`addin/installer/INSTALL.md`](addin/installer/INSTALL.md).

## Kontrak data

Empat bentuk ini harus sama persis di tiga tempat: migrasi Postgres,
`addin/src/CircuitSync.Core/Contract.cs`, dan `web/lib/contract.ts`.

```
device:   revit_unique_id, kind, level_key, room_name, family_key,
          x_mm, y_mm, va, status, circuit_number, panel_unique_id
panel:    revit_unique_id, name, prefix, distribution_system,
          voltage, phase, slots_total, slots_used, is_usable
circuit:  id, panel_unique_id, kind, device_unique_ids[], circuit_number, status
sync_job: id, project_id, direction, status, payload, error, applied_at
```

Ada test di sisi C# yang membandingkan nama kolom hasil serialisasi dengan daftar
di atas, jadi ketidakcocokan gagal saat `dotnet test`, bukan saat sinkronisasi
diam-diam berhenti bekerja.

## Status device

| Status | Arti | Tampilan |
| --- | --- | --- |
| `unwired` | Belum di-circuit | Merah, garis putus-putus |
| `no_panel` | Circuit ada, panel kosong | Kuning, titik di tengah |
| `connected` | Circuit dan panel terisi | Hijau, isian penuh |
| `no_connector` | Tidak ada connector listrik | Abu, garis miring, tidak bisa dipilih |

Bentuk simbol dibedakan per family. Warna saja tidak dipakai sebagai satu-satunya
pembeda, supaya denah tetap terbaca oleh pengguna dengan buta warna dan saat
dicetak hitam putih.
