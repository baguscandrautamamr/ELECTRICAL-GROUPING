/**
 * Ringkasan yang sudah dihitung Postgres, disusun ulang jadi bentuk yang dipakai layar.
 *
 * Barisnya datang dari `panel_contents` dan `unconnected_devices` — keduanya sudah
 * teragregasi di database. Yang dikerjakan di sini hanya mengelompokkan dan mengurutkan
 * beberapa ratus baris ringkasan, bukan menghitung ribuan device satu per satu.
 */
import type {DeviceKind, DeviceStatus} from '@/lib/contract';

/** Satu baris `panel_contents`: satu panel, satu circuit, satu jenis, satu family. */
export type PanelContentRow = {
  panel_unique_id: string;
  circuit_number: string | null;
  kind: DeviceKind;
  family_key: string;
  devices: number;
};

/** Satu baris `unconnected_devices`. `layout_unique_id` null = tidak tampak di denah mana pun. */
export type UnconnectedRow = {
  layout_unique_id: string | null;
  kind: DeviceKind;
  status: DeviceStatus;
  family_key: string;
  devices: number;
};

export type FamilyCount = {familyKey: string; devices: number};

export type PanelCircuit = {
  /** Selalu dari Revit. Null berarti circuit-nya belum punya nomor di model. */
  number: string | null;
  devices: number;
  lighting: number;
  receptacle: number;
  families: FamilyCount[];
};

export type UnconnectedGroup = {
  layoutUniqueId: string | null;
  devices: number;
  lighting: number;
  receptacle: number;
  families: FamilyCount[];
  /** Berapa yang circuit-nya sudah ada tapi panelnya masih kosong. */
  noPanel: number;
};

/**
 * Nomor circuit diurutkan secara alami, jadi `(LC)2` mendahului `(LC)11` —
 * perbandingan string biasa menaruhnya terbalik, dan daftar panel yang isinya puluhan
 * circuit jadi tidak bisa ditelusuri.
 */
const byNumber = new Intl.Collator(undefined, {numeric: true, sensitivity: 'base'});

/** Yang paling banyak lebih dulu: itu yang paling menentukan isi sebuah circuit. */
function byCountThenName(a: FamilyCount, b: FamilyCount) {
  return b.devices - a.devices || a.familyKey.localeCompare(b.familyKey);
}

/** Isi tiap panel, dikelompokkan per circuit. Kuncinya `revit_unique_id` panel. */
export function groupPanelContents(rows: PanelContentRow[]): Record<string, PanelCircuit[]> {
  const panels: Record<string, Map<string, PanelCircuit>> = {};

  for (const row of rows) {
    const circuits = (panels[row.panel_unique_id] ??= new Map());

    // Circuit tanpa nomor tetap punya satu keranjang sendiri, terpisah dari yang
    // bernomor — menggabungkannya akan menyatukan circuit yang tidak berhubungan.
    const key = row.circuit_number ?? '';
    const circuit = circuits.get(key) ?? {
      number: row.circuit_number,
      devices: 0,
      lighting: 0,
      receptacle: 0,
      families: []
    };

    const devices = Number(row.devices);
    circuit.devices += devices;
    if (row.kind === 'lighting') circuit.lighting += devices;
    else circuit.receptacle += devices;

    // Satu family bisa muncul dua kali di satu circuit — sekali sebagai lighting dan
    // sekali sebagai receptacle — jadi entrinya dijumlahkan, bukan ditumpuk.
    const family = circuit.families.find((item) => item.familyKey === row.family_key);
    if (family) family.devices += devices;
    else circuit.families.push({familyKey: row.family_key, devices});

    circuits.set(key, circuit);
  }

  return Object.fromEntries(
    Object.entries(panels).map(([panelId, circuits]) => [
      panelId,
      [...circuits.values()]
        .map((circuit) => ({...circuit, families: circuit.families.sort(byCountThenName)}))
        .sort((a, b) => byNumber.compare(a.number ?? '', b.number ?? ''))
    ])
  );
}

/**
 * Sisa pekerjaan, dikelompokkan per denah.
 *
 * Satu device bisa tampak di dua denah sekaligus, jadi menjumlahkan `devices` di seluruh
 * grup akan menghitungnya dua kali. Angka di judul kartu datang dari `unconnected_total`,
 * yang menghitung device — bukan kemunculannya di denah.
 */
export function groupUnconnected(rows: UnconnectedRow[]): UnconnectedGroup[] {
  const groups = new Map<string, UnconnectedGroup>();

  for (const row of rows) {
    const key = row.layout_unique_id ?? '';
    const group = groups.get(key) ?? {
      layoutUniqueId: row.layout_unique_id,
      devices: 0,
      lighting: 0,
      receptacle: 0,
      families: [],
      noPanel: 0
    };

    const devices = Number(row.devices);
    group.devices += devices;
    if (row.kind === 'lighting') group.lighting += devices;
    else group.receptacle += devices;
    if (row.status === 'no_panel') group.noPanel += devices;

    const family = group.families.find((item) => item.familyKey === row.family_key);
    if (family) family.devices += devices;
    else group.families.push({familyKey: row.family_key, devices});

    groups.set(key, group);
  }

  return [...groups.values()]
    .map((group) => ({...group, families: group.families.sort(byCountThenName)}))
    .sort((a, b) => {
      // Yang tidak punya denah selalu terakhir: ia tidak bisa dibuka, jadi menaruhnya
      // di atas hanya menghalangi daftar yang bisa ditindaklanjuti.
      if ((a.layoutUniqueId === null) !== (b.layoutUniqueId === null)) {
        return a.layoutUniqueId === null ? 1 : -1;
      }
      return b.devices - a.devices;
    });
}
