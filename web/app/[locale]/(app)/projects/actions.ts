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
