/**
 * Perutean garis circuit di denah.
 *
 * Dipisah dari komponen supaya bisa dibaca sebagai fungsi murni: titik masuk,
 * string path keluar, tanpa React dan tanpa SVG.
 *
 * Aturan bentuknya: tiap circuit punya warna dan pola garisnya sendiri, dan
 * garisnya **memutar** saat harus melewati device milik circuit lain. Dua device
 * sewarna sering terpisah satu device milik saklar lain, jadi garis yang lurus
 * akan menembus device itu dan dua circuit jadi tampak berimpit.
 */

export type Point = {x: number; y: number};

export type SeriesStyle = {
  /** Token CSS, bukan hex — supaya mode gelap ikut bekerja. */
  color: string;
  /** Pola garis dalam satuan tebal garis; null berarti utuh. */
  dash: readonly number[] | null;
  /**
   * Sisi tempat garis memutar. Dibedakan per circuit supaya dua garis yang
   * melompati device yang sama tidak menempuh jalur yang sama.
   */
  side: 1 | -1;
};

/**
 * Warna sekaligus pola garis: dicetak hitam putih atau dilihat pengguna dengan
 * buta warna, polanya yang membedakan — sama seperti simbol status device.
 */
export const SERIES_STYLES: readonly SeriesStyle[] = [
  {color: 'var(--series-1)', dash: null, side: 1},
  {color: 'var(--series-2)', dash: [5, 3], side: -1},
  {color: 'var(--series-3)', dash: [1.6, 2.4], side: 1},
  {color: 'var(--series-4)', dash: [7, 3, 1.6, 3], side: -1},
  {color: 'var(--series-5)', dash: [3, 2.2], side: 1},
  {color: 'var(--series-6)', dash: [9, 4], side: -1}
];

export function seriesFor(index: number): SeriesStyle {
  const count = SERIES_STYLES.length;
  return SERIES_STYLES[((index % count) + count) % count]!;
}

/**
 * Urutan kunjungan: jalur dirangkai pita demi pita, arahnya bergantian, supaya
 * jalurnya pendek dan zigzagnya terbaca. Urutan pilihan user tidak dipakai — itu
 * urutan klik, dan garis akan melompat maju-mundur tanpa alasan yang terlihat.
 *
 * Pitanya mengikuti sisi panjang kelompok: kelompok yang menjulur ke bawah
 * dirangkai kolom demi kolom, yang melebar dirangkai baris demi baris. Kalau
 * selalu memakai baris, kelompok yang tinggi menghasilkan diagonal panjang yang
 * melintasi denah dan menutupi garis circuit lain.
 */
export function orderForRouting<T extends Point>(points: readonly T[], band: number): T[] {
  if (points.length < 3) return [...points];

  const xs = points.map((point) => point.x);
  const ys = points.map((point) => point.y);
  const alongRows = Math.max(...xs) - Math.min(...xs) >= Math.max(...ys) - Math.min(...ys);

  const across = (point: Point) => (alongRows ? point.y : point.x);
  const within = (point: Point) => (alongRows ? point.x : point.y);

  const sorted = [...points].sort((a, b) => across(a) - across(b) || within(a) - within(b));

  const bands: T[][] = [];
  for (const point of sorted) {
    const current = bands.at(-1);
    if (current && Math.abs(across(point) - across(current[0]!)) <= band) current.push(point);
    else bands.push([point]);
  }

  return bands.flatMap((items, index) => {
    const ordered = [...items].sort((a, b) => within(a) - within(b));
    return index % 2 === 0 ? ordered : ordered.reverse();
  });
}

/**
 * Menyisipkan titik belok supaya garis melompati <c>obstacles</c>, bukan
 * menembusnya. Satu titik belok per device yang menghalangi, ditempatkan di
 * proyeksi device itu pada segmen lalu digeser tegak lurus ke sisi milik circuit.
 */
export function routeThrough(
  stops: readonly Point[],
  obstacles: readonly Point[],
  clearance: number,
  side: 1 | -1
): Point[] {
  if (stops.length < 2) return [...stops];

  const path: Point[] = [stops[0]!];

  for (let index = 1; index < stops.length; index++) {
    const from = stops[index - 1]!;
    const to = stops[index]!;

    const dx = to.x - from.x;
    const dy = to.y - from.y;
    const length = Math.hypot(dx, dy);

    if (length > 0) {
      const ux = dx / length;
      const uy = dy / length;
      const nx = -uy * side;
      const ny = ux * side;

      const blocking = obstacles
        .map((point) => {
          const ox = point.x - from.x;
          const oy = point.y - from.y;
          return {
            along: (ox * ux + oy * uy) / length,
            gap: Math.abs(ox * -uy + oy * ux)
          };
        })
        // Ujung segmen adalah device milik circuit ini sendiri; hanya yang benar-benar
        // di tengah jalan yang perlu diputari.
        .filter((hit) => hit.along > 0.02 && hit.along < 0.98 && hit.gap < clearance)
        .sort((a, b) => a.along - b.along);

      for (const hit of blocking) {
        const at = length * hit.along;
        path.push({
          x: from.x + ux * at + nx * clearance * 1.8,
          y: from.y + uy * at + ny * clearance * 1.8
        });
      }
    }

    path.push(to);
  }

  return path;
}

/**
 * Polyline dengan sudut membulat. Sudut tajam pada titik belok membuat garis
 * tampak seperti patah karena kesalahan, bukan seperti jalur yang memutar.
 */
export function roundedPath(points: readonly Point[], corner: number): string {
  if (points.length === 0) return '';

  const first = points[0]!;
  if (points.length === 1) return `M ${fmt(first)}`;

  let d = `M ${fmt(first)}`;

  for (let index = 1; index < points.length - 1; index++) {
    const previous = points[index - 1]!;
    const current = points[index]!;
    const next = points[index + 1]!;

    const inLength = Math.hypot(current.x - previous.x, current.y - previous.y);
    const outLength = Math.hypot(next.x - current.x, next.y - current.y);
    const cut = Math.min(corner, inLength / 2, outLength / 2);

    if (cut <= 0) continue;

    d += ` L ${fmt(toward(current, previous, cut))} Q ${fmt(current)} ${fmt(toward(current, next, cut))}`;
  }

  return `${d} L ${fmt(points.at(-1)!)}`;
}

function toward(from: Point, to: Point, distance: number): Point {
  const length = Math.hypot(to.x - from.x, to.y - from.y);
  if (length === 0) return from;
  return {
    x: from.x + ((to.x - from.x) / length) * distance,
    y: from.y + ((to.y - from.y) / length) * distance
  };
}

function fmt(point: Point): string {
  return `${point.x.toFixed(1)} ${point.y.toFixed(1)}`;
}
