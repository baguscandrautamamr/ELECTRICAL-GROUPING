-- CircuitSync — saklar apa yang benar-benar tampak di sebuah layout.
--
-- `layout_devices` sudah membuat isi denah ditentukan view Revit, bukan pasangan
-- (level, kind). Saklarnya tertinggal: web masih menyaring `lighting_devices`
-- dengan `level_key`, padahal justru saklar yang menentukan sebuah ruangan dipecah
-- jadi berapa grouping.
--
-- Akibatnya persis kebalikan dari yang diperbaiki `layout_devices`. Satu lantai
-- dengan dua denah lighting — "GROUND FLOOR - LIGHTING SYSTEM LAYOUT PLAN" dan
-- "GROUND FLOOR - EMERGENCY & EXIT LIGHTING SYSTEM LAYOUT PLAN" — punya `level_key`
-- yang sama, jadi kedua halaman menerima **seluruh** saklar lantai itu. Tiap saklar
-- lalu diberikan ke kumpulan lampu terdekat yang ada di halaman itu, sehingga denah
-- emergency dipecah oleh saklar yang sebetulnya mengendalikan lampu biasa, dan
-- sebaliknya. Gejalanya cuma jumlah grouping yang salah — tidak ada peringatan,
-- karena datanya memang ada, hanya bukan milik denah itu.
--
-- Tabel ini tidak bisa digabung ke `layout_devices`: foreign key di sana menunjuk
-- `public.devices`, sedangkan saklar hidup di `public.lighting_devices`.
--
-- Kontrak kolom di sini harus sama persis dengan addin/src/CircuitSync.Core/Contract.cs
-- dan web/lib/contract.ts.

create table public.layout_lighting_devices (
  project_id                uuid        not null references public.projects (id) on delete cascade,
  layout_unique_id          text        not null,
  lighting_device_unique_id text        not null,
  updated_at                timestamptz not null default now(),

  primary key (project_id, layout_unique_id, lighting_device_unique_id),

  -- Cascade dua arah, sama seperti layout_devices: layout atau saklar yang tersapu
  -- snapshot ikut membawa keanggotaannya, jadi sapuan tidak meninggalkan baris yatim.
  foreign key (project_id, layout_unique_id)
    references public.layouts (project_id, revit_unique_id) on delete cascade,
  foreign key (project_id, lighting_device_unique_id)
    references public.lighting_devices (project_id, revit_unique_id) on delete cascade
);

create index layout_lighting_devices_layout_idx
  on public.layout_lighting_devices (project_id, layout_unique_id);

comment on table public.layout_lighting_devices is 'Saklar yang benar-benar tampak di sebuah view denah Revit. Tanpa ini kedua denah lighting di satu lantai menerima seluruh saklar lantai itu, dan jumlah grouping di keduanya salah.';

-- Sama seperti tabel snapshot yang lain: sapuan add-in membandingkan updated_at
-- terhadap stempel awal snapshot, dan upsert tidak menggeser updated_at sendiri.
create trigger layout_lighting_devices_touch before update on public.layout_lighting_devices
  for each row execute function public.touch_updated_at();

-- `grant ... on all tables` di migrasi pertama hanya berlaku untuk tabel yang sudah
-- ada saat itu. Di Supabase kelalaian ini tertutupi ALTER DEFAULT PRIVILEGES bawaan,
-- jadi bedanya baru muncul di Postgres kosong — termasuk yang dipakai CI — sebagai
-- "permission denied for table" yang tidak ada hubungannya dengan RLS.
grant select, insert, update, delete on public.layout_lighting_devices to authenticated;

alter table public.layout_lighting_devices enable row level security;

create policy layout_lighting_devices_select on public.layout_lighting_devices for select to authenticated
  using (public.is_project_member(project_id));

create policy layout_lighting_devices_insert on public.layout_lighting_devices for insert to authenticated
  with check (public.is_project_editor(project_id));

create policy layout_lighting_devices_update on public.layout_lighting_devices for update to authenticated
  using (public.is_project_editor(project_id))
  with check (public.is_project_editor(project_id));

create policy layout_lighting_devices_delete on public.layout_lighting_devices for delete to authenticated
  using (public.is_project_editor(project_id));
