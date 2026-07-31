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

/**
 * Satu kaki saklar, beserta di mana bentuk garisnya berganti.
 *
 * `straight` menghitung berapa titik pertama yang digambar lurus. Satu kaki memang
 * bisa berisi dua bentuk sekaligus: di gaya silang, bagian menyilangnya lurus, lalu
 * kolom sisa yang ditempelkan di ujungnya dirangkai siku — persis seperti di gambar
 * acuan, dan itu yang membuat sambungannya menyusur tepi alih-alih memotong ruangan
 * secara diagonal.
 */
type Leg = {
  devices: Device[];
  straight: number;
};

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

/**
 * Gaya silang: kolom diambil berpasangan, tiap pasangan menyilang turun.
 *
 * Untuk pasangan (kiri, kanan), kaki pertama mengambil kiri di baris genap dan kanan
 * di baris ganjil; kaki kedua kebalikannya. Itu yang menghasilkan pola X yang dipakai
 * di lapangan — nyalakan satu saklar dan tiap baris tetap dapat satu lampu,
 * berselang-seling sisi, jadi ruangan terang merata separuh.
 *
 * Semua pasangan menyumbang ke **dua kaki yang sama**, bukan dua kaki per pasangan.
 * Ruangan enam kolom tetap dua saklar, bukan enam.
 */
function crossingLegs(grid: Grid): {legs: Leg[]; leftover: Device[]} {
  const legs: Leg[] = [
    {devices: [], straight: 0},
    {devices: [], straight: 0}
  ];
  const leftover: Device[] = [];

  for (let column = 0; column + 1 < grid.columns; column += 2) {
    for (let row = 0; row < grid.rows; row++) {
      const left = grid.at(row, column);
      const right = grid.at(row, column + 1);

      // Baris genap: kaki 0 di kiri. Baris ganjil: kaki 0 pindah ke kanan.
      const first = row % 2 === 0 ? left : right;
      const second = row % 2 === 0 ? right : left;

      if (first) legs[0]!.devices.push(first);
      if (second) legs[1]!.devices.push(second);
    }
  }

  // Seluruh bagian menyilang digambar lurus; yang menyusul sesudahnya tidak.
  for (const leg of legs) leg.straight = leg.devices.length;

  // Kolom ganjil yang tidak kebagian pasangan. Dirangkai tegak dari atas ke bawah;
  // ke mana ia ditempelkan diputuskan di `attachLeftover`.
  if (grid.columns % 2 === 1) {
    const last = grid.columns - 1;
    for (let row = 0; row < grid.rows; row++) {
      const device = grid.at(row, last);
      if (device) leftover.push(device);
    }
  }

  return {legs, leftover};
}

/**
 * Menempelkan kolom sisa ke kaki yang ujungnya paling dekat.
 *
 * Sisa kolom tidak dijadikan saklar ketiga: yang diminta dua saklar, dan menambah
 * kaki hanya karena ruangannya berkolom ganjil akan mengubah jumlah saklar tanpa
 * ada yang memintanya. Arah tempelnya mengikuti ujung mana yang lebih dekat, jadi
 * garis sambungnya tidak melintasi seluruh ruangan untuk mencapai pangkalnya.
 */
function attachLeftover(legs: Leg[], leftover: readonly Device[]) {
  if (leftover.length === 0) return;

  const head = leftover[0]!;
  const tail = leftover[leftover.length - 1]!;

  let bestLeg = 0;
  let bestDistance = Infinity;
  let reversed = false;

  legs.forEach((leg, index) => {
    const end = leg.devices[leg.devices.length - 1];
    if (!end) return;

    const toHead = distance(end, head);
    const toTail = distance(end, tail);

    if (Math.min(toHead, toTail) < bestDistance) {
      bestDistance = Math.min(toHead, toTail);
      bestLeg = index;
      reversed = toTail < toHead;
    }
  });

  legs[bestLeg]!.devices.push(...(reversed ? [...leftover].reverse() : leftover));
}

function distance(a: Device, b: Device): number {
  const dx = a.x_mm - b.x_mm;
  const dy = a.y_mm - b.y_mm;
  return Math.sqrt(dx * dx + dy * dy);
}

/**
 * Gaya siku: tidak ada silang, kolom ditelusuri satu per satu.
 *
 * Kaki tetap dibagi papan catur `(baris + kolom) % jumlah saklar` — itu urusan
 * listrik dan tidak boleh berubah hanya karena gambarnya diganti. Yang berbeda cuma
 * urutannya: menurut kolom, arah naik-turun dibalik tiap kolom, jadi garisnya
 * memanjang tegak seperti di gambar acuan alih-alih menyisir mendatar.
 */
function orthogonalLegs(grid: Grid, switches: number): Leg[] {
  const legs: Leg[] = Array.from({length: switches}, () => ({devices: [], straight: 0}));

  for (let column = 0; column < grid.columns; column++) {
    const members: Device[][] = Array.from({length: switches}, () => []);

    for (let row = 0; row < grid.rows; row++) {
      const device = grid.at(row, column);
      if (device) members[(row + column) % switches]!.push(device);
    }

    members.forEach((list, leg) => {
      legs[leg]!.devices.push(...(column % 2 === 0 ? list : [...list].reverse()));
    });
  }

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

/** Seberapa jauh garis memutar ke samping saat menghindari lampu, terhadap jarak khas. */
const DETOUR_RATIO = 0.32;

/** Sedekat apa sebuah lampu dianggap terhalangi oleh garis yang lewat. */
const CLEARANCE_RATIO = 0.4;

/** Jarak titik ke ruas garis. Dipakai memutuskan apakah sebuah lampu terlewati. */
function distanceToSegment(point: Point, a: Point, b: Point): number {
  const dx = b.x - a.x;
  const dy = b.y - a.y;
  const lengthSquared = dx * dx + dy * dy;

  if (lengthSquared < EPSILON) return Math.hypot(point.x - a.x, point.y - a.y);

  const t = Math.max(0, Math.min(1, ((point.x - a.x) * dx + (point.y - a.y) * dy) / lengthSquared));
  return Math.hypot(point.x - (a.x + t * dx), point.y - (a.y + t * dy));
}

/**
 * Garis memutar ke samping saat jalur lurusnya melewati lampu milik kaki lain.
 *
 * Ini yang membedakan gaya siku di gambar acuan dari sekadar garis tegak: kaki saklar
 * dibagi papan catur, jadi dua lampu berurutan di satu kolom hampir selalu diselingi
 * lampu milik kaki sebelah. Menariknya lurus berarti garis menembus lampu yang tidak
 * ada hubungannya dengan kaki itu — benar secara topologi, tapi salah dibaca sebagai
 * gambar kerja.
 *
 * Memutarnya ke arah **luar ruangan**, bukan ke arah tengah: sisi luar hampir selalu
 * kosong, sedangkan sisi dalam berisi kolom lampu berikutnya. Belokannya 45° di kedua
 * ujung, sama seperti chamfer, jadi bentuknya menyatu dengan sisa gambar.
 */
function routeAvoiding(
  points: readonly Point[],
  obstacles: readonly Point[],
  detour: number,
  chamfer: number,
  centerX: number
): Point[] {
  if (points.length < 2) return [...points];

  const clearance = (detour / DETOUR_RATIO) * CLEARANCE_RATIO;

  /**
   * Berapa lampu yang terserempet sebuah rute. Diukur pada rute **yang benar-benar
   * digambar**, bukan pada garis lurus antar lampu: chamfer memotong sudut, dan
   * potongan itu bisa lewat dekat lampu yang garis lurusnya sendiri jauh.
   */
  function violations(route: readonly Point[]): number {
    let count = 0;
    for (let index = 1; index < route.length; index++) {
      const a = route[index - 1]!;
      const b = route[index]!;
      for (const obstacle of obstacles) {
        if (distanceToSegment(obstacle, a, b) < clearance) count++;
      }
    }
    return count;
  }

  const out: Point[] = [points[0]!];

  for (let index = 1; index < points.length; index++) {
    const from = points[index - 1]!;
    const to = points[index]!;

    const direct = routeChamfer([from, to], chamfer);

    // Memutar hanya kalau rute langsungnya memang menyerempet. Memutar tanpa sebab
    // cuma menambah belokan yang harus dibaca orang.
    const chosen = violations(direct) === 0 ? direct : aroundRoute(from, to, detour, centerX);

    for (const vertex of chosen.slice(1)) push(out, vertex);
  }

  return out;
}

/**
 * Rute memutar lewat sisi luar: keluar 45°, menyusur lajur, lalu menuju tujuan.
 *
 * Lajurnya di sisi luar ruangan — sisi dalam berisi kolom lampu berikutnya, jadi
 * memutar ke sana hanya menukar satu halangan dengan halangan lain.
 */
function aroundRoute(from: Point, to: Point, detour: number, centerX: number): Point[] {
  const vertical = Math.abs(to.y - from.y) >= Math.abs(to.x - from.x);

  if (vertical) {
    const lane = from.x + (from.x <= centerX ? -detour : detour);
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

  const lane = from.y + (from.y <= (from.y + to.y) / 2 ? -detour : detour);
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
 * Titik gambar satu kaki: bagian lurus dulu, sisanya siku.
 *
 * Yang menyilang digambar lurus. Chamfer di atas diagonal justru **menghapus** bentuk
 * silangnya — sudutnya dipotong dan X berubah jadi siku membulat. Itu yang membuat
 * percobaan pertama tidak menyerupai gambar acuan sama sekali, dan itu sebabnya
 * bentuk garis bukan satu pilihan tunggal untuk seluruh kaki.
 *
 * Sambungan antar dua bagian ikut dirutekan siku, bukan dibiarkan lurus: titik
 * terakhir bagian lurus jadi titik pertama bagian siku, jadi belokannya menyusur
 * tepi ruangan alih-alih memotong diagonal.
 */
function verticesOf(
  leg: Leg,
  shape: {chamfer: number; detour: number; obstacles: readonly Point[]; centerX: number}
): Point[] {
  const points = leg.devices.map(pointOf);
  if (leg.straight >= points.length) return points;

  // Lampu milik kaki ini sendiri bukan penghalang — garisnya memang harus menyentuhnya.
  const own = new Set(points.map((point) => `${point.x}:${point.y}`));
  const obstacles = shape.obstacles.filter((point) => !own.has(`${point.x}:${point.y}`));

  const straight = points.slice(0, leg.straight);
  const rest = routeAvoiding(
    points.slice(Math.max(0, leg.straight - 1)),
    obstacles,
    shape.detour,
    shape.chamfer,
    shape.centerX
  );

  // `rest` dimulai dari titik terakhir bagian lurus, jadi kepalanya dibuang.
  return leg.straight === 0 ? rest : [...straight, ...rest.slice(1)];
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

  const switches = switchesFor(options);
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
    const grid = gridOf(bands, columnsOf(room.devices, spacing * BAND_REACH));

    const obstacles = room.devices.map(pointOf);
    const centerX =
      obstacles.reduce((sum, point) => sum + point.x, 0) / Math.max(1, obstacles.length);
    const shape = {chamfer, detour: spacing * DETOUR_RATIO, obstacles, centerX};

    let legs: Leg[];
    if (options.style === 'crossing') {
      const crossing = crossingLegs(grid);
      attachLeftover(crossing.legs, crossing.leftover);
      legs = crossing.legs;
    } else {
      legs = orthogonalLegs(grid, switches);
    }

    legs.forEach((leg, switchIndex) => {
      // Ruangan berisi satu lampu tidak bisa dibagi dua; kaki yang kosong dibuang
      // daripada muncul sebagai garis tanpa panjang.
      if (leg.devices.length < 2) return;

      runs.push({
        roomKey: room.key,
        switchIndex,
        deviceIds: leg.devices.map((device) => device.revit_unique_id),
        vertices: verticesOf(leg, shape)
      });
    });
  }

  return {rooms, runs, spacing};
}
