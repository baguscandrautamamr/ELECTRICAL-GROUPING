-- CircuitSync — project yang disembunyikan dari daftar, per user.
--
-- Ini **bukan** penghapusan. Tidak ada satu pun baris model yang hilang: device, panel,
-- circuit, layout, dan garis wiring tetap utuh di database, dan model Revit-nya tidak
-- disentuh sama sekali. Yang disimpan di sini cuma satu keputusan tampilan — "jangan
-- tampilkan project ini di daftar saya".
--
-- Menghapus sungguhan adalah pilihan yang salah untuk daftar yang penuh. Project di sini
-- mewakili satu model Revit, dan model itu hidup di luar jangkauan web: menghapus barisnya
-- berarti membuang riwayat circuit dan wiring yang tidak bisa dibuat ulang dari model,
-- sementara tarikan berikutnya dari add-in akan membuat project itu muncul lagi sebagai
-- project baru yang kosong. Yang tampak seperti "hapus" akhirnya jadi "hapus isinya".
--
-- Per user, bukan per project. Menyembunyikan adalah preferensi tampilan, dan preferensi
-- satu orang tidak boleh menghilangkan project dari layar rekan satu timnya — kalau
-- disimpan di tabel projects, satu klik owner membuat editor kehilangan halaman kerjanya
-- tanpa sebab yang bisa dilihat.
--
-- Kontrak kolom di sini harus sama persis dengan addin/src/CircuitSync.Core/Contract.cs
-- dan web/lib/contract.ts.

create table public.project_hidden (
  project_id uuid        not null references public.projects (id) on delete cascade,
  user_id    uuid        not null references auth.users (id) on delete cascade,
  hidden_at  timestamptz not null default now(),

  primary key (project_id, user_id)
);

comment on table public.project_hidden is 'Project yang disembunyikan dari daftar, per user. Bukan penghapusan: isi model tetap utuh, dan barisnya dibuang lagi begitu add-in mengirim tarikan model berikutnya.';
comment on column public.project_hidden.hidden_at is 'Kapan disembunyikan. Dipakai untuk menjelaskan keadaan di layar, bukan untuk menyaring.';

grant select, insert, update, delete on public.project_hidden to authenticated;

alter table public.project_hidden enable row level security;

-- Semua policy bertumpu pada user_id = auth.uid(): baris ini milik satu orang, dan tidak
-- ada seorang pun yang berkepentingan membaca atau mengubah pilihan tampilan orang lain.
create policy project_hidden_select on public.project_hidden for select to authenticated
  using (user_id = auth.uid());

-- Keanggotaan tetap diperiksa saat menyisipkan. Tanpa itu, siapa pun bisa menumpuk baris
-- untuk project yang tidak pernah dilihatnya.
create policy project_hidden_insert on public.project_hidden for insert to authenticated
  with check (user_id = auth.uid() and public.is_project_member(project_id));

create policy project_hidden_update on public.project_hidden for update to authenticated
  using (user_id = auth.uid())
  with check (user_id = auth.uid());

create policy project_hidden_delete on public.project_hidden for delete to authenticated
  using (user_id = auth.uid());

-- ---------------------------------------------------------------------------
-- Tarikan model berikutnya membatalkan penyembunyian
-- ---------------------------------------------------------------------------

-- Add-in mencatat setiap tarikan model sebagai satu baris sync_jobs berarah 'snapshot'.
-- Itu sinyal yang sudah ada, dan tepat maknanya: model ini baru saja dikirim lagi dari
-- Revit, jadi project-nya sedang dikerjakan dan pantas muncul kembali di daftar.
--
-- Karena sinyalnya sudah ada, add-in tidak perlu diubah sama sekali untuk fitur ini.
--
-- Hanya 'snapshot' yang membatalkan. Arah 'apply' dan 'wiring' berjalan dari web ke Revit
-- dan bisa datang dari project yang memang sengaja disembunyikan; yang menandakan "model
-- ini hidup lagi" cuma kiriman dari sisi Revit.
create or replace function public.unhide_on_snapshot()
returns trigger
language plpgsql
-- security definer: baris yang dibuang di sini milik user lain, dan policy di atas sengaja
-- mengurung setiap orang pada barisnya sendiri. Tanpa ini, tarikan model hanya akan
-- memunculkan kembali project bagi orang yang kebetulan menjalankan add-in.
security definer
set search_path = public
as $$
begin
  if new.direction = 'snapshot' then
    delete from public.project_hidden where project_id = new.project_id;
  end if;

  return new;
end;
$$;

create trigger sync_jobs_unhide
  after insert on public.sync_jobs
  for each row execute function public.unhide_on_snapshot();
