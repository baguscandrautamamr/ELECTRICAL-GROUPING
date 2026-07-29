-- CircuitSync — device apa yang benar-benar tampak di sebuah layout.
--
-- Sebelum ini web memilih device sebuah layout lewat pasangan (level_key, kind)
-- milik layout itu. Di model sungguhan satu lantai punya lebih dari satu denah
-- lighting — "D_FG WAREHOUSE GROUND FLOOR - LIGHTING SYSTEM LAYOUT PLAN" dan
-- "D_FG WAREHOUSE GROUND FLOOR - EMERGENCY & EXIT LIGHTING SYSTEM LAYOUT PLAN" —
-- dan keduanya berlantai sama, berjenis sama. Akibatnya kedua halaman di web
-- menampilkan device yang persis sama, termasuk lampu emergency dan exit yang di
-- Revit justru disembunyikan dari denah lighting biasa.
--
-- Yang menentukan isi sebuah denah bukan lantai dan jenisnya, melainkan view
-- Revit itu sendiri: filter view, visibility per kategori, crop region, dan fase.
-- Add-in membacanya lewat FilteredElementCollector yang dibatasi view, lalu
-- menyimpan hasilnya di sini. Web tinggal mengikuti.
--
-- Kontrak kolom di sini harus sama persis dengan addin/src/CircuitSync.Core/Contract.cs
-- dan web/lib/contract.ts.

create table public.layout_devices (
  project_id       uuid        not null references public.projects (id) on delete cascade,
  layout_unique_id text        not null,
  device_unique_id text        not null,
  updated_at       timestamptz not null default now(),

  primary key (project_id, layout_unique_id, device_unique_id),

  -- Cascade dua arah: layout atau device yang tersapu snapshot ikut membawa
  -- keanggotaannya. Tanpa ini sapuan meninggalkan baris yatim yang menunjuk view
  -- atau device yang sudah tidak ada di model.
  foreign key (project_id, layout_unique_id)
    references public.layouts (project_id, revit_unique_id) on delete cascade,
  foreign key (project_id, device_unique_id)
    references public.devices (project_id, revit_unique_id) on delete cascade
);

create index layout_devices_layout_idx
  on public.layout_devices (project_id, layout_unique_id);

comment on table public.layout_devices is 'Device yang benar-benar tampak di sebuah view denah Revit. Ditentukan view — bukan pasangan (level, kind) — supaya denah lighting dan denah emergency/exit di lantai yang sama tidak menampilkan isi yang sama.';

-- Sama seperti tabel snapshot yang lain: sapuan add-in membandingkan updated_at
-- terhadap stempel awal snapshot, dan upsert tidak menggeser updated_at sendiri.
create trigger layout_devices_touch before update on public.layout_devices
  for each row execute function public.touch_updated_at();

alter table public.layout_devices enable row level security;

create policy layout_devices_select on public.layout_devices for select to authenticated
  using (public.is_project_member(project_id));

create policy layout_devices_insert on public.layout_devices for insert to authenticated
  with check (public.is_project_editor(project_id));

create policy layout_devices_update on public.layout_devices for update to authenticated
  using (public.is_project_editor(project_id))
  with check (public.is_project_editor(project_id));

create policy layout_devices_delete on public.layout_devices for delete to authenticated
  using (public.is_project_editor(project_id));
