-- Uji perilaku RLS: dua user, satu project, pastikan yang bukan anggota tidak
-- melihat apa pun. Dijalankan setelah harness.sql + semua migrasi.
-- Gagal = exception, jadi psql dengan ON_ERROR_STOP akan berhenti.

\set ON_ERROR_STOP on

\set uid_a '''aaaaaaaa-0000-4000-8000-000000000001'''
\set uid_b '''bbbbbbbb-0000-4000-8000-000000000002'''
\set pid   '''cccccccc-0000-4000-8000-000000000003'''

insert into auth.users (id, email) values (:uid_a, 'a@example.com'), (:uid_b, 'b@example.com');

do $$
begin
  assert (select count(*) from public.profiles) = 2, 'trigger profil tidak jalan';
end;
$$;

-- ---------------------------------------------------------------- user A
set role authenticated;
set request.jwt.claim.sub = 'aaaaaaaa-0000-4000-8000-000000000001';

insert into public.projects (id, name, owner_id) values (:pid, 'Gedung A', :uid_a);

do $$
begin
  assert (select count(*) from public.projects) = 1, 'A tidak melihat project miliknya';
  assert (select role from public.project_members
           where project_id = 'cccccccc-0000-4000-8000-000000000003') = 'owner',
         'pembuat project tidak jadi owner';
end;
$$;

insert into public.levels (project_id, level_key, name, elevation_mm, sort_order)
values (:pid, 'L1', 'Lantai 1', 0, 0);

insert into public.panels (project_id, revit_unique_id, name, prefix, distribution_system,
                           voltage, phase, slots_total, slots_used, is_usable)
values (:pid, 'panel-uid-1', 'LP-1', '(LC)', '380/220V 3ph', '220 V', 3, 24, 0, true),
       (:pid, 'panel-uid-2', 'LP-2', '(LC)', null, '220 V', 3, 12, 0, false);

insert into public.devices (project_id, revit_unique_id, kind, level_key, room_name,
                            family_key, x_mm, y_mm, va, status)
values (:pid, 'dev-1', 'lighting', 'L1', 'Ruang Rapat', 'Downlight::18W', 1000, 2000, 18, 'unwired'),
       (:pid, 'dev-2', 'lighting', 'L1', 'Ruang Rapat', 'Downlight::18W', 1600, 2000, 18, 'unwired'),
       (:pid, 'dev-3', 'receptacle', 'L1', 'Ruang Rapat', 'Outlet::Duplex', 2200, 2000, 180, 'unwired');

insert into public.circuits (id, project_id, panel_unique_id, kind, device_unique_ids)
values ('dddddddd-0000-4000-8000-000000000004', :pid, 'panel-uid-1', 'lighting',
        array['dev-1', 'dev-2']);

-- queue_apply memindahkan circuit ke queued dan membuat satu job apply.
do $$
declare
  v_job uuid;
begin
  v_job := public.queue_apply(
    'cccccccc-0000-4000-8000-000000000003',
    array['dddddddd-0000-4000-8000-000000000004']::uuid[]);

  assert v_job is not null, 'queue_apply tidak mengembalikan job';
  assert (select status from public.circuits
           where id = 'dddddddd-0000-4000-8000-000000000004') = 'queued',
         'circuit tidak pindah ke queued';
  assert (select count(*) from public.sync_jobs
           where direction = 'apply' and status = 'queued') = 1,
         'job apply tidak dibuat';
end;
$$;

-- Circuit yang sudah queued tidak boleh diantre ulang.
do $$
declare
  v_failed boolean := false;
begin
  begin
    perform public.queue_apply(
      'cccccccc-0000-4000-8000-000000000003',
      array['dddddddd-0000-4000-8000-000000000004']::uuid[]);
  exception when others then
    v_failed := true;
  end;
  assert v_failed, 'queue_apply menerima circuit yang sudah queued';
end;
$$;

-- Nomor circuit hanya boleh dari Revit; DB tidak memaksa itu, tapi statusnya iya.
do $$
declare
  v_failed boolean := false;
begin
  begin
    insert into public.devices (project_id, revit_unique_id, kind, level_key, family_key, x_mm, y_mm, status)
    values ('cccccccc-0000-4000-8000-000000000003', 'dev-x', 'lighting', 'L1', 'X::Y', 0, 0, 'ngawur');
  exception when check_violation then
    v_failed := true;
  end;
  assert v_failed, 'status device di luar daftar diterima';
end;
$$;

-- ------------------------------------------------ isi panel dan sisa pekerjaan
--
-- Dua jalur harus sampai ke jawaban yang sama. Jalur utama: panel yang dibaca
-- add-in langsung dari model, tersimpan di `devices.panel_unique_id`. Jalur
-- cadangan: circuit yang pernah diterapkan lewat web, dipakai selama model belum
-- ditarik ulang add-in versi ini.

-- Jalur utama — dua lampu tersambung ke LP-1 lewat (LC)1.
update public.devices
   set status = 'connected', panel_unique_id = 'panel-uid-1', circuit_number = '(LC)1'
 where project_id = :pid and revit_unique_id in ('dev-1', 'dev-2');

-- Jalur cadangan — receptacle yang tersambung di model, tapi panelnya hanya
-- diketahui web lewat circuit yang sudah diterapkan.
insert into public.devices (project_id, revit_unique_id, kind, level_key, room_name,
                            family_key, x_mm, y_mm, va, status)
values (:pid, 'dev-4', 'receptacle', 'L1', 'Ruang Rapat', 'Outlet::Duplex', 2800, 2000, 180, 'connected');

insert into public.circuits (id, project_id, panel_unique_id, kind, device_unique_ids,
                             status, circuit_number)
values ('eeeeeeee-0000-4000-8000-000000000005', :pid, 'panel-uid-1', 'receptacle',
        array['dev-4'], 'applied', '(LC)3');

do $$
declare
  v_lighting bigint;
  v_receptacle bigint;
begin
  select devices into v_lighting from public.panel_contents(
    'cccccccc-0000-4000-8000-000000000003')
   where panel_unique_id = 'panel-uid-1' and circuit_number = '(LC)1';

  select devices into v_receptacle from public.panel_contents(
    'cccccccc-0000-4000-8000-000000000003')
   where panel_unique_id = 'panel-uid-1' and circuit_number = '(LC)3';

  assert v_lighting = 2, 'isi panel dari model tidak terbaca';
  assert v_receptacle = 1, 'isi panel dari circuit yang sudah diterapkan tidak terbaca';

  -- Circuit yang masih usulan bukan isi panel. dev-3 belum tersambung ke mana pun,
  -- jadi tidak boleh muncul hanya karena ada baris circuit yang menyebutnya.
  assert (select coalesce(sum(devices), 0) from public.panel_contents(
           'cccccccc-0000-4000-8000-000000000003')) = 3,
         'panel memuat device yang belum diterapkan';
end;
$$;

do $$
begin
  -- Tersisa dev-3, satu-satunya yang masih unwired.
  assert public.unconnected_total('cccccccc-0000-4000-8000-000000000003') = 1,
         'sisa pekerjaan salah hitung';

  assert (select devices from public.unconnected_devices(
           'cccccccc-0000-4000-8000-000000000003')
           where kind = 'receptacle' and status = 'unwired') = 1,
         'device belum tersambung tidak terpecah per jenis';

  -- Belum ada baris layout_devices, jadi dev-3 tidak tampak di denah mana pun —
  -- dan justru itu yang harus tetap terhitung, bukan hilang lewat join.
  assert (select count(*) from public.unconnected_devices(
           'cccccccc-0000-4000-8000-000000000003')
           where layout_unique_id is null) = 1,
         'device di luar denah hilang dari sisa pekerjaan';
end;
$$;

-- Saklar: penentu sebuah ruangan dipecah jadi berapa grouping.
insert into public.lighting_devices (project_id, revit_unique_id, family_key, level_key,
                                     room_name, x_mm, y_mm)
values (:pid, 'sw-1', 'Switch::1 Gang', 'L1', 'Ruang Rapat', 0, 2000),
       (:pid, 'sw-2', 'Switch::2 Gang', 'L1', 'Ruang Rapat', 5000, 2000);

do $$
declare
  v_before timestamptz;
  v_after  timestamptz;
begin
  assert (select count(*) from public.lighting_devices) = 2, 'A tidak melihat saklar miliknya';

  -- Trigger updated_at wajib jalan: sapuan snapshot membandingkan stempelnya, dan tanpa
  -- trigger ini upsert tidak menggesernya sehingga saklar yang masih hidup ikut terhapus.
  select updated_at into v_before from public.lighting_devices where revit_unique_id = 'sw-1';
  perform pg_sleep(0.01);
  update public.lighting_devices set x_mm = 100 where revit_unique_id = 'sw-1';
  select updated_at into v_after from public.lighting_devices where revit_unique_id = 'sw-1';
  assert v_after > v_before, 'trigger lighting_devices_touch tidak menggeser updated_at';
end;
$$;

-- ------------------------------------------------ layout, line style, antre wiring
--
-- Saklar dibatasi per layout, bukan per lantai. Satu lantai bisa punya denah lighting
-- dan denah emergency/exit sekaligus; keduanya `level_key` sama, jadi tanpa tabel
-- keanggotaan ini kedua halaman menerima seluruh saklar lantai itu.

insert into public.layouts (project_id, revit_unique_id, name, kind, level_key, scale, sort_order)
values (:pid, 'view-lighting', 'L1 - LIGHTING PLAN', 'lighting', 'L1', 100, 0),
       (:pid, 'view-emergency', 'L1 - EMERGENCY & EXIT PLAN', 'lighting', 'L1', 100, 1);

-- sw-1 hanya tampak di denah lighting biasa. Denah emergency tidak boleh kebagian.
insert into public.layout_lighting_devices (project_id, layout_unique_id, lighting_device_unique_id)
values (:pid, 'view-lighting', 'sw-1'),
       (:pid, 'view-lighting', 'sw-2');

insert into public.line_styles (project_id, revit_unique_id, name, sort_order)
values (:pid, 'gs-lighting', 'LIGHTING', 0),
       (:pid, 'gs-receptacle', 'RECEPTACLE', 1);

do $$
begin
  assert (select count(*) from public.layout_lighting_devices
           where layout_unique_id = 'view-lighting') = 2,
         'keanggotaan saklar di denah lighting tidak terbaca';
  assert (select count(*) from public.layout_lighting_devices
           where layout_unique_id = 'view-emergency') = 0,
         'saklar denah lighting ikut jatuh ke denah emergency';
end;
$$;

-- Cascade: saklar yang tersapu snapshot harus membawa keanggotaannya.
do $$
begin
  delete from public.lighting_devices
   where project_id = 'cccccccc-0000-4000-8000-000000000003' and revit_unique_id = 'sw-2';

  assert (select count(*) from public.layout_lighting_devices
           where lighting_device_unique_id = 'sw-2') = 0,
         'keanggotaan yatim tertinggal setelah saklarnya dihapus';
end;
$$;

-- queue_wiring: satu job wiring, tanpa menyentuh circuit mana pun.
do $$
declare
  v_job uuid;
  v_runs jsonb := jsonb_build_array(
    jsonb_build_object('switch_index', 0, 'vertices', jsonb_build_array(
      jsonb_build_object('x_mm', 1000, 'y_mm', 2000),
      jsonb_build_object('x_mm', 1600, 'y_mm', 2000))));
begin
  v_job := public.queue_wiring(
    'cccccccc-0000-4000-8000-000000000003', 'view-lighting', 'gs-lighting', v_runs);

  assert v_job is not null, 'queue_wiring tidak mengembalikan job';
  assert (select count(*) from public.sync_jobs
           where direction = 'wiring' and status = 'queued') = 1,
         'job wiring tidak dibuat';
  assert (select payload -> 'line_style_unique_id' from public.sync_jobs where id = v_job)
           = '"gs-lighting"'::jsonb,
         'line style tidak ikut di payload';

  -- Garis wiring bukan circuit: statusnya tidak boleh ikut berpindah.
  assert (select count(*) from public.circuits where status = 'queued') = 1,
         'queue_wiring menyentuh status circuit';
end;
$$;

-- Tiga penolakan: tanpa garis, layout asing, line style asing. Payload jsonb tidak
-- dijaga foreign key, jadi validasinya harus hidup di dalam fungsi.
do $$
declare
  v_runs jsonb := jsonb_build_array(
    jsonb_build_object('switch_index', 0, 'vertices', jsonb_build_array(
      jsonb_build_object('x_mm', 0, 'y_mm', 0))));
  v_failed boolean;
begin
  v_failed := false;
  begin
    perform public.queue_wiring('cccccccc-0000-4000-8000-000000000003',
      'view-lighting', 'gs-lighting', '[]'::jsonb);
  exception when others then v_failed := true;
  end;
  assert v_failed, 'queue_wiring menerima daftar garis kosong';

  v_failed := false;
  begin
    perform public.queue_wiring('cccccccc-0000-4000-8000-000000000003',
      'view-entah', 'gs-lighting', v_runs);
  exception when others then v_failed := true;
  end;
  assert v_failed, 'queue_wiring menerima layout yang bukan milik project';

  v_failed := false;
  begin
    perform public.queue_wiring('cccccccc-0000-4000-8000-000000000003',
      'view-lighting', 'gs-entah', v_runs);
  exception when others then v_failed := true;
  end;
  assert v_failed, 'queue_wiring menerima line style yang bukan milik project';
end;
$$;

-- ---------------------------------------------------------------- user B
set request.jwt.claim.sub = 'bbbbbbbb-0000-4000-8000-000000000002';

do $$
begin
  assert (select count(*) from public.projects) = 0, 'B melihat project orang lain';
  assert (select count(*) from public.devices) = 0, 'B melihat device orang lain';
  assert (select count(*) from public.panels) = 0, 'B melihat panel orang lain';
  assert (select count(*) from public.circuits) = 0, 'B melihat circuit orang lain';
  assert (select count(*) from public.sync_jobs) = 0, 'B melihat job orang lain';
  assert (select count(*) from public.profiles) = 1, 'B melihat profil di luar projectnya';

  -- Fungsi ringkasan berjalan sebagai pemanggilnya, jadi RLS tabel di dalamnya tetap
  -- berlaku. Kalau salah satunya pernah dibuat `security definer`, di sinilah bocornya
  -- ketahuan — bukan di production.
  assert (select count(*) from public.panel_contents(
           'cccccccc-0000-4000-8000-000000000003')) = 0,
         'B melihat isi panel project orang lain';
  assert (select count(*) from public.unconnected_devices(
           'cccccccc-0000-4000-8000-000000000003')) = 0,
         'B melihat sisa pekerjaan project orang lain';
  assert public.unconnected_total('cccccccc-0000-4000-8000-000000000003') = 0,
         'B melihat jumlah sisa pekerjaan project orang lain';
end;
$$;

do $$
declare
  v_failed boolean := false;
begin
  begin
    insert into public.devices (project_id, revit_unique_id, kind, level_key, family_key, x_mm, y_mm)
    values ('cccccccc-0000-4000-8000-000000000003', 'dev-b', 'lighting', 'L1', 'X::Y', 0, 0);
  exception when insufficient_privilege then
    v_failed := true;
  end;
  assert v_failed, 'B bisa menulis device ke project orang lain';

  -- Saklar bocor berarti pembagian grouping ikut bocor.
  assert (select count(*) from public.lighting_devices) = 0, 'B melihat saklar project orang lain';
  assert (select count(*) from public.layout_lighting_devices) = 0,
         'B melihat keanggotaan saklar project orang lain';
  assert (select count(*) from public.line_styles) = 0, 'B melihat line style project orang lain';
end;
$$;

-- queue_wiring berjalan sebagai pemanggilnya. B bukan anggota, jadi layout-nya tidak
-- terlihat dan fungsinya harus menolak — bukan menyisipkan job ke project orang lain.
do $$
declare
  v_failed boolean := false;
begin
  begin
    perform public.queue_wiring('cccccccc-0000-4000-8000-000000000003',
      'view-lighting', 'gs-lighting',
      jsonb_build_array(jsonb_build_object('switch_index', 0, 'vertices',
        jsonb_build_array(jsonb_build_object('x_mm', 0, 'y_mm', 0)))));
  exception when others then v_failed := true;
  end;
  assert v_failed, 'B bisa mengantre gambar garis ke project orang lain';
end;
$$;

-- B diundang sebagai viewer: boleh baca, tidak boleh tulis.
reset role;
insert into public.project_members (project_id, user_id, role) values (:pid, :uid_b, 'viewer');
set role authenticated;

do $$
declare
  v_failed boolean := false;
begin
  assert (select count(*) from public.devices) = 4, 'viewer tidak bisa baca device';
  begin
    update public.devices set room_name = 'diubah viewer'
     where project_id = 'cccccccc-0000-4000-8000-000000000003';
    v_failed := (select count(*) from public.devices where room_name = 'diubah viewer') = 0;
  exception when insufficient_privilege then
    v_failed := true;
  end;
  assert v_failed, 'viewer bisa mengubah device';
end;
$$;

-- ---------------------------------------------------------------- anon
reset role;

do $$
begin
  assert not has_table_privilege('anon', 'public.projects', 'select'), 'anon masih bisa baca projects';
  assert not has_table_privilege('anon', 'public.devices', 'select'), 'anon masih bisa baca devices';
  assert (select count(*) from pg_tables t
           where t.schemaname = 'public'
             and not exists (select 1 from pg_class c
                              where c.relname = t.tablename
                                and c.relnamespace = 'public'::regnamespace
                                and c.relrowsecurity)) = 0,
         'ada tabel public tanpa RLS';
end;
$$;

\echo 'smoke test: lolos'
