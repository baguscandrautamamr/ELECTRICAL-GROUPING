'use server';

import {isKind} from '@/lib/contract';
import {createClient} from '@/lib/supabase/server';

export type CircuitActionResult =
  | {ok: true}
  | {ok: false; reason: 'selection' | 'panel' | 'kind' | 'failed'};

/**
 * Menyimpan usulan grouping sebagai circuit berstatus draft.
 *
 * Tidak ada nomor circuit di sini. Nomor seperti `(LC)1` dibuat Revit saat
 * SelectPanel berhasil; sebelum diterapkan, usulan tampil tanpa nomor.
 */
export async function createCircuit(input: {
  projectId: string;
  panelUniqueId: string;
  kind: string;
  deviceUniqueIds: string[];
}): Promise<CircuitActionResult> {
  if (input.deviceUniqueIds.length === 0) return {ok: false, reason: 'selection'};
  if (input.panelUniqueId.length === 0) return {ok: false, reason: 'panel'};
  if (!isKind(input.kind)) return {ok: false, reason: 'kind'};

  const supabase = await createClient();
  const {error} = await supabase.from('circuits').insert({
    project_id: input.projectId,
    panel_unique_id: input.panelUniqueId,
    kind: input.kind,
    device_unique_ids: input.deviceUniqueIds,
    status: 'draft'
  });

  return error ? {ok: false, reason: 'failed'} : {ok: true};
}

/**
 * Mengubah isi circuit yang sudah ada: titik yang ikut, dan panel tujuannya.
 *
 * Circuit kembali ke `draft` supaya masuk lagi ke antrean kirim berikutnya.
 * `revit_unique_id` sengaja dipertahankan — itu penanda bagi add-in bahwa circuit ini
 * sudah hidup di model dan harus dibongkar dulu sebelum dibuat ulang dengan isi baru.
 * Menghapusnya akan meninggalkan circuit lama menggantung di Revit.
 *
 * Nomor circuit dikosongkan karena nomor lama tidak lagi berlaku, dan yang baru datang
 * dari Revit — bukan dari sini.
 */
export async function updateCircuit(input: {
  circuitId: string;
  panelUniqueId: string;
  deviceUniqueIds: string[];
}): Promise<CircuitActionResult> {
  if (input.deviceUniqueIds.length === 0) return {ok: false, reason: 'selection'};
  if (input.panelUniqueId.length === 0) return {ok: false, reason: 'panel'};

  const supabase = await createClient();
  const {error} = await supabase
    .from('circuits')
    .update({
      panel_unique_id: input.panelUniqueId,
      device_unique_ids: input.deviceUniqueIds,
      status: 'draft',
      circuit_number: null,
      error: null
    })
    .eq('id', input.circuitId)
    // Circuit yang sedang dikerjakan add-in tidak boleh berubah di tengah jalan.
    .in('status', ['draft', 'failed', 'applied']);

  return error ? {ok: false, reason: 'failed'} : {ok: true};
}

/** Hanya usulan yang boleh dihapus. Yang sudah diterapkan adalah catatan model, bukan draft. */
export async function removeCircuit(circuitId: string): Promise<CircuitActionResult> {
  const supabase = await createClient();
  const {error} = await supabase.from('circuits').delete().eq('id', circuitId).in('status', ['draft', 'failed']);

  return error ? {ok: false, reason: 'failed'} : {ok: true};
}

/**
 * Website tidak pernah menulis langsung ke Revit. Ini hanya memindahkan circuit ke
 * `queued` dan menyisipkan satu baris `sync_jobs` — keduanya dalam satu transaksi
 * di dalam fungsi database, supaya tidak ada job yang menunjuk circuit yang masih draft.
 */
export async function queueApply(projectId: string, circuitIds: string[]): Promise<CircuitActionResult> {
  if (circuitIds.length === 0) return {ok: false, reason: 'selection'};

  const supabase = await createClient();
  const {error} = await supabase.rpc('queue_apply', {
    p_project: projectId,
    p_circuit_ids: circuitIds
  });

  return error ? {ok: false, reason: 'failed'} : {ok: true};
}
