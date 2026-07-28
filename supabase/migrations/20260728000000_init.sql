-- CircuitSync — skema awal.
--
-- Satu file, karena ini migrasi pertama: semua tabel lahir bersama policy RLS-nya.
-- Kontrak kolom di sini harus sama persis dengan addin/src/CircuitSync.Core/Contract.cs
-- dan web/lib/contract.ts.

create extension if not exists "pgcrypto";

-- ---------------------------------------------------------------------------
-- profiles
-- ---------------------------------------------------------------------------

create table public.profiles (
  id          uuid primary key references auth.users (id) on delete cascade,
  email       text        not null,
  display_name text,
  locale      text        not null default 'id' check (locale in ('id', 'en')),
  created_at  timestamptz not null default now()
);

comment on table public.profiles is 'Data user yang boleh dibaca anggota project lain. Bukan tempat menyimpan rahasia.';

create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  insert into public.profiles (id, email, display_name)
  values (new.id, new.email, split_part(coalesce(new.email, ''), '@', 1))
  on conflict (id) do nothing;
  return new;
end;
$$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_user();

-- ---------------------------------------------------------------------------
-- projects + keanggotaan
-- ---------------------------------------------------------------------------

create table public.projects (
  id          uuid primary key default gen_random_uuid(),
  name        text        not null check (length(btrim(name)) > 0),
  owner_id    uuid        not null references auth.users (id) on delete cascade,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);

create table public.project_members (
  project_id  uuid        not null references public.projects (id) on delete cascade,
  user_id     uuid        not null references auth.users (id) on delete cascade,
  role        text        not null default 'editor' check (role in ('owner', 'editor', 'viewer')),
  created_at  timestamptz not null default now(),
  primary key (project_id, user_id)
);

create index project_members_user_idx on public.project_members (user_id);

-- Dipakai semua policy di bawah. SECURITY DEFINER supaya membaca
-- project_members dari dalam policy project_members sendiri tidak rekursif.
create or replace function public.is_project_member(p_project uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
  select exists (
    select 1 from public.project_members m
    where m.project_id = p_project and m.user_id = auth.uid()
  );
$$;

create or replace function public.is_project_editor(p_project uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
  select exists (
    select 1 from public.project_members m
    where m.project_id = p_project
      and m.user_id = auth.uid()
      and m.role in ('owner', 'editor')
  );
$$;

create or replace function public.is_project_owner(p_project uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
  select exists (
    select 1 from public.project_members m
    where m.project_id = p_project and m.user_id = auth.uid() and m.role = 'owner'
  );
$$;

-- Pembuat project langsung jadi owner. Tanpa ini, dia tidak bisa membaca
-- project yang baru saja dia buat.
create or replace function public.handle_new_project()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  insert into public.project_members (project_id, user_id, role)
  values (new.id, new.owner_id, 'owner')
  on conflict (project_id, user_id) do update set role = 'owner';
  return new;
end;
$$;

create trigger on_project_created
  after insert on public.projects
  for each row execute function public.handle_new_project();

create or replace function public.touch_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

create trigger projects_touch before update on public.projects
  for each row execute function public.touch_updated_at();

-- ---------------------------------------------------------------------------
-- snapshot model: levels, panels, devices
-- ---------------------------------------------------------------------------

create table public.levels (
  project_id   uuid        not null references public.projects (id) on delete cascade,
  level_key    text        not null,
  name         text        not null,
  elevation_mm double precision not null default 0,
  sort_order   integer     not null default 0,
  updated_at   timestamptz not null default now(),
  primary key (project_id, level_key)
);

create table public.panels (
  project_id          uuid        not null references public.projects (id) on delete cascade,
  revit_unique_id     text        not null,
  name                text        not null,
  prefix              text,
  distribution_system text,
  voltage             text,
  phase               integer,
  slots_total         integer,
  slots_used          integer,
  is_usable           boolean     not null default false,
  updated_at          timestamptz not null default now(),
  primary key (project_id, revit_unique_id)
);

comment on column public.panels.is_usable is 'False biasanya berarti distribution system belum diisi di Revit. Web menampilkannya terpisah beserta alasannya, tidak menyembunyikannya.';

create table public.devices (
  project_id      uuid        not null references public.projects (id) on delete cascade,
  revit_unique_id text        not null,
  kind            text        not null check (kind in ('lighting', 'receptacle')),
  level_key       text        not null,
  room_name       text,
  family_key      text        not null,
  x_mm            double precision not null,
  y_mm            double precision not null,
  va              double precision,
  status          text        not null default 'unwired'
                    check (status in ('unwired', 'no_panel', 'connected', 'no_connector')),
  circuit_number  text,
  updated_at      timestamptz not null default now(),
  primary key (project_id, revit_unique_id)
);

create index devices_plan_idx on public.devices (project_id, level_key, kind);
create index devices_family_idx on public.devices (project_id, family_key);

comment on column public.devices.circuit_number is 'Selalu berasal dari Revit. Web tidak pernah mengisi kolom ini.';

-- Simbol 2D per family, override manual dari web. Default-nya dihitung
-- deterministik dari family_key di klien, jadi baris hanya ada kalau di-override.
create table public.symbol_overrides (
  project_id uuid        not null references public.projects (id) on delete cascade,
  family_key text        not null,
  symbol     text        not null,
  updated_at timestamptz not null default now(),
  primary key (project_id, family_key)
);

-- Add-in menyapu baris yang sudah tidak ada di model dengan membandingkan
-- updated_at terhadap stempel awal snapshot. Tanpa trigger ini, upsert tidak
-- menggeser updated_at dan sapuan akan menghapus baris yang masih hidup.
create trigger levels_touch before update on public.levels
  for each row execute function public.touch_updated_at();
create trigger panels_touch before update on public.panels
  for each row execute function public.touch_updated_at();
create trigger devices_touch before update on public.devices
  for each row execute function public.touch_updated_at();
create trigger symbol_overrides_touch before update on public.symbol_overrides
  for each row execute function public.touch_updated_at();

-- ---------------------------------------------------------------------------
-- circuits
-- ---------------------------------------------------------------------------

create table public.circuits (
  id                 uuid        primary key default gen_random_uuid(),
  project_id         uuid        not null references public.projects (id) on delete cascade,
  panel_unique_id    text        not null,
  kind               text        not null check (kind in ('lighting', 'receptacle')),
  device_unique_ids  text[]      not null default '{}',
  circuit_number     text,
  status             text        not null default 'draft'
                       check (status in ('draft', 'queued', 'applied', 'failed')),
  revit_unique_id    text,
  error              text,
  created_at         timestamptz not null default now(),
  updated_at         timestamptz not null default now()
);

create index circuits_project_idx on public.circuits (project_id, status);

create trigger circuits_touch before update on public.circuits
  for each row execute function public.touch_updated_at();

comment on column public.circuits.circuit_number is 'Nomor asli dari Revit, misal "(LC)1". Hanya add-in yang menulisnya.';

-- ---------------------------------------------------------------------------
-- sync_jobs — satu-satunya jembatan web -> Revit
-- ---------------------------------------------------------------------------

create table public.sync_jobs (
  id          uuid        primary key default gen_random_uuid(),
  project_id  uuid        not null references public.projects (id) on delete cascade,
  direction   text        not null check (direction in ('apply', 'snapshot')),
  status      text        not null default 'queued' check (status in ('queued', 'applied', 'failed')),
  payload     jsonb       not null default '{}'::jsonb,
  error       text,
  created_by  uuid        references auth.users (id) on delete set null,
  created_at  timestamptz not null default now(),
  applied_at  timestamptz
);

create index sync_jobs_poll_idx on public.sync_jobs (project_id, status, created_at);

comment on table public.sync_jobs is 'direction=apply: web mengantre rencana untuk add-in. direction=snapshot: add-in mencatat tarikan model. Add-in hanya mengambil apply + queued milik project dokumen yang terbuka.';

-- Mengantre satu rencana sebagai satu transaksi: circuit pindah ke queued dan
-- job dibuat bersama, supaya tidak ada job yang menunjuk circuit yang masih draft.
create or replace function public.queue_apply(p_project uuid, p_circuit_ids uuid[])
returns uuid
language plpgsql
security invoker
set search_path = public
as $$
declare
  v_job uuid;
  v_count integer;
begin
  update public.circuits
     set status = 'queued', error = null
   where project_id = p_project
     and id = any (p_circuit_ids)
     and status in ('draft', 'failed');

  get diagnostics v_count = row_count;
  if v_count = 0 then
    raise exception 'no_circuits_to_queue';
  end if;

  insert into public.sync_jobs (project_id, direction, payload, created_by)
  values (
    p_project,
    'apply',
    jsonb_build_object('circuit_ids', to_jsonb(p_circuit_ids)),
    auth.uid()
  )
  returning id into v_job;

  return v_job;
end;
$$;

-- ---------------------------------------------------------------------------
-- RLS
-- ---------------------------------------------------------------------------

alter table public.profiles         enable row level security;
alter table public.projects         enable row level security;
alter table public.project_members  enable row level security;
alter table public.levels           enable row level security;
alter table public.panels           enable row level security;
alter table public.devices          enable row level security;
alter table public.symbol_overrides enable row level security;
alter table public.circuits         enable row level security;
alter table public.sync_jobs        enable row level security;

-- profiles: dirinya sendiri, plus anggota project yang sama.
create policy profiles_select on public.profiles for select to authenticated
  using (
    id = auth.uid()
    or exists (
      select 1
        from public.project_members mine
        join public.project_members theirs on theirs.project_id = mine.project_id
       where mine.user_id = auth.uid() and theirs.user_id = profiles.id
    )
  );

create policy profiles_update on public.profiles for update to authenticated
  using (id = auth.uid()) with check (id = auth.uid());

-- projects
create policy projects_select on public.projects for select to authenticated
  using (public.is_project_member(id));

create policy projects_insert on public.projects for insert to authenticated
  with check (owner_id = auth.uid());

create policy projects_update on public.projects for update to authenticated
  using (public.is_project_editor(id)) with check (public.is_project_editor(id));

create policy projects_delete on public.projects for delete to authenticated
  using (public.is_project_owner(id));

-- project_members
create policy project_members_select on public.project_members for select to authenticated
  using (user_id = auth.uid() or public.is_project_member(project_id));

create policy project_members_insert on public.project_members for insert to authenticated
  with check (public.is_project_owner(project_id));

create policy project_members_update on public.project_members for update to authenticated
  using (public.is_project_owner(project_id)) with check (public.is_project_owner(project_id));

create policy project_members_delete on public.project_members for delete to authenticated
  using (public.is_project_owner(project_id) or user_id = auth.uid());

-- Tabel isi model: baca untuk semua anggota, tulis untuk editor.
do $$
declare
  t text;
begin
  foreach t in array array['levels', 'panels', 'devices', 'symbol_overrides', 'circuits', 'sync_jobs']
  loop
    execute format(
      'create policy %1$s_select on public.%1$s for select to authenticated
         using (public.is_project_member(project_id))', t);
    execute format(
      'create policy %1$s_insert on public.%1$s for insert to authenticated
         with check (public.is_project_editor(project_id))', t);
    execute format(
      'create policy %1$s_update on public.%1$s for update to authenticated
         using (public.is_project_editor(project_id))
         with check (public.is_project_editor(project_id))', t);
    execute format(
      'create policy %1$s_delete on public.%1$s for delete to authenticated
         using (public.is_project_editor(project_id))', t);
  end loop;
end;
$$;

-- ---------------------------------------------------------------------------
-- grants
-- ---------------------------------------------------------------------------

grant usage on schema public to authenticated;
grant select, insert, update, delete on all tables in schema public to authenticated;
grant execute on function public.queue_apply(uuid, uuid[]) to authenticated;
grant execute on function public.is_project_member(uuid) to authenticated;
grant execute on function public.is_project_editor(uuid) to authenticated;
grant execute on function public.is_project_owner(uuid) to authenticated;

-- anon tidak punya akses apa pun ke data project. Login dulu.
revoke all on all tables in schema public from anon;
