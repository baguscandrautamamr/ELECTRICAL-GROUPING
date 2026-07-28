-- Tiruan minimal bagian Supabase yang dipakai migrasi, supaya migrasi bisa
-- diuji di Postgres kosong (CI, mesin lokal) tanpa project Supabase.
--
--   psql -f supabase/tests/harness.sql -f supabase/migrations/*.sql -f supabase/tests/smoke.sql
--
-- Ini BUKAN bagian dari database production. `supabase db push` hanya mengirim
-- isi supabase/migrations.

create schema if not exists auth;

create table if not exists auth.users (
  id    uuid primary key default gen_random_uuid(),
  email text
);

-- Supabase menyuntikkan klaim JWT lewat GUC request.jwt.claims. Untuk test,
-- cukup baca GUC "request.jwt.claim.sub" yang bisa kita set manual.
create or replace function auth.uid()
returns uuid
language sql
stable
as $$
  select nullif(current_setting('request.jwt.claim.sub', true), '')::uuid;
$$;

do $$
begin
  if not exists (select 1 from pg_roles where rolname = 'anon') then
    create role anon nologin;
  end if;
  if not exists (select 1 from pg_roles where rolname = 'authenticated') then
    create role authenticated nologin;
  end if;
  if not exists (select 1 from pg_roles where rolname = 'service_role') then
    create role service_role nologin bypassrls;
  end if;
end;
$$;

-- Di Supabase asli, ketiga role ini sudah punya akses ke helper auth.
grant usage on schema auth to anon, authenticated, service_role;
grant execute on function auth.uid() to anon, authenticated, service_role;
