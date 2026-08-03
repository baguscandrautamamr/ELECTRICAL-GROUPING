'use server';

import {revalidatePath} from 'next/cache';
import {classifyError} from '@/lib/supabase/errors';
import {createClient} from '@/lib/supabase/server';

export type ActionResult = {ok: true; name?: string} | {ok: false; reason: 'name' | 'schema' | 'failed'};

/**
 * Membuat project. Trigger di database yang menjadikan pembuatnya owner, jadi tidak
 * ada langkah kedua di sini yang bisa gagal setengah jalan.
 */
export async function createProject(name: string): Promise<ActionResult> {
  const trimmed = name.trim();
  if (trimmed.length === 0) return {ok: false, reason: 'name'};

  const supabase = await createClient();
  const {
    data: {user}
  } = await supabase.auth.getUser();

  if (!user) return {ok: false, reason: 'failed'};

  const {error} = await supabase.from('projects').insert({name: trimmed, owner_id: user.id});
  // "Gagal" tanpa sebab menyembunyikan kasus paling umum: tabelnya memang belum ada.
  if (error) return {ok: false, reason: classifyError(error) === 'schema' ? 'schema' : 'failed'};

  revalidatePath('/[locale]/(app)/projects', 'page');
  return {ok: true, name: trimmed};
}

export type DeleteResult =
  | {ok: true; name: string}
  | {ok: false; reason: 'forbidden' | 'gone' | 'schema' | 'failed'};

/**
 * Menghapus satu project beserta seluruh isinya.
 *
 * Tidak ada penghapusan bertahap di sini: setiap tabel turunan menunjuk
 * `projects (id) on delete cascade`, jadi device, panel, circuit, layout, saklar,
 * line style, garis wiring, dan antrean job-nya ikut lepas dalam satu perintah.
 * Menghapusnya satu per satu dari sini justru bisa berhenti setengah jalan dan
 * meninggalkan project yang isinya tinggal separuh.
 */
export async function deleteProject(id: string): Promise<DeleteResult> {
  const supabase = await createClient();
  const {
    data: {user}
  } = await supabase.auth.getUser();

  if (!user) return {ok: false, reason: 'failed'};

  // Dibaca dulu supaya dua kegagalan yang bentuknya sama bisa dibedakan di layar —
  // lihat komentar di bawah.
  const {data: existing, error: readError} = await supabase
    .from('projects')
    .select('name')
    .eq('id', id)
    .maybeSingle();

  if (readError) return {ok: false, reason: classifyError(readError) === 'schema' ? 'schema' : 'failed'};
  if (!existing) return {ok: false, reason: 'gone'};

  const {data, error} = await supabase.from('projects').delete().eq('id', id).select('id');

  if (error) return {ok: false, reason: classifyError(error) === 'schema' ? 'schema' : 'failed'};

  // RLS menolak dengan diam. Policy `projects_delete` hanya melepas baris milik owner,
  // dan baris yang tidak lolos policy membuat DELETE tetap dijawab 200 tanpa menghapus
  // apa pun. Tanpa `select()` di atas, member biasa akan melihat project "terhapus"
  // lalu menemukannya kembali setelah halaman dimuat ulang.
  if (!data || data.length === 0) return {ok: false, reason: 'forbidden'};

  revalidatePath('/[locale]/(app)/projects', 'page');
  return {ok: true, name: existing.name};
}
