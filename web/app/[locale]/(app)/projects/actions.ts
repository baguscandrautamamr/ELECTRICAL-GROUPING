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

export type HideResult = {ok: true} | {ok: false; reason: 'schema' | 'failed'};

/**
 * Menyembunyikan satu project dari daftar milik user yang sedang masuk.
 *
 * Tidak ada baris model yang dihapus, dan model Revit-nya tidak disentuh. Yang ditulis
 * hanya satu baris `project_hidden` — pilihan tampilan, bukan penghapusan. Trigger
 * `sync_jobs_unhide` membuangnya lagi begitu add-in mengirim tarikan model berikutnya,
 * jadi project yang masih dikerjakan muncul kembali dengan sendirinya.
 */
export async function hideProject(id: string): Promise<HideResult> {
  const supabase = await createClient();
  const {
    data: {user}
  } = await supabase.auth.getUser();

  if (!user) return {ok: false, reason: 'failed'};

  // Upsert, bukan insert: menyembunyikan sesuatu yang sudah tersembunyi bukan kesalahan
  // yang perlu dilaporkan — dua tab yang terbuka bersamaan sudah cukup untuk membuatnya
  // terjadi, dan hasil akhirnya sama persis dengan yang diminta user.
  const {error} = await supabase
    .from('project_hidden')
    .upsert({project_id: id, user_id: user.id}, {onConflict: 'project_id,user_id'});

  if (error) return {ok: false, reason: classifyError(error) === 'schema' ? 'schema' : 'failed'};

  revalidatePath('/[locale]/(app)/projects', 'page');
  return {ok: true};
}

/**
 * Menampilkan kembali semua project yang disembunyikan user ini.
 *
 * Jalan keluar tanpa harus membuka Revit. Tanpa ini, satu klik salah hanya bisa dibatalkan
 * dengan mengirim tarikan model dari add-in — pintu satu arah untuk aksi yang justru
 * sengaja dibuat murah.
 */
export async function showHiddenProjects(): Promise<HideResult> {
  const supabase = await createClient();
  const {
    data: {user}
  } = await supabase.auth.getUser();

  if (!user) return {ok: false, reason: 'failed'};

  // RLS sudah mengurung penghapusan ini pada baris milik user sendiri; `eq` di sini
  // menyebutkannya sekali lagi supaya maksudnya terbaca dari kodenya.
  const {error} = await supabase.from('project_hidden').delete().eq('user_id', user.id);

  if (error) return {ok: false, reason: classifyError(error) === 'schema' ? 'schema' : 'failed'};

  revalidatePath('/[locale]/(app)/projects', 'page');
  return {ok: true};
}
