-- CircuitSync — lighting device (saklar dan sensor).
--
-- Kategori Revit `OST_LightingDevices`, terpisah dari `OST_LightingFixtures` yang
-- berisi lampunya. Ini yang menjawab pertanyaan "ruangan ini dibagi berapa grouping":
-- dua saklar berarti dua grouping, berapa pun rapatnya lampu.
--
-- Sebelum ada tabel ini, batas grouping disimpulkan dari kerapatan lampu saja, dengan
-- ambang 2,5 kali jarak antar lampu. Dua ruangan yang dipisah dinding tipis berjarak
-- sekitar 1,5 sampai 2 kali jarak lampu, jadi keduanya dilebur jadi satu dan garis
-- wiring menyeberang dinding. Menaikkan ambangnya hanya memindahkan salah tebak ke
-- ukuran ruangan yang lain — yang dibutuhkan data, bukan tebakan yang lebih baik.
--
-- Kontrak kolom di sini harus sama persis dengan addin/src/CircuitSync.Core/Contract.cs
-- dan web/lib/contract.ts.

create table public.lighting_devices (
  project_id      uuid        not null references public.projects (id) on delete cascade,
  revit_unique_id text        not null,
  family_key      text        not null,
  level_key       text        not null,

  -- Ruangan menurut Revit. Sering kosong, sama seperti pada devices: Revit hanya bisa
  -- menjawabnya kalau family punya Room Calculation Point.
  room_name       text,

  x_mm            double precision not null,
  y_mm            double precision not null,
  updated_at      timestamptz not null default now(),
  primary key (project_id, revit_unique_id)
);

create index lighting_devices_level_idx on public.lighting_devices (project_id, level_key);

comment on table public.lighting_devices is 'Saklar dan sensor dari kategori Revit OST_LightingDevices. Jumlahnya per ruangan menentukan jadi berapa grouping lampu ruangan itu dipecah.';
comment on column public.lighting_devices.room_name is 'Sering null; Revit hanya menjawabnya kalau family punya Room Calculation Point.';

-- Sama seperti tabel snapshot yang lain: sapuan add-in membandingkan updated_at
-- terhadap stempel awal snapshot, dan upsert tidak menggeser updated_at sendiri.
-- Tanpa trigger ini, sapuan akan menghapus saklar yang masih ada di model.
create trigger lighting_devices_touch before update on public.lighting_devices
  for each row execute function public.touch_updated_at();

alter table public.lighting_devices enable row level security;

create policy lighting_devices_select on public.lighting_devices for select to authenticated
  using (public.is_project_member(project_id));

create policy lighting_devices_insert on public.lighting_devices for insert to authenticated
  with check (public.is_project_editor(project_id));

create policy lighting_devices_update on public.lighting_devices for update to authenticated
  using (public.is_project_editor(project_id))
  with check (public.is_project_editor(project_id));

create policy lighting_devices_delete on public.lighting_devices for delete to authenticated
  using (public.is_project_editor(project_id));

-- `grant ... on all tables` di migrasi pertama hanya berlaku untuk tabel yang sudah
-- ada saat itu, dan tidak ada default privilege yang menyusulkannya. Tanpa baris ini
-- policy-nya benar tapi tabelnya tetap ditolak — dan pesannya "permission denied",
-- yang tidak menyebut-nyebut RLS sama sekali.
grant select, insert, update, delete on public.lighting_devices to authenticated;
