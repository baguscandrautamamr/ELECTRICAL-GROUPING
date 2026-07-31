-- CircuitSync — garis wiring yang sedang ada di model, per denah.
--
-- Tanpa tabel ini pengiriman garis hanya bisa menambah. Kiriman kedua untuk denah yang
-- sama meninggalkan garis lama di model dan menumpuk garis baru di atasnya — dan itu
-- keadaan yang biasa, bukan jarang: begitu ada electrical device yang berubah, web
-- menghitung wiring baru dan user mengirimnya lagi.
--
-- Yang dibutuhkan add-in adalah jawaban atas satu pertanyaan: garis mana di denah ini
-- yang dibuat CircuitSync? Menebaknya dari model tidak aman. Menghapus semua detail curve
-- di view akan ikut membuang garis yang digambar user sendiri; menghapus berdasarkan line
-- style pun sama, karena style itu dipakai user juga. Jadi yang dipakai catatan, bukan
-- tebakan: satu baris per garis, ditulis add-in setelah menggambar.
--
-- Konsekuensinya pengiriman garis jadi **pengganti**, bukan tambahan — bentuk yang sama
-- dengan snapshot model: web menyebut wiring denah ini seharusnya begini, add-in membuat
-- model cocok dengan itu.
--
-- Kontrak kolom di sini harus sama persis dengan addin/src/CircuitSync.Core/Contract.cs
-- dan web/lib/contract.ts.

create table public.wiring_curves (
  project_id       uuid        not null references public.projects (id) on delete cascade,
  layout_unique_id text        not null,

  -- UniqueId detail curve di Revit. Add-in memakainya untuk menemukan lalu menghapus
  -- garis itu sebelum menggambar yang baru.
  revit_unique_id  text        not null,

  -- Kaki saklar keberapa. Tidak dipakai untuk menghapus; ada supaya web bisa menyebut
  -- garis mana yang sedang hidup di model tanpa menebak dari warnanya.
  switch_index     integer     not null default 0,

  updated_at       timestamptz not null default now(),

  -- Satu garis milik satu denah, jadi UniqueId-nya sendiri sudah cukup jadi kunci.
  primary key (project_id, revit_unique_id),

  foreign key (project_id, layout_unique_id)
    references public.layouts (project_id, revit_unique_id) on delete cascade
);

create index wiring_curves_layout_idx on public.wiring_curves (project_id, layout_unique_id);

comment on table public.wiring_curves is 'Detail curve wiring yang sedang ada di model, per denah. Dipakai add-in untuk menghapus garis lama sebelum menggambar kiriman baru, supaya garis tidak menumpuk.';
comment on column public.wiring_curves.revit_unique_id is 'UniqueId detail curve di Revit, bukan id garis di web.';

-- Sama seperti tabel snapshot yang lain, dan dengan alasan yang sama.
create trigger wiring_curves_touch before update on public.wiring_curves
  for each row execute function public.touch_updated_at();

grant select, insert, update, delete on public.wiring_curves to authenticated;

alter table public.wiring_curves enable row level security;

create policy wiring_curves_select on public.wiring_curves for select to authenticated
  using (public.is_project_member(project_id));

create policy wiring_curves_insert on public.wiring_curves for insert to authenticated
  with check (public.is_project_editor(project_id));

create policy wiring_curves_update on public.wiring_curves for update to authenticated
  using (public.is_project_editor(project_id))
  with check (public.is_project_editor(project_id));

create policy wiring_curves_delete on public.wiring_curves for delete to authenticated
  using (public.is_project_editor(project_id));
