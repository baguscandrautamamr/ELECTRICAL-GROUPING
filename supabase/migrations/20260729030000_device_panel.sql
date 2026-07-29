-- CircuitSync — panel mana yang memuat sebuah device, dan apa saja isinya.
--
-- Sebelum ini web hanya bisa menjawab "isi panel ini apa" dari tabel `circuits`,
-- yaitu circuit yang pernah dibuat lewat web. Model sungguhan hampir selalu sudah
-- punya circuit yang dibuat langsung di Revit, jauh sebelum project ini ada, dan
-- circuit seperti itu tidak punya baris di `circuits` sama sekali. Akibatnya panel
-- yang di Revit penuh tampak kosong di web — jawaban yang bukan sekadar kurang,
-- tapi menyesatkan.
--
-- Yang tahu jawabannya adalah model: `ElectricalSystem.PanelName` menyebut panel
-- pemuat tiap circuit. Add-in membacanya bersama status koneksi — keduanya dari
-- pembacaan yang sama — lalu menyimpannya di sini.
--
-- Kontrak kolom di sini harus sama persis dengan addin/src/CircuitSync.Core/Contract.cs
-- dan web/lib/contract.ts.

alter table public.devices add column panel_unique_id text;

comment on column public.devices.panel_unique_id is 'UniqueId panel pemuat device ini, dibaca add-in dari ElectricalSystem.PanelName. Null berarti device belum tersambung ke panel mana pun.';

-- Tanpa foreign key ke `panels` dengan sengaja. Snapshot mengunggah tabelnya satu
-- per satu, jadi urutan unggah akan menentukan apakah baris device diterima —
-- device yang menunjuk panel yang barisnya belum sempat masuk akan ditolak, dan
-- seluruh tarikan model gagal karena alasan yang tidak ada hubungannya dengan data.
-- Panel yang tidak ketemu di web ditampilkan apa adanya, bukan menjatuhkan unggahan.
create index devices_panel_idx on public.devices (project_id, panel_unique_id)
  where panel_unique_id is not null;

-- ---------------------------------------------------------------- isi panel

-- Isi tiap panel, sudah terhitung per (circuit, jenis, family).
--
-- Dihitung di database, bukan dengan menarik seluruh baris device ke web: model ini
-- punya ribuan titik dan PostgREST memotong hasil di seribu baris tanpa memberi tahu.
-- Yang keluar dari sini paling banyak beberapa ratus baris ringkasan.
--
-- `security invoker` disengaja: fungsi berjalan sebagai pemanggilnya, jadi RLS
-- `devices` dan `circuits` tetap berlaku dan project orang lain tetap tidak terlihat.
create or replace function public.panel_contents(p_project uuid)
returns table (
  panel_unique_id text,
  circuit_number  text,
  kind            text,
  family_key      text,
  devices         bigint
)
language sql
stable
security invoker
set search_path = public
as $$
  with linked as (
    -- Sumber utama: panel yang dibaca add-in langsung dari model.
    select
      d.panel_unique_id as panel_id,
      d.circuit_number  as number,
      d.kind            as device_kind,
      d.family_key      as family,
      d.revit_unique_id as device_id
    from public.devices d
    where d.project_id = p_project
      and d.panel_unique_id is not null

    union all

    -- Cadangan untuk model yang terakhir ditarik add-in versi lama: circuit yang
    -- pernah diterapkan lewat web sudah tahu panelnya sendiri. Dibatasi ke circuit
    -- berstatus `applied` dan device berstatus `connected` — usulan yang belum
    -- diterapkan bukan isi panel, melainkan rencana, dan tempatnya di halaman denah.
    select
      c.panel_unique_id,
      coalesce(d.circuit_number, c.circuit_number),
      d.kind,
      d.family_key,
      d.revit_unique_id
    from public.circuits c
    join public.devices d
      on d.project_id = c.project_id
     and d.revit_unique_id = any (c.device_unique_ids)
    where c.project_id = p_project
      and c.status = 'applied'
      and d.status = 'connected'
      and d.panel_unique_id is null
  )
  select
    l.panel_id,
    l.number,
    l.device_kind,
    l.family,
    count(distinct l.device_id)
  from linked l
  group by l.panel_id, l.number, l.device_kind, l.family;
$$;

comment on function public.panel_contents(uuid) is 'Isi tiap panel per (circuit, jenis, family). Sumbernya panel yang dibaca add-in dari model; circuit yang pernah diterapkan lewat web dipakai sebagai cadangan untuk model yang belum ditarik ulang.';

revoke all on function public.panel_contents(uuid) from public;
grant execute on function public.panel_contents(uuid) to authenticated;

-- ------------------------------------------------------- yang belum tersambung

-- Device yang belum tersambung ke panel, dipecah per denah, jenis, dan family.
--
-- `no_connector` sengaja tidak dihitung: device itu tidak akan pernah bisa masuk
-- circuit, jadi memasukkannya ke sisa pekerjaan membuat angkanya tidak pernah nol.
-- `no_panel` ikut dihitung — circuit-nya ada, tapi tanpa panel ia belum memberi
-- daya kepada apa pun.
--
-- `left join` supaya device yang tidak tampak di denah mana pun tetap terhitung,
-- dengan `layout_unique_id` null. Itu justru yang paling mudah hilang dari pandangan.
create or replace function public.unconnected_devices(p_project uuid)
returns table (
  layout_unique_id text,
  kind             text,
  status           text,
  family_key       text,
  devices          bigint
)
language sql
stable
security invoker
set search_path = public
as $$
  select
    ld.layout_unique_id,
    d.kind,
    d.status,
    d.family_key,
    count(distinct d.revit_unique_id)
  from public.devices d
  left join public.layout_devices ld
    on ld.project_id = d.project_id
   and ld.device_unique_id = d.revit_unique_id
  where d.project_id = p_project
    and d.status in ('unwired', 'no_panel')
  group by ld.layout_unique_id, d.kind, d.status, d.family_key;
$$;

comment on function public.unconnected_devices(uuid) is 'Device yang belum tersambung ke panel, per denah, jenis, dan family. Device yang tidak tampak di denah mana pun keluar dengan layout_unique_id null.';

revoke all on function public.unconnected_devices(uuid) from public;
grant execute on function public.unconnected_devices(uuid) to authenticated;

-- Berapa device yang belum tersambung, dihitung sekali.
--
-- Bukan penjumlahan baris `unconnected_devices`: satu lampu bisa tampak di dua denah
-- sekaligus — denah lighting dan denah emergency — dan menjumlahkan barisnya akan
-- menghitungnya dua kali. Angka di judul harus menyebut jumlah device, bukan jumlah
-- kemunculannya di denah.
create or replace function public.unconnected_total(p_project uuid)
returns bigint
language sql
stable
security invoker
set search_path = public
as $$
  select count(*)
  from public.devices d
  where d.project_id = p_project
    and d.status in ('unwired', 'no_panel');
$$;

comment on function public.unconnected_total(uuid) is 'Jumlah device yang belum tersambung ke panel. Dihitung sekali per device, bukan per kemunculan di denah.';

revoke all on function public.unconnected_total(uuid) from public;
grant execute on function public.unconnected_total(uuid) to authenticated;
