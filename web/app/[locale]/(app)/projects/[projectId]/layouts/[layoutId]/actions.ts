'use server';

import type {WireRunPayload} from '@/lib/contract';
import {isKind} from '@/lib/contract';
import {createClient} from '@/lib/supabase/server';

export type CircuitActionResult =
  | {ok: true}
  | {ok: false; reason: 'selection' | 'panel' | 'kind' | 'failed'};

export type WiringActionResult =
  | {ok: true}
  | {ok: false; reason: 'selection' | 'lineStyle' | 'failed'};

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

/**
 * Mengantre gambar garis wiring untuk sebuah denah.
 *
 * Jalur yang sama seperti `queueApply`: web hanya menyisipkan satu baris `sync_jobs`,
 * add-in yang mengambilnya. Yang berbeda isinya — bukan rencana circuit melainkan
 * polyline yang titiknya sudah selesai dihitung di `lib/wiring.ts`. Add-in menggambar
 * apa yang dikirim, jadi garis di Revit adalah angka yang identik dengan pratinjau.
 *
 * Tidak ada circuit yang ikut berpindah status di sini. Garis wiring bukan circuit: ia
 * tidak punya panel, tidak punya nomor, dan tidak menyambungkan apa pun secara listrik.
 *
 * Layout dan line style diperiksa di dalam `queue_wiring` terhadap project yang sama.
 * RLS menahan pembacaan baris project lain, tapi tidak menahan penyebutan id-nya di
 * dalam payload jsonb — jadi yang menjaga itu fungsi database, bukan kode ini.
 */
export async function queueWiring(input: {
  projectId: string;
  layoutUniqueId: string;
  lineStyleUniqueId: string;
  runs: WireRunPayload[];
}): Promise<WiringActionResult> {
  if (input.runs.length === 0) return {ok: false, reason: 'selection'};
  if (input.lineStyleUniqueId.length === 0) return {ok: false, reason: 'lineStyle'};

  const supabase = await createClient();
  const {error} = await supabase.rpc('queue_wiring', {
    p_project: input.projectId,
    p_layout: input.layoutUniqueId,
    p_line_style: input.lineStyleUniqueId,
    p_runs: input.runs
  });

  return error ? {ok: false, reason: 'failed'} : {ok: true};
}
