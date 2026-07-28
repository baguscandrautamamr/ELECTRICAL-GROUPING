import type {DeviceStatus} from './contract';

export const SYMBOL_SHAPES = ['circle', 'square', 'diamond', 'triangle', 'pentagon', 'hexagon'] as const;
export type SymbolShape = (typeof SYMBOL_SHAPES)[number];

/**
 * FNV-1a 32 bit. Dipakai supaya simbol bawaan satu family selalu sama antar sesi
 * dan antar pengguna — kalau tidak, denah yang sama akan kelihatan berbeda tiap
 * kali dibuka, dan diskusi soal "lampu bulat di kanan" jadi tidak berarti.
 */
function hash(text: string): number {
  let value = 0x811c9dc5;
  for (let index = 0; index < text.length; index++) {
    value ^= text.charCodeAt(index);
    value = Math.imul(value, 0x01000193) >>> 0;
  }
  return value >>> 0;
}

export function isSymbolShape(value: string): value is SymbolShape {
  return (SYMBOL_SHAPES as readonly string[]).includes(value);
}

/** Override per project menang; kalau tidak ada, bentuknya dihitung dari nama family. */
export function symbolFor(familyKey: string, overrides?: Record<string, string>): SymbolShape {
  const override = overrides?.[familyKey];
  if (override && isSymbolShape(override)) return override;

  return SYMBOL_SHAPES[hash(familyKey) % SYMBOL_SHAPES.length]!;
}

export type Geometry = {kind: 'circle'; r: number} | {kind: 'polygon'; points: string};

/** Titik-titik poligon beraturan, sisi pertama menghadap ke atas. */
function regular(sides: number, r: number, rotation: number): string {
  const points: string[] = [];
  for (let index = 0; index < sides; index++) {
    const angle = rotation + (index * 2 * Math.PI) / sides;
    points.push(`${(r * Math.sin(angle)).toFixed(2)},${(-r * Math.cos(angle)).toFixed(2)}`);
  }
  return points.join(' ');
}

export function geometryFor(shape: SymbolShape, r: number): Geometry {
  switch (shape) {
    case 'circle':
      return {kind: 'circle', r};
    case 'square':
      return {kind: 'polygon', points: regular(4, r * 1.15, Math.PI / 4)};
    case 'diamond':
      return {kind: 'polygon', points: regular(4, r * 1.25, 0)};
    case 'triangle':
      return {kind: 'polygon', points: regular(3, r * 1.3, 0)};
    case 'pentagon':
      return {kind: 'polygon', points: regular(5, r * 1.15, 0)};
    case 'hexagon':
      return {kind: 'polygon', points: regular(6, r * 1.1, 0)};
  }
}

/**
 * Warna saja tidak cukup: setiap status juga punya garis atau isian yang berbeda,
 * supaya terbaca oleh pengguna dengan buta warna dan saat dicetak hitam putih.
 */
export type StatusStyle = {
  color: string;
  fill: 'solid' | 'none' | 'muted';
  dash?: string;
  /** Garis miring menembus simbol — penanda "tidak bisa dipilih". */
  struck?: boolean;
  /** Titik kecil di tengah — penanda "sudah di circuit, panel belum". */
  pip?: boolean;
};

export const STATUS_STYLE: Record<DeviceStatus, StatusStyle> = {
  unwired: {color: 'var(--danger)', fill: 'none', dash: '3 2'},
  no_panel: {color: 'var(--warn)', fill: 'none', pip: true},
  connected: {color: 'var(--ok)', fill: 'solid'},
  // Tanpa dash: garis putus-putus rapat ditambah garis miring terbaca sebagai
  // coretan, bukan sebagai simbol.
  no_connector: {color: 'var(--ink-muted)', fill: 'muted', struck: true}
};
