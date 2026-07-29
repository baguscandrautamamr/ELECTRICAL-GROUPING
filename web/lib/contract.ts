/**
 * Kontrak data. Cermin dari `addin/src/CircuitSync.Core/Contract.cs` dan
 * `supabase/migrations/`. Kalau berubah di sini, ubah juga di sana pada commit
 * yang sama — PostgREST menolak kolom yang tidak dikenal tanpa pesan yang jelas.
 *
 * Definisikan sekali di file ini. Jangan menduplikasi tipenya di komponen.
 */

export const DEVICE_KINDS = ['lighting', 'receptacle'] as const;
export type DeviceKind = (typeof DEVICE_KINDS)[number];

export const DEVICE_STATUSES = ['unwired', 'no_panel', 'connected', 'no_connector'] as const;
export type DeviceStatus = (typeof DEVICE_STATUSES)[number];

export const CIRCUIT_STATUSES = ['draft', 'queued', 'applied', 'failed'] as const;
export type CircuitStatus = (typeof CIRCUIT_STATUSES)[number];

export type SyncDirection = 'apply' | 'snapshot';
export type SyncJobStatus = 'queued' | 'applied' | 'failed';

export type Project = {
  id: string;
  name: string;
  owner_id: string;
  created_at: string;
  updated_at: string;
};

export type Level = {
  project_id: string;
  level_key: string;
  name: string;
  elevation_mm: number;
  sort_order: number;
};

/**
 * Cerminan satu view denah Revit, dan halaman kerja di web. Nama view sudah memuat
 * lantai dan jenis sekaligus, jadi layout menggantikan pasangan (level, kind) —
 * lihat `supabase/migrations/20260729000000_layouts.sql`.
 */
export type Layout = {
  project_id: string;
  revit_unique_id: string;
  name: string;
  kind: DeviceKind;
  level_key: string;
  /** Penyebut skala Revit: 1:100 disimpan sebagai 100. */
  scale: number | null;
  /** Crop region dalam milimeter model. Null berarti crop tidak aktif di Revit. */
  crop_min_x_mm: number | null;
  crop_min_y_mm: number | null;
  crop_max_x_mm: number | null;
  crop_max_y_mm: number | null;
  sort_order: number;
};

export type Device = {
  project_id: string;
  revit_unique_id: string;
  kind: DeviceKind;
  level_key: string;
  room_name: string | null;
  family_key: string;
  x_mm: number;
  y_mm: number;
  va: number | null;
  status: DeviceStatus;
  circuit_number: string | null;
};

export type Panel = {
  project_id: string;
  revit_unique_id: string;
  name: string;
  prefix: string | null;
  distribution_system: string | null;
  voltage: string | null;
  phase: number | null;
  slots_total: number | null;
  slots_used: number | null;
  is_usable: boolean;
};

export type Circuit = {
  id: string;
  project_id: string;
  panel_unique_id: string;
  kind: DeviceKind;
  device_unique_ids: string[];
  /** Selalu dari Revit. Web tidak pernah mengisi ini. */
  circuit_number: string | null;
  status: CircuitStatus;
  revit_unique_id: string | null;
  error: string | null;
  created_at: string;
  updated_at: string;
};

export type SyncJob = {
  id: string;
  project_id: string;
  direction: SyncDirection;
  status: SyncJobStatus;
  payload: Record<string, unknown>;
  error: string | null;
  applied_at: string | null;
  created_at: string;
};

export type SymbolOverride = {
  project_id: string;
  family_key: string;
  symbol: string;
};

/** `family_key` = `"{FamilyName}::{TypeName}"`. */
export const FAMILY_KEY_SEPARATOR = '::';

export function splitFamilyKey(key: string): {family: string; type: string} {
  const at = key.indexOf(FAMILY_KEY_SEPARATOR);
  if (at < 0) return {family: key, type: ''};
  return {
    family: key.slice(0, at),
    type: key.slice(at + FAMILY_KEY_SEPARATOR.length)
  };
}

export function isKind(value: string): value is DeviceKind {
  return (DEVICE_KINDS as readonly string[]).includes(value);
}

/** Device tanpa connector listrik tidak bisa masuk circuit, jadi tidak bisa dipilih. */
export function isSelectable(device: Device): boolean {
  return device.status !== 'no_connector';
}
