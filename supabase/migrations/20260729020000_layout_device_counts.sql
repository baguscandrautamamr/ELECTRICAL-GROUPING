-- CircuitSync — jumlah titik per denah dalam satu panggilan.
--
-- Halaman daftar layout menyebut berapa titik di tiap denah. Menghitungnya di web
-- berarti satu request per layout — model ini punya enam belas denah, dan halaman
-- itu menyegarkan diri secara berkala, jadi ongkosnya menumpuk tanpa alasan.
--
-- Menarik seluruh baris keanggotaan lalu menghitungnya di JavaScript bukan jalan
-- keluar: PostgREST memotong hasil di seribu baris tanpa memberi tahu, dan model
-- besar punya jauh lebih banyak dari itu. Yang benar adalah membiarkan Postgres
-- yang menghitung.
--
-- `security invoker` disengaja: fungsi ini berjalan sebagai pemanggilnya, jadi RLS
-- `layout_devices` tetap berlaku dan project orang lain tetap tidak terlihat.

create or replace function public.layout_device_counts(p_project uuid)
returns table (layout_unique_id text, devices bigint)
language sql
stable
security invoker
set search_path = public
as $$
  select ld.layout_unique_id, count(*)
  from public.layout_devices ld
  where ld.project_id = p_project
  group by ld.layout_unique_id;
$$;

comment on function public.layout_device_counts(uuid) is 'Jumlah device per layout untuk satu project, dihitung di database. Menggantikan satu request per layout dari web.';

revoke all on function public.layout_device_counts(uuid) from public;
grant execute on function public.layout_device_counts(uuid) to authenticated;

-- Menyusul hak yang terlewat untuk `layouts`. Tabel itu lahir setelah migrasi
-- pertama, sedangkan `grant ... on all tables` di sana hanya berlaku untuk tabel
-- yang sudah ada saat itu. Di Supabase tertutupi ALTER DEFAULT PRIVILEGES bawaan,
-- jadi tidak pernah terlihat; di Postgres kosong ia gagal sebagai "permission
-- denied for table layouts".
grant select, insert, update, delete on public.layouts to authenticated;
