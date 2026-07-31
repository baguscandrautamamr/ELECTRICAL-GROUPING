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

export type SyncDirection = 'apply' | 'snapshot' | 'wiring';
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

/**
 * Satu device yang tampak di satu layout.
 *
 * Isi sebuah denah ditentukan view Revit-nya — filter view, visibility kategori, crop
 * region, fase — bukan pasangan (level, kind). Satu lantai bisa punya denah lighting
 * dan denah emergency/exit sekaligus, dan keduanya berlantai serta berjenis sama.
 */
export type LayoutDevice = {
  project_id: string;
  layout_unique_id: string;
  device_unique_id: string;
};

/**
 * Saklar atau sensor dari kategori Revit `OST_LightingDevices` — bukan lampunya,
 * yang ada di `OST_LightingFixtures`.
 *
 * Jumlahnya di sebuah ruangan menentukan lampu ruangan itu dipecah jadi berapa
 * grouping. Tanpa ini batas grouping hanya bisa disimpulkan dari kerapatan lampu,
 * dan dua ruangan yang dipisah dinding tipis dilebur jadi satu — lihat
 * `supabase/migrations/20260731000000_lighting_devices.sql`.
 */
export type LightingDevice = {
  project_id: string;
  revit_unique_id: string;
  family_key: string;
  level_key: string;
  room_name: string | null;
  x_mm: number;
  y_mm: number;
};

/**
 * Satu saklar yang tampak di satu layout.
 *
 * Pasangan `LayoutDevice` untuk saklar, dan ada karena alasan yang sama. Satu lantai bisa
 * punya denah lighting dan denah emergency/exit sekaligus; keduanya `level_key` sama, jadi
 * menyaring saklar dengan lantai membuat kedua halaman menerima seluruh saklar lantai itu —
 * dan jumlah grouping di keduanya salah — lihat
 * `supabase/migrations/20260731010000_layout_lighting_devices.sql`.
 */
export type LayoutLightingDevice = {
  project_id: string;
  layout_unique_id: string;
  lighting_device_unique_id: string;
};

/**
 * Line style dari model: subcategory kategori Revit `OST_Lines`, yang di Revit muncul di
 * dialog Line Styles.
 *
 * Web tidak pernah mengarang namanya. Yang sah hanya yang ada di model, karena add-in
 * harus menemukannya kembali lewat `revit_unique_id` untuk dipasang ke garis yang
 * digambar — dan nama style bisa diubah user kapan saja.
 */
export type LineStyle = {
  project_id: string;
  revit_unique_id: string;
  name: string;
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
  /**
   * Panel pemuat device ini, dibaca add-in dari model. Null berarti belum tersambung
   * ke panel mana pun — atau model terakhir ditarik add-in versi lama, dan isi panel
   * jatuh ke circuit yang pernah diterapkan lewat web.
   */
  panel_unique_id: string | null;
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

/**
 * Satu garis wiring yang sedang ada di model Revit, dicatat add-in setelah menggambar.
 *
 * Ini yang membuat pengiriman garis jadi pengganti, bukan tambahan: add-in membaca daftar
 * ini, menghapus garisnya dari model, lalu menggambar kiriman baru. Tanpa catatan ini
 * kiriman kedua menumpuk di atas yang pertama — lihat
 * `supabase/migrations/20260731030000_wiring_curves.sql`.
 */
export type WiringCurve = {
  project_id: string;
  layout_unique_id: string;
  /** `UniqueId` detail curve di Revit, bukan id garis di web. */
  revit_unique_id: string;
  switch_index: number;
};

/**
 * Satu kaki saklar di dalam payload job `wiring`. Cermin dari `WireRunRow` di C#.
 *
 * Titiknya milimeter di koordinat model, sama seperti `device.x_mm`. Add-in memakainya apa
 * adanya — bentuk garisnya diputuskan di sini, bukan di sana.
 */
export type WireRunPayload = {
  switch_index: number;
  vertices: {x_mm: number; y_mm: number}[];
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
