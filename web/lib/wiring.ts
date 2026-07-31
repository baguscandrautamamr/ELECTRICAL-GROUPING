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
import type {Device, LightingDevice, WireRunPayload} from '@/lib/contract';
import {isSelectable} from '@/lib/contract';

/**
 * Dua gaya gambar, dan gaya adalah pilihan user — bukan sesuatu yang disimpulkan
 * dari bentuk ruangan. Keduanya berlaku di ruangan berapa pun kolomnya.
 *
 * `crossing` — kolom diambil berpasangan dan tiap pasangan menyilang dengan garis
 * lurus diagonal. Kolom ganjil yang tersisa dirangkai tegak lalu ditempelkan ke
 * salah satu kaki, jadi jumlah saklarnya tidak ikut bertambah.
 *
 * `orthogonal` — tidak ada silang sama sekali. Kolom ditelusuri satu per satu naik
 * turun, dan perpindahannya bersudut potong 45°.
 */
export const WIRE_STYLES = ['crossing', 'orthogonal'] as const;
export type WireStyle = (typeof WIRE_STYLES)[number];

/** Titik di koordinat model, milimeter, Y ke atas — sama seperti `devices.x_mm/y_mm`. */
export type Point = {x: number; y: number};

export type WiringOptions = {
  /**
   * Berapa saklar yang membagi tiap ruangan. Hanya berlaku untuk gaya `orthogonal`;
   * `crossing` selalu menghasilkan dua kaki, karena menyilang butuh sepasang kolom
   * dan pasangan hanya punya dua sisi.
   */
  switches: number;
  style: WireStyle;
};

export const DEFAULT_WIRING_OPTIONS: WiringOptions = {
  switches: 2,
  style: 'crossing'
};

/** Gaya `crossing` tidak bisa dibagi lebih dari dua; pasangan kolom hanya punya dua sisi. */
export function switchesFor(options: WiringOptions): number {
  return options.style === 'crossing' ? 2 : Math.max(1, Math.round(options.switches));
}

export type WireRoom = {
  key: string;
  /** Null berarti ruangannya tidak bernama di Revit dan dikira-kira dari kerapatan titik. */
  name: string | null;
  inferred: boolean;
  /** Benar kalau ruangan ini hasil pemecahan karena saklarnya lebih dari satu. */
  split: boolean;
  /** Saklar yang jatuh ke ruangan asalnya. Nol berarti model belum membawa datanya. */
  switches: number;
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

/**
 * Memecah sekumpulan lampu jadi sebanyak saklar yang ada di dalamnya.
 *
 * Kerapatan saja tidak cukup untuk menemukan batas ruangan. Dua ruangan yang dipisah
 * dinding tipis berjarak sekitar 1,5 sampai 2 kali jarak antar lampu — di bawah ambang
 * `ROOM_REACH`, jadi keduanya dilebur jadi satu dan garis wiring menyeberang dinding.
 * Menaikkan ambangnya cuma memindahkan salah tebak ke ukuran ruangan yang lain.
 *
 * Jumlah saklar menjawabnya tanpa menebak: dua lighting device berarti dua grouping,
 * serapat apa pun lampunya. Pemotongannya dilakukan dengan mengecilkan jangkauan
 * pengelompokan sampai kumpulannya benar-benar pecah jadi sebanyak itu — dengan
 * sendirinya potongan jatuh di celah terlebar, yaitu di dindingnya.
 */
function splitBySwitches(devices: readonly Device[], parts: number, spacing: number): Device[][] {
  if (parts < 2 || devices.length < parts) return [[...devices]];

  // Dipotong **antar baris**, bukan lewat pengelompokan ulang seluruh titik.
  // Percobaan pertama mengecilkan jangkauan pengelompokan sampai kumpulannya pecah,
  // dan itu bekerja hanya kalau ada celah. Di ruangan seragam tidak ada celah sama
  // sekali: begitu jangkauannya turun di bawah jarak antar lampu, kumpulannya tidak
  // pecah jadi dua melainkan langsung hancur jadi satu lampu per bagian.
  const columns = columnGroups(devices, spacing * BAND_REACH);
  if (columns.length < parts) return [[...devices]];

  // Celah antar baris. Dinding pemisah muncul di sini sebagai celah yang paling lebar.
  const gaps: {at: number; width: number}[] = [];
  for (let index = 1; index < columns.length; index++) {
    gaps.push({at: index, width: columns[index]![0]!.x_mm - columns[index - 1]!.at(-1)!.x_mm});
  }

  // Celah terlebar menang. Seri diputus oleh keseimbangan — di ruangan seragam semua
  // celahnya sama, dan tanpa ini potongannya jatuh di tepi alih-alih di tengah.
  const ideal = (step: number) => (step * columns.length) / parts;
  const balance = (at: number) =>
    Math.min(...Array.from({length: parts - 1}, (_, step) => Math.abs(at - ideal(step + 1))));

  const cuts = gaps
    .slice()
    .sort((a, b) => b.width - a.width || balance(a.at) - balance(b.at) || a.at - b.at)
    .slice(0, parts - 1)
    .map((gap) => gap.at)
    .sort((a, b) => a - b);

  const pieces: Device[][] = [];
  let start = 0;
  for (const cut of [...cuts, columns.length]) {
    pieces.push(columns.slice(start, cut).flat());
    start = cut;
  }

  return pieces.filter((piece) => piece.length > 0);
}

/** Lampu satu ruangan dikelompokkan per baris tegak, urut dari kiri. */
function columnGroups(devices: readonly Device[], tolerance: number): Device[][] {
  const sorted = [...devices].sort((a, b) => a.x_mm - b.x_mm || b.y_mm - a.y_mm);
  const groups: Device[][] = [];

  let current: Device[] = [];
  let previousX: number | null = null;

  for (const device of sorted) {
    if (previousX !== null && device.x_mm - previousX > tolerance) {
      groups.push(current);
      current = [];
    }

    current.push(device);
    previousX = device.x_mm;
  }

  if (current.length > 0) groups.push(current);

  return groups;
}

/**
 * Berapa saklar yang jatuh ke tiap kumpulan lampu.
 *
 * Tiap saklar dimiliki kumpulan yang lampunya paling dekat dengannya. Diputuskan
 * lewat perbandingan antar kumpulan, bukan per kumpulan sendiri-sendiri: saklar di
 * dinding pemisah berjarak hampir sama ke dua ruangan, dan menghitungnya di dua-duanya
 * membuat kedua ruangan terpecah lebih banyak daripada yang sebenarnya.
 */
function switchCounts(rooms: readonly {devices: Device[]}[], switches: readonly Point[]): number[] {
  const counts = rooms.map(() => 0);

  for (const point of switches) {
    let owner = -1;
    let nearest = Infinity;

    rooms.forEach((room, index) => {
      for (const device of room.devices) {
        const gap = Math.hypot(point.x - device.x_mm, point.y - device.y_mm);
        if (gap < nearest) {
          nearest = gap;
          owner = index;
        }
      }
    });

    if (owner >= 0) counts[owner]!++;
  }

  return counts;
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
 * Kolom sebuah ruangan: sama seperti baris, tapi diukur di sumbu X.
 *
 * Kolom dihitung sekali untuk seluruh ruangan, bukan per baris. Baris yang kebetulan
 * kehilangan satu lampu — ceruk, kolom struktur, apa pun — tidak boleh menggeser
 * makna "kolom kedua" bagi baris lain, kalau tidak pola silangnya berantakan justru
 * di ruangan yang tidak sempurna persegi.
 */
function columnsOf(devices: readonly Device[], tolerance: number): Map<string, number> {
  const sorted = [...devices].sort((a, b) => a.x_mm - b.x_mm);
  const index = new Map<string, number>();

  let column = 0;
  let previousX: number | null = null;

  for (const device of sorted) {
    if (previousX !== null && device.x_mm - previousX > tolerance) column++;
    index.set(device.revit_unique_id, column);
    previousX = device.x_mm;
  }

  return index;
}

/** Petak (baris, kolom) satu ruangan. Sel bisa kosong; ruangan jarang persegi penuh. */
type Grid = {
  rows: number;
  columns: number;
  at: (row: number, column: number) => Device | undefined;
};

function gridOf(bands: readonly Device[][], columns: Map<string, number>): Grid {
  const cells = new Map<string, Device>();
  let widest = 0;

  bands.forEach((band, row) => {
    for (const device of band) {
      const column = columns.get(device.revit_unique_id) ?? 0;
      cells.set(`${row}:${column}`, device);
      if (column + 1 > widest) widest = column + 1;
    }
  });

  return {
    rows: bands.length,
    columns: widest,
    at: (row, column) => cells.get(`${row}:${column}`)
  };
}

/** Satu lampu beserta letaknya di petak ruangan. */
type Cell = {device: Device; row: number; column: number};

/**
 * Urutan sambungan satu kaki, mengikuti cara baris dihabiskan berpasangan.
 *
 * "Baris" di sini deretan lampu **tegak**. Baris dihabiskan dua-dua: (1,2), lalu
 * (3,4), dan seterusnya. Tiap pasangan menyilang — di dalam satu pasangan, kaki ini
 * mengambil sisi kiri di lampu ganjil dan sisi kanan di lampu genap, jadi urutan
 * turunnya membentuk X. Baris yang tidak kebagian pasangan dikerjakan terakhir.
 *
 * Pembagian saklarnya papan catur `(baris + kolom) % jumlah saklar`, dan itu berlaku
 * di baris sisa juga: lampu ke-1, ke-3, ke-5 ke satu saklar, ke-2 dan ke-4 ke saklar
 * lain. Akibatnya dua lampu sewarna di baris sisa selalu terpisah satu lampu, dan
 * garisnya wajib memutar untuk melompatinya — itulah zigzag di baris sisa.
 *
 * Percobaan sebelumnya memilih sambungan lewat tetangga terdekat tanpa mengenal
 * pasangan sama sekali. Hasilnya bentuk chevron yang menyapu seluruh ruangan: benar
 * menurut papan catur, tapi bukan silang per pasangan seperti gambar acuan.
 */
function orderLeg(grid: Grid, switchIndex: number, switches: number): Cell[] {
  const mine = (row: number, column: number): Cell | null => {
    const device = grid.at(row, column);
    if (!device || (row + column) % switches !== switchIndex) return null;
    return {device, row, column};
  };

  const segments: Cell[][] = [];
  let column = 0;

  for (; column + 1 < grid.columns; column += 2) {
    const segment: Cell[] = [];
    for (let row = 0; row < grid.rows; row++) {
      // Di dalam satu pasangan, satu baris hanya menyumbang satu lampu ke kaki ini —
      // itulah yang membuat urutannya berpindah sisi tiap turun, alias menyilang.
      for (const side of [column, column + 1]) {
        const cell = mine(row, side);
        if (cell) segment.push(cell);
      }
    }
    if (segment.length > 0) segments.push(segment);
  }

  // Baris sisa, kalau jumlah barisnya ganjil.
  if (column < grid.columns) {
    const segment: Cell[] = [];
    for (let row = 0; row < grid.rows; row++) {
      const cell = mine(row, column);
      if (cell) segment.push(cell);
    }
    if (segment.length > 0) segments.push(segment);
  }

  const ordered: Cell[] = [];

  for (const segment of segments) {
    const previous = ordered[ordered.length - 1];

    // Segmen berikutnya dimasuki dari ujung yang paling dekat. Tanpa ini, kaki yang
    // selesai di bawah harus melompat ke puncak baris sisa dan garis sambungnya
    // memotong seluruh ruangan.
    if (previous) {
      const head = segment[0]!;
      const tail = segment[segment.length - 1]!;
      const toHead = Math.hypot(head.row - previous.row, head.column - previous.column);
      const toTail = Math.hypot(tail.row - previous.row, tail.column - previous.column);
      if (toTail < toHead) segment.reverse();
    }

    ordered.push(...segment);
  }

  return ordered;
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
 * Seberapa jauh garis memutar ke samping saat menghindari lampu, terhadap jarak khas.
 *
 * **Harus lebih besar daripada `CLEARANCE_RATIO`.** Kalau tidak, lajur putarannya
 * sendiri jatuh di dalam radius bersih lampu yang sedang dihindari — putarannya
 * dinilai menyerempet, tidak ada kandidat yang lolos, dan rutenya kembali menembus
 * lampu. Versi pertama memakai 0,32 lawan 0,4 dan gagal persis begitu.
 */
const DETOUR_RATIO = 0.5;

/** Sedekat apa sebuah lampu dianggap terhalangi oleh garis yang lewat. */
const CLEARANCE_RATIO = 0.35;

/** Jarak titik ke ruas garis. Dipakai memutuskan apakah sebuah lampu terlewati. */
function distanceToSegment(point: Point, a: Point, b: Point): number {
  const dx = b.x - a.x;
  const dy = b.y - a.y;
  const lengthSquared = dx * dx + dy * dy;

  if (lengthSquared < EPSILON) return Math.hypot(point.x - a.x, point.y - a.y);

  const t = Math.max(0, Math.min(1, ((point.x - a.x) * dx + (point.y - a.y) * dy) / lengthSquared));
  return Math.hypot(point.x - (a.x + t * dx), point.y - (a.y + t * dy));
}

/** Satu ruas garis yang sudah dipakai kaki lain. */
type Segment = {a: Point; b: Point};

/** Sedekat apa dua garis dianggap menumpuk, terhadap jarak khas antar lampu. */
const OVERLAP_RATIO = 0.18;

/**
 * Rute satu kaki, menghindari lampu kaki lain **dan** garis kaki lain.
 *
 * Dua batasan berjalan bersamaan, dan keduanya datang dari papan catur. Karena
 * lampu sewarna selalu diselingi lampu warna lain, sambungan yang bukan diagonal
 * pasti melewati lampu yang bukan miliknya kalau ditarik lurus. Dan karena kedua
 * kaki menempati ruangan yang sama, keduanya cenderung memilih lajur yang sama
 * lalu bertumpuk — garis yang bertumpuk tidak bisa dibaca sebagai dua sirkuit.
 *
 * Karena itu tiap ruas menawar tiga rute: langsung, memutar ke kiri, memutar ke
 * kanan. Yang menabrak lampu didiskualifikasi lebih dulu; sisanya dinilai dari
 * seberapa panjang ia berimpit dengan garis yang sudah ada, lalu dari panjangnya
 * sendiri. Rute langsung menang saat seri — memutar tanpa sebab hanya menambah
 * belokan yang harus dibaca orang.
 */
function routeAvoiding(
  points: readonly Point[],
  obstacles: readonly Point[],
  occupied: readonly Segment[],
  detour: number,
  chamfer: number,
  style: WireStyle,
  centerX: number
): Point[] {
  if (points.length < 2) return [...points];

  const spacing = detour / DETOUR_RATIO;
  const clearance = spacing * CLEARANCE_RATIO;
  const overlapReach = spacing * OVERLAP_RATIO;

  /**
   * Berapa lampu yang terserempet. Diukur pada rute **yang benar-benar digambar**,
   * bukan pada garis lurus antar lampu: chamfer memotong sudut, dan potongan itu
   * bisa lewat dekat lampu yang garis lurusnya sendiri jauh.
   */
  function hits(route: readonly Point[]): number {
    let count = 0;
    for (let index = 1; index < route.length; index++) {
      for (const obstacle of obstacles) {
        if (distanceToSegment(obstacle, route[index - 1]!, route[index]!) < clearance) count++;
      }
    }
    return count;
  }

  /**
   * Seberapa panjang rute ini **berjalan berdampingan** dengan garis yang sudah ada.
   *
   * Yang dihitung deretan contoh yang berdekatan berturut-turut, bukan jumlah contoh
   * yang dekat. Dua garis yang bersilangan pasti punya satu dua contoh yang dekat di
   * titik potongnya — dan bersilangan justru wajib ada di pola X. Yang tidak boleh
   * adalah dua garis yang berimpit sepanjang jalan, karena itu terbaca sebagai satu
   * garis, bukan dua sirkuit.
   */
  function overlap(route: readonly Point[]): number {
    if (occupied.length === 0) return 0;

    let total = 0;
    let run = 0;

    for (let index = 1; index < route.length; index++) {
      const a = route[index - 1]!;
      const b = route[index]!;
      const steps = Math.max(2, Math.ceil(Math.hypot(b.x - a.x, b.y - a.y) / (spacing / 6)));

      for (let step = 0; step <= steps; step++) {
        const t = step / steps;
        const point = {x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t};

        if (occupied.some((seg) => distanceToSegment(point, seg.a, seg.b) < overlapReach)) {
          run++;
        } else {
          // Deretan pendek adalah persilangan, bukan impitan.
          if (run > 3) total += run;
          run = 0;
        }
      }
    }

    return total + (run > 3 ? run : 0);
  }

  function length(route: readonly Point[]): number {
    let total = 0;
    for (let index = 1; index < route.length; index++) {
      total += Math.hypot(route[index]!.x - route[index - 1]!.x, route[index]!.y - route[index - 1]!.y);
    }
    return total;
  }

  const out: Point[] = [points[0]!];

  for (let index = 1; index < points.length; index++) {
    const from = points[index - 1]!;
    const to = points[index]!;

    // Silang menyambung diagonal dengan garis lurus; siku selalu tegak-datar.
    const direct = style === 'crossing' ? [from, to] : routeChamfer([from, to], chamfer);

    // Sisi luar ruangan didahulukan saat seri: di luar hampir selalu kosong, sedangkan
    // ke arah tengah menunggu deretan lampu berikutnya.
    const outward: -1 | 1 = from.x <= centerX ? -1 : 1;
    const candidates = [
      {route: direct, bias: 0},
      {route: aroundRoute(from, to, detour, outward), bias: 0},
      {route: aroundRoute(from, to, detour, outward === -1 ? 1 : -1), bias: 0.5}
    ];

    let chosen = direct;
    let bestScore = Infinity;

    for (const candidate of candidates) {
      const score =
        hits(candidate.route) * 1e9 +
        overlap(candidate.route) * 1e4 +
        length(candidate.route) / spacing +
        candidate.bias;

      if (score < bestScore - EPSILON) {
        bestScore = score;
        chosen = candidate.route;
      }
    }

    for (let step = 1; step < chosen.length; step++) {
      occupiedPush(occupied, chosen[step - 1]!, chosen[step]!);
      push(out, chosen[step]!);
    }
  }

  return out;
}

/** `occupied` sengaja tumbuh selama perutean: satu kaki pun tidak boleh menimpa dirinya sendiri. */
function occupiedPush(occupied: readonly Segment[], a: Point, b: Point) {
  (occupied as Segment[]).push({a, b});
}

/**
 * Rute memutar: keluar 45°, menyusur lajur, masuk 45°, lalu mendatar ke tujuan.
 *
 * `side` menentukan lajurnya di kiri atau kanan. Keduanya selalu ditawarkan, dan
 * yang memilih adalah penilaian di `routeAvoiding` — sisi yang bebas dari garis
 * kaki lain yang menang. Itu yang membuat kedua kaki tidak berebut lajur yang sama.
 */
function aroundRoute(from: Point, to: Point, detour: number, side: -1 | 1): Point[] {
  const vertical = Math.abs(to.y - from.y) >= Math.abs(to.x - from.x);

  if (vertical) {
    const lane = from.x + side * detour;
    const step = Math.sign(to.y - from.y) || 1;
    const back = Math.sign(to.x - lane) || 1;

    return [
      from,
      {x: lane, y: from.y + step * detour},
      {x: lane, y: to.y - step * detour},
      // Masuk kembali 45° lalu mendatar. Tanpa dua titik ini ujungnya jadi satu
      // diagonal panjang yang memotong ruangan — bentuk yang tidak ada di gambar acuan.
      {x: lane + back * detour, y: to.y},
      to
    ];
  }

  const lane = from.y + side * detour;
  const step = Math.sign(to.x - from.x) || 1;
  const back = Math.sign(to.y - lane) || 1;

  return [
    from,
    {x: from.x + step * detour, y: lane},
    {x: to.x - step * detour, y: lane},
    {x: to.x, y: lane + back * detour},
    to
  ];
}

/**
 * Rencana wiring seluruh denah.
 *
 * Device tanpa connector listrik dibuang lebih dulu: ia tidak bisa masuk circuit,
 * jadi menariknya ke dalam garis hanya menghasilkan gambar yang menjanjikan sesuatu
 * yang tidak bisa dikerjakan.
 */
export function planWiring(
  devices: readonly Device[],
  options: WiringOptions,
  /**
   * Saklar dan sensor di lantai ini. Jumlahnya per ruangan memecah lampu ruangan itu
   * jadi sebanyak itu grouping. Kosong berarti model belum ditarik ulang oleh add-in
   * yang membacanya — batas grouping jatuh ke kerapatan lampu seperti sebelumnya.
   */
  lightingDevices: readonly LightingDevice[] = []
): WiringPlan {
  const wirable = devices.filter(isSelectable);
  const spacing = medianNearestGap(wirable.map(pointOf));

  // Kurang dari dua titik: tidak ada jarak khas yang bisa diukur, jadi tidak ada
  // baris, ruangan, maupun garis yang bisa disimpulkan. Ruangannya tetap dilaporkan
  // apa adanya supaya layar menyebut nama yang benar alih-alih "kiraan".
  if (spacing === null) {
    const only = wirable[0];
    return {
      rooms: only
        ? [
            {
              key: 'single',
              name: only.room_name?.trim() || null,
              inferred: false,
              split: false,
              switches: 0,
              devices: 1
            }
          ]
        : [],
      runs: [],
      spacing: null
    };
  }

  const switches = switchesFor(options);
  const chamfer = spacing * CHAMFER_RATIO;

  const rooms: WireRoom[] = [];
  const runs: WireRun[] = [];

  // Kumpulan awal dari nama ruangan dan kerapatan, lalu dipecah lagi menurut jumlah
  // saklar yang jatuh ke masing-masing. Dua langkah, bukan satu: saklar hanya bisa
  // dibagikan setelah ada kumpulan untuk dibandingkan.
  const initial = roomsOf(wirable, spacing);
  const counts = switchCounts(initial, lightingDevices.map((device) => ({x: device.x_mm, y: device.y_mm})));

  const grouped = initial.flatMap((room, index) => {
    const parts = splitBySwitches(room.devices, counts[index] ?? 0, spacing);
    return parts.map((devices, part) => ({
      key: parts.length > 1 ? `${room.key}#${part + 1}` : room.key,
      name: room.name,
      inferred: room.name === null,
      split: parts.length > 1,
      switches: counts[index] ?? 0,
      devices
    }));
  });

  for (const room of grouped) {
    rooms.push({
      key: room.key,
      name: room.name,
      inferred: room.inferred,
      split: room.split,
      switches: room.switches,
      devices: room.devices.length
    });

    const bands = bandsOf(room.devices, spacing * BAND_REACH);
    const grid = gridOf(bands, columnsOf(room.devices, spacing * BAND_REACH));

    // Garis yang sudah dirutekan di ruangan ini. Kaki berikutnya membacanya supaya
    // tidak memilih lajur yang sama lalu bertumpuk — dua garis yang berimpit tidak
    // bisa dibaca sebagai dua sirkuit yang berbeda.
    const occupied: Segment[] = [];

    // Titik tengah ruangan, dipakai memutuskan arah mana yang "ke luar" saat memutar.
    const centerX =
      room.devices.reduce((sum, device) => sum + device.x_mm, 0) / Math.max(1, room.devices.length);

    for (let switchIndex = 0; switchIndex < switches; switchIndex++) {
      const leg = orderLeg(grid, switchIndex, switches).map((cell) => cell.device);

      // Ruangan berisi satu lampu tidak bisa dibagi dua; kaki yang kosong dibuang
      // daripada muncul sebagai garis tanpa panjang.
      if (leg.length < 2) continue;

      const own = new Set(leg.map((device) => device.revit_unique_id));

      runs.push({
        roomKey: room.key,
        switchIndex,
        deviceIds: leg.map((device) => device.revit_unique_id),
        vertices: routeAvoiding(
          leg.map(pointOf),
          // Lampu milik kaki ini sendiri bukan penghalang — garisnya memang harus
          // menyentuhnya. Yang harus dihindari justru lampu warna satunya.
          room.devices.filter((device) => !own.has(device.revit_unique_id)).map(pointOf),
          occupied,
          spacing * DETOUR_RATIO,
          chamfer,
          options.style,
          centerX
        )
      });
    }
  }

  return {rooms, runs, spacing};
}

/**
 * Kaki yang seluruh titiknya ada di dalam sebuah pilihan.
 *
 * Sengaja **seluruhnya**, bukan sebagian. Kaki yang dipotong di tengah akan digambar di
 * Revit sebagai garis yang tidak pernah dilihat siapa pun di pratinjau — dan itu
 * melanggar satu-satunya janji berkas ini: yang tergambar di Revit adalah angka yang
 * identik dengan yang terlihat di web. Kaki yang cuma sebagian terpilih dilewati, dan
 * jumlahnya dilaporkan supaya "kenapa cuma sebagian yang terkirim" punya jawaban.
 */
export function runsWithin(runs: readonly WireRun[], selected: ReadonlySet<string>): {
  inside: WireRun[];
  partial: number;
} {
  const inside: WireRun[] = [];
  let partial = 0;

  for (const run of runs) {
    const count = run.deviceIds.filter((id) => selected.has(id)).length;
    if (count === run.deviceIds.length) inside.push(run);
    else if (count > 0) partial++;
  }

  return {inside, partial};
}

/**
 * Kaki siap kirim: titik yang sama persis dengan yang digambar pratinjau, dalam bentuk
 * yang dibaca add-in.
 */
export function toWirePayload(runs: readonly WireRun[]): WireRunPayload[] {
  return runs.map((run) => ({
    switch_index: run.switchIndex,
    vertices: run.vertices.map((vertex) => ({x_mm: vertex.x, y_mm: vertex.y}))
  }));
}
