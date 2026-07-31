-- CircuitSync — line style dari model, dan antrean menggambar garis wiring di Revit.
--
-- Dua hal yang saling membutuhkan, jadi satu migrasi.
--
-- 1. `line_styles` — subcategory dari kategori Revit `OST_Lines`, yang di Revit muncul
--    di dialog Line Styles. Web tidak boleh mengarang namanya: yang sah hanya yang ada
--    di model, karena add-in nanti harus menemukannya kembali untuk dipasang ke garis
--    yang digambar. Sebelum tabel ini ada, dropdown line style di web selalu kosong
--    dan dimatikan, padahal di model style-nya sudah dibuat.
--
-- 2. `direction = 'wiring'` — jalur kedua dari web ke add-in, di samping `apply`.
--    Isinya bukan rencana circuit melainkan daftar polyline siap gambar: titik-titiknya
--    sudah dihitung di web (`web/lib/wiring.ts`) dan dikirim apa adanya, jadi garis di
--    Revit bukan sekadar mirip pratinjau melainkan angka yang identik.
--
-- Kontrak kolom di sini harus sama persis dengan addin/src/CircuitSync.Core/Contract.cs
-- dan web/lib/contract.ts.

create table public.line_styles (
  project_id      uuid        not null references public.projects (id) on delete cascade,

  -- UniqueId GraphicsStyle-nya, bukan namanya. Nama line style bisa diubah user di
  -- Revit; yang dipakai add-in untuk menemukannya kembali harus yang tidak berubah.
  revit_unique_id text        not null,

  name            text        not null,
  sort_order      integer     not null default 0,
  updated_at      timestamptz not null default now(),
  primary key (project_id, revit_unique_id)
);

comment on table public.line_styles is 'Line style dari kategori Revit OST_Lines. Dipakai web sebagai pilihan gaya garis wiring; add-in memasangnya ke detail curve yang digambar.';
comment on column public.line_styles.revit_unique_id is 'UniqueId GraphicsStyle. Bukan namanya — nama bisa diubah user di Revit.';

-- Sama seperti tabel snapshot yang lain: sapuan add-in membandingkan updated_at
-- terhadap stempel awal snapshot, dan upsert tidak menggeser updated_at sendiri.
create trigger line_styles_touch before update on public.line_styles
  for each row execute function public.touch_updated_at();

grant select, insert, update, delete on public.line_styles to authenticated;

alter table public.line_styles enable row level security;

create policy line_styles_select on public.line_styles for select to authenticated
  using (public.is_project_member(project_id));

create policy line_styles_insert on public.line_styles for insert to authenticated
  with check (public.is_project_editor(project_id));

create policy line_styles_update on public.line_styles for update to authenticated
  using (public.is_project_editor(project_id))
  with check (public.is_project_editor(project_id));

create policy line_styles_delete on public.line_styles for delete to authenticated
  using (public.is_project_editor(project_id));

-- ---------------------------------------------------------------------------
-- sync_jobs: arah ketiga
-- ---------------------------------------------------------------------------

-- Constraint aslinya dibuat tanpa nama di migrasi pertama, jadi Postgres menamainya
-- sendiri `<tabel>_<kolom>_check`. Yang baru diberi nama eksplisit supaya perubahan
-- berikutnya tidak perlu menebak lagi.
alter table public.sync_jobs drop constraint if exists sync_jobs_direction_check;
alter table public.sync_jobs add constraint sync_jobs_direction_check
  check (direction in ('apply', 'snapshot', 'wiring'));

comment on table public.sync_jobs is 'direction=apply: web mengantre rencana circuit. direction=wiring: web mengantre polyline siap gambar. direction=snapshot: add-in mencatat tarikan model. Add-in hanya mengambil job queued milik project dokumen yang terbuka.';

-- Mengantre gambar garis. Tidak ada tabel circuit yang ikut berpindah status di sini —
-- garis wiring bukan circuit, dan sengaja tidak berpura-pura jadi circuit: ia tidak
-- punya panel, tidak punya nomor, dan tidak menyambungkan apa pun secara listrik.
--
-- Yang divalidasi di dalam fungsi, bukan di web: layout dan line style harus benar-benar
-- milik project ini. Web bisa saja mengirim id dari project lain — RLS menahan bacanya,
-- tapi tidak menahan penyebutannya di dalam payload jsonb.
create or replace function public.queue_wiring(
  p_project    uuid,
  p_layout     text,
  p_line_style text,
  p_runs       jsonb
)
returns uuid
language plpgsql
security invoker
set search_path = public
as $$
declare
  v_job uuid;
begin
  if p_runs is null or jsonb_typeof(p_runs) <> 'array' or jsonb_array_length(p_runs) = 0 then
    raise exception 'no_runs_to_queue';
  end if;

  if not exists (
    select 1 from public.layouts
     where project_id = p_project and revit_unique_id = p_layout
  ) then
    raise exception 'layout_not_found';
  end if;

  if not exists (
    select 1 from public.line_styles
     where project_id = p_project and revit_unique_id = p_line_style
  ) then
    raise exception 'line_style_not_found';
  end if;

  insert into public.sync_jobs (project_id, direction, payload, created_by)
  values (
    p_project,
    'wiring',
    jsonb_build_object(
      'layout_unique_id', p_layout,
      'line_style_unique_id', p_line_style,
      'runs', p_runs
    ),
    auth.uid()
  )
  returning id into v_job;

  return v_job;
end;
$$;
