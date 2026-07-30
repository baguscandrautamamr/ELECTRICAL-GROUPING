/**
 * Wiring per ruangan dengan metode dua saklar.
 *
 * Seluruh berkas ini fungsi murni: titik masuk, titik keluar. Tidak menyentuh React,
 * tidak menyentuh database, tidak tahu apa-apa soal Revit. Itu disengaja — hasil
 * hitungan di sini adalah **daftar titik yang sama persis** yang nanti dikirim ke
 * add-in untuk digambar, jadi apa yang terlihat di pratinjau bukan sekadar mirip
 * dengan hasil di Revit, melainkan angka yang identik.
 *
 * Konsekuensinya, algoritmanya tidak boleh dikembarkan di sisi C#. Lapisan Revit
 * menggambar apa yang diperintahkan; keputusan bentuknya berhenti di sini.
 */
import type {Device} from '@/lib/contract';
import {isSelectable} from '@/lib/contract';

export const ROUTING_STYLES = ['chamfer', 'direct'] as const;
export type RoutingStyle = (typeof ROUTING_STYLES)[number];

/** Titik di koordinat model, milimeter, Y ke atas — sama seperti `devices.x_mm/y_mm`. */
export type Point = {x: number; y: number};

export type WiringOptions = {
  /** Berapa saklar yang membagi tiap ruangan. Dua berarti selang-seling papan catur. */
  switches: number;
  routing: RoutingStyle;
};

export const DEFAULT_WIRING_OPTIONS: WiringOptions = {
  switches: 2,
  routing: 'chamfer'
};

export type WireRoom = {
  key: string;
  /** Null berarti ruangannya tidak bernama di Revit dan dikira-kira dari kerapatan titik. */
  name: string | null;
  inferred: boolean;
  devices: number;
};

/** Satu kaki saklar di satu ruangan: urutan device-nya, plus garis siap gambar. */
export type WireRun = {
  roomKey: string;
  switchIndex: number;
  deviceIds: string[];
  vertices: Point[];
};

export type WiringPlan = {
  rooms: WireRoom[];
  runs: WireRun[];
  /** Jarak khas antar titik, dasar semua ukuran turunan. Null kalau titiknya < 2. */
  spacing: number | null;
};

/**
 * Jarak khas antar titik: median jarak ke tetangga terdekat.
 *
 * Median, bukan rata-rata dan bukan persentil bawah. Rata-rata ditarik jauh oleh
 * titik terpencil di ujung denah; persentil bawah dikuasai pasangan yang hampir
 * berimpit. Angka ini jadi dasar toleransi baris, ambang ruangan, dan panjang
 * chamfer sekaligus, jadi kalau ia meleset semuanya ikut meleset.
 */
export function medianNearestGap(points: readonly Point[]): number | null {
  if (points.length < 2) return null;

  // Mencontoh beberapa ratus titik sudah cukup menggambarkan kerapatan, dan biayanya
  // tetap terikat berapa pun besar denahnya.
  const step = Math.max(1, Math.ceil(points.length / 300));
  const gaps: number[] = [];

  for (let index = 0; index < points.length; index += step) {
    const a = points[index]!;
    let best = Infinity;

    for (const b of points) {
      if (b === a) continue;
      const dx = a.x - b.x;
      const dy = a.y - b.y;
      const squared = dx * dx + dy * dy;
      if (squared > 0 && squared < best) best = squared;
    }

    if (best < Infinity) gaps.push(Math.sqrt(best));
  }

  if (gaps.length === 0) return null;

  gaps.sort((a, b) => a - b);
  return gaps[Math.floor(gaps.length / 2)] ?? null;
}

/** Ambang dua titik dianggap satu ruangan, dan dua titik dianggap satu baris. */
const ROOM_REACH = 2.5;
const BAND_REACH = 0.5;

/** Panjang chamfer terhadap jarak khas antar titik. */
const CHAMFER_RATIO = 0.35;

const EPSILON = 1e-6;

function pointOf(device: Device): Point {
  return {x: device.x_mm, y: device.y_mm};
}

/**
 * Ruangan tiap device.
 *
 * `room_name` dipakai apa adanya kalau terisi. Ia sering kosong: add-in hanya bisa
 * membacanya kalau family punya Room Calculation Point, dan downlight yang di-host
 * ceiling biasanya tidak punya — lihat `ModelReader.RoomNameOf`.
 *
 * Yang kosong dikelompokkan dari kerapatannya dan **ditandai** sebagai kiraan, tidak
 * diam-diam disatukan dengan ruangan bernama di dekatnya. Menebak batas ruangan dari
 * jarak antar lampu menghasilkan pengelompokan yang kelihatan masuk akal padahal
 * salah — kesalahan yang paling lama tidak ketahuan. Lebih baik terlihat bahwa
 * bagian ini dikira-kira.
 */
function roomsOf(devices: readonly Device[], spacing: number): {key: string; name: string | null; devices: Device[]}[] {
  const named = new Map<string, Device[]>();
  const unnamed: Device[] = [];

  for (const device of devices) {
    const name = device.room_name?.trim();
    if (name) {
      const list = named.get(name);
      if (list) list.push(device);
      else named.set(name, [device]);
    } else {
      unnamed.push(device);
    }
  }

  const rooms: {key: string; name: string | null; devices: Device[]}[] = [...named.entries()]
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([name, list]) => ({key: `named:${name}`, name, devices: list}));

  clusters(unnamed, spacing * ROOM_REACH)
    // Urutan kiraan harus stabil antar muat ulang, kalau tidak "ruangan 2" berpindah
    // arti setiap kali halaman disegarkan. Kiri-atas lebih dulu.
    .sort((a, b) => topLeft(b) - topLeft(a))
    .forEach((list, index) => {
      rooms.push({key: `cluster:${index}`, name: null, devices: list});
    });

  return rooms;
}

/** Nilai urut kiri-atas: makin besar makin dekat ke pojok kiri atas denah. */
function topLeft(devices: readonly Device[]): number {
  let best = -Infinity;
  for (const device of devices) {
    const score = device.y_mm - device.x_mm;
    if (score > best) best = score;
  }
  return best;
}

/**
 * Kelompok titik yang saling terhubung dalam jarak `reach` (single linkage).
 *
 * Dipetakan ke grid sebesar `reach` lebih dulu, jadi tiap titik hanya dibandingkan
 * dengan isi sembilan sel di sekitarnya. Membandingkan semua pasangan akan
 * berperilaku baik di denah kecil lalu tersendat di lantai berisi ribuan titik —
 * dan lantai seperti itu justru yang paling butuh fitur ini.
 */
function clusters(devices: readonly Device[], reach: number): Device[][] {
  const count = devices.length;
  if (count === 0) return [];

  const parent = Array.from({length: count}, (_, index) => index);

  function find(index: number): number {
    let root = index;
    while (parent[root] !== root) root = parent[root]!;

    // Pemendekan jalur: tanpa ini pencarian berulang di kelompok besar jadi berantai.
    let walk = index;
    while (parent[walk] !== root) {
      const next = parent[walk]!;
      parent[walk] = root;
      walk = next;
    }

    return root;
  }

  function union(a: number, b: number) {
    const rootA = find(a);
    const rootB = find(b);
    if (rootA !== rootB) parent[rootA] = rootB;
  }

  const cells = new Map<string, number[]>();
  const coords = devices.map((device) => ({
    cx: Math.floor(device.x_mm / reach),
    cy: Math.floor(device.y_mm / reach)
  }));

  coords.forEach(({cx, cy}, index) => {
    const key = `${cx}:${cy}`;
    const list = cells.get(key);
    if (list) list.push(index);
    else cells.set(key, [index]);
  });

  const limit = reach * reach;

  for (let index = 0; index < count; index++) {
    const {cx, cy} = coords[index]!;
    const a = devices[index]!;

    for (let dx = -1; dx <= 1; dx++) {
      for (let dy = -1; dy <= 1; dy++) {
        const neighbours = cells.get(`${cx + dx}:${cy + dy}`);
        if (!neighbours) continue;

        for (const other of neighbours) {
          if (other <= index) continue;
          const b = devices[other]!;
          const gapX = a.x_mm - b.x_mm;
          const gapY = a.y_mm - b.y_mm;
          if (gapX * gapX + gapY * gapY <= limit) union(index, other);
        }
      }
    }
  }

  const grouped = new Map<number, Device[]>();
  for (let index = 0; index < count; index++) {
    const root = find(index);
    const list = grouped.get(root);
    if (list) list.push(devices[index]!);
    else grouped.set(root, [devices[index]!]);
  }

  return [...grouped.values()];
}

/**
 * Device satu ruangan dipecah jadi baris, atas ke bawah, tiap baris kiri ke kanan.
 *
 * Baris baru dimulai saat jarak Y ke titik sebelumnya melampaui toleransi — bukan
 * saat jaraknya terhadap titik pertama baris melampauinya. Lampu tidak pernah lurus
 * sempurna, dan ukuran terhadap titik pertama membuat baris yang melandai pelan
 * terpotong di tengah tanpa alasan yang terlihat.
 */
function bandsOf(devices: readonly Device[], tolerance: number): Device[][] {
  const sorted = [...devices].sort((a, b) => b.y_mm - a.y_mm || a.x_mm - b.x_mm);
  const bands: Device[][] = [];

  let current: Device[] = [];
  let previousY: number | null = null;

  for (const device of sorted) {
    if (previousY !== null && previousY - device.y_mm > tolerance) {
      bands.push(current);
      current = [];
    }

    current.push(device);
    previousY = device.y_mm;
  }

  if (current.length > 0) bands.push(current);

  return bands.map((band) => [...band].sort((a, b) => a.x_mm - b.x_mm));
}

/**
 * Urutan device tiap kaki saklar di satu ruangan.
 *
 * Kaki ditentukan papan catur: `(baris + kolom) % jumlah saklar`. Dengan dua saklar
 * dan dua kolom, hasilnya persis pola menyilang yang dipakai di lapangan — nyalakan
 * satu saklar dan tiap baris tetap dapat satu lampu, berselang-seling sisi, jadi
 * ruangan terang merata separuh alih-alih separuhnya gelap total.
 *
 * Di dalam satu baris, arahnya dibalik selang-seling baris (serpentine) supaya kaki
 * tidak melompat dari ujung kanan kembali ke ujung kiri tiap turun satu baris. Untuk
 * ruangan dua kolom ini tidak berpengaruh — tiap baris hanya menyumbang satu titik
 * per kaki — jadi pola menyilangnya tetap utuh.
 */
function legsOf(bands: readonly Device[][], switches: number): Device[][] {
  const legs: Device[][] = Array.from({length: switches}, () => []);

  bands.forEach((band, bandIndex) => {
    const members: Device[][] = Array.from({length: switches}, () => []);
    band.forEach((device, column) => {
      members[(bandIndex + column) % switches]!.push(device);
    });

    members.forEach((list, leg) => {
      legs[leg]!.push(...(bandIndex % 2 === 0 ? list : [...list].reverse()));
    });
  });

  return legs;
}

function push(into: Point[], point: Point) {
  const last = into[into.length - 1];
  if (last && Math.abs(last.x - point.x) < EPSILON && Math.abs(last.y - point.y) < EPSILON) return;
  into.push(point);
}

/**
 * Rute ortogonal dengan sudut dipotong 45°.
 *
 * Tiap ruas menempuh sumbu yang dominan lebih dulu, memotong sudutnya, lalu
 * menyelesaikan sumbu satunya. Panjang potongan dibatasi kaki terpendek: chamfer
 * sepanjang `c` memakan `c` di kedua sumbu sekaligus, jadi tanpa batas itu ia
 * melewati titik tujuan di ruas pendek dan garisnya berbalik arah.
 *
 * Saat batas itu tercapai, rutenya luruh jadi diagonal murni — bukan kegagalan,
 * memang bentuk yang benar untuk dua titik yang berdekatan menyerong.
 */
function routeChamfer(points: readonly Point[], chamfer: number): Point[] {
  if (points.length < 2) return [...points];

  const out: Point[] = [points[0]!];

  for (let index = 1; index < points.length; index++) {
    const from = points[index - 1]!;
    const to = points[index]!;

    const dx = to.x - from.x;
    const dy = to.y - from.y;
    const spanX = Math.abs(dx);
    const spanY = Math.abs(dy);

    // Sudah lurus di salah satu sumbu: tidak ada sudut untuk dipotong.
    if (spanX < EPSILON || spanY < EPSILON) {
      push(out, to);
      continue;
    }

    const cut = Math.min(chamfer, spanX, spanY);
    const stepX = Math.sign(dx);
    const stepY = Math.sign(dy);

    if (spanY >= spanX) {
      push(out, {x: from.x, y: to.y - stepY * cut});
      push(out, {x: from.x + stepX * cut, y: to.y});
    } else {
      push(out, {x: to.x - stepX * cut, y: from.y});
      push(out, {x: to.x, y: from.y + stepY * cut});
    }

    push(out, to);
  }

  return out;
}

/**
 * Rencana wiring seluruh denah.
 *
 * Device tanpa connector listrik dibuang lebih dulu: ia tidak bisa masuk circuit,
 * jadi menariknya ke dalam garis hanya menghasilkan gambar yang menjanjikan sesuatu
 * yang tidak bisa dikerjakan.
 */
export function planWiring(devices: readonly Device[], options: WiringOptions): WiringPlan {
  const wirable = devices.filter(isSelectable);
  const spacing = medianNearestGap(wirable.map(pointOf));

  // Kurang dari dua titik: tidak ada jarak khas yang bisa diukur, jadi tidak ada
  // baris, ruangan, maupun garis yang bisa disimpulkan. Ruangannya tetap dilaporkan
  // apa adanya supaya layar menyebut nama yang benar alih-alih "kiraan".
  if (spacing === null) {
    const only = wirable[0];
    return {
      rooms: only ? [{key: 'single', name: only.room_name?.trim() || null, inferred: false, devices: 1}] : [],
      runs: [],
      spacing: null
    };
  }

  const switches = Math.max(1, Math.round(options.switches));
  const chamfer = spacing * CHAMFER_RATIO;

  const rooms: WireRoom[] = [];
  const runs: WireRun[] = [];

  for (const room of roomsOf(wirable, spacing)) {
    rooms.push({
      key: room.key,
      name: room.name,
      inferred: room.name === null,
      devices: room.devices.length
    });

    const bands = bandsOf(room.devices, spacing * BAND_REACH);

    legsOf(bands, switches).forEach((leg, switchIndex) => {
      // Ruangan berisi satu lampu tidak bisa dibagi dua; kaki yang kosong dibuang
      // daripada muncul sebagai garis tanpa panjang.
      if (leg.length < 2) return;

      const path = leg.map(pointOf);

      runs.push({
        roomKey: room.key,
        switchIndex,
        deviceIds: leg.map((device) => device.revit_unique_id),
        vertices: options.routing === 'chamfer' ? routeChamfer(path, chamfer) : path
      });
    });
  }

  return {rooms, runs, spacing};
}
