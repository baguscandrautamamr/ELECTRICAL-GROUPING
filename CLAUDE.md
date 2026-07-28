# ELECTRICAL-GROUPING — monorepo

Dua bagian yang dipasangkan, dalam satu repo:

| Folder | Isi | Panduan |
| --- | --- | --- |
| `addin/` | Add-in Revit 2025 (CircuitSync) | [`addin/CLAUDE.md`](addin/CLAUDE.md) |
| `web/` | Next.js app, deploy di Vercel | [`web/CLAUDE.md`](web/CLAUDE.md) |
| `supabase/` | Migrasi Postgres + RLS, dipakai keduanya | — |
| `assets/` | Sumber ikon, generator raster | — |

Kedua panduan itu berlaku penuh di foldernya masing-masing. Baca yang relevan
sebelum menyentuh kode di folder itu.

## Kontrak data adalah satu sumber, dua cermin

Karena add-in dan web ada di satu repo, kontrak data (`device`, `panel`,
`circuit`, `sync_job`) **wajib diubah dalam satu commit** di tiga tempat:

1. `supabase/migrations/` — bentuk tabelnya
2. `addin/src/CircuitSync.Core/Contract.cs` — sisi C#
3. `web/lib/contract.ts` — sisi TypeScript

Kalau salah satu ketinggalan, round-trip diam-diam rusak: PostgREST menolak
kolom yang tidak dikenal tanpa pesan yang jelas.

## Perintah

```bash
# add-in
cd addin && dotnet build && dotnet test

# web
cd web && pnpm install && pnpm build && pnpm lint && pnpm typecheck

# ikon (setelah mengubah assets/icon.svg)
python assets/generate-icons.py
```

## Catatan

Keputusan teknis yang tidak jelas dari kode — kenapa WPF tanpa XAML, kenapa
paket Revit API dari NuGet, apa yang masih manual — dicatat di
[`CATATAN.md`](CATATAN.md). Tambahkan ke situ, jangan ke komentar kode.
