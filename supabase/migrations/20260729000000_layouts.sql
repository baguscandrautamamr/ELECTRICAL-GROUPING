-- CircuitSync — layout denah.
--
-- Sebelum ini halaman kerja di web dikunci oleh pasangan (level_key, kind), dan
-- nama yang muncul di layar adalah nama level. Di model sungguhan, engineer
-- bekerja per *view denah* Revit — "D_FG WAREHOUSE GROUND FLOOR - LIGHTING
-- SYSTEM LAYOUT PLAN" — yang sudah memuat lantai dan jenis sekaligus, punya crop
-- region sendiri, dan punya skala yang menentukan bentuk cetaknya.
--
-- Layout adalah cerminan view itu. Add-in hanya mengirim view yang namanya
-- menyebut lighting atau socket outlet; sisanya (fire alarm, CCTV, grounding,
-- dan seterusnya) tidak ada urusannya dengan pengelompokan circuit.
--
-- Kontrak kolom di sini harus sama persis dengan addin/src/CircuitSync.Core/Contract.cs
-- dan web/lib/contract.ts.

create table public.layouts (
  project_id      uuid        not null references public.projects (id) on delete cascade,
  revit_unique_id text        not null,
  name            text        not null,
  kind            text        not null check (kind in ('lighting', 'receptacle')),
  level_key       text        not null,

  -- Skala view Revit sebagai penyebut: 1:100 disimpan sebagai 100. Dipakai web
  -- untuk menyebut skala apa adanya, bukan untuk menghitung ukuran piksel.
  scale           integer,

  -- Crop region view dalam milimeter model. Null berarti crop tidak aktif di
  -- Revit; web jatuh ke kotak pembatas device seperti sebelumnya.
  crop_min_x_mm   double precision,
  crop_min_y_mm   double precision,
  crop_max_x_mm   double precision,
  crop_max_y_mm   double precision,

  sort_order      integer     not null default 0,
  updated_at      timestamptz not null default now(),
  primary key (project_id, revit_unique_id)
);

create index layouts_project_idx on public.layouts (project_id, sort_order);

comment on table public.layouts is 'Cerminan view denah Revit yang dipakai untuk pengelompokan circuit. Hanya view yang namanya menyebut lighting atau socket outlet yang dikirim add-in.';
comment on column public.layouts.scale is 'Penyebut skala Revit: 1:100 disimpan 100.';
comment on column public.layouts.crop_min_x_mm is 'Crop region dalam milimeter model. Null berarti crop tidak aktif di view Revit.';

-- Sama seperti tabel snapshot yang lain: sapuan add-in membandingkan updated_at
-- terhadap stempel awal snapshot, dan upsert tidak menggeser updated_at sendiri.
-- Tanpa trigger ini, sapuan akan menghapus layout yang masih ada di model.
create trigger layouts_touch before update on public.layouts
  for each row execute function public.touch_updated_at();

alter table public.layouts enable row level security;

create policy layouts_select on public.layouts for select to authenticated
  using (public.is_project_member(project_id));

create policy layouts_insert on public.layouts for insert to authenticated
  with check (public.is_project_editor(project_id));

create policy layouts_update on public.layouts for update to authenticated
  using (public.is_project_editor(project_id))
  with check (public.is_project_editor(project_id));

create policy layouts_delete on public.layouts for delete to authenticated
  using (public.is_project_editor(project_id));
