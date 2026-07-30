'use client';

import {Maximize2, Minus, Plus} from 'lucide-react';
import {useTranslations} from 'next-intl';
import {useCallback, useEffect, useMemo, useRef, useState} from 'react';
import type {Device} from '@/lib/contract';
import {isSelectable} from '@/lib/contract';
import {STATUS_STYLE, geometryFor, symbolFor} from '@/lib/symbols';
import type {WireRun} from '@/lib/wiring';

type Bounds = {minX: number; minY: number; width: number; height: number};

/** Bagian layout yang dipakai kanvas. Null di keempatnya berarti crop tidak aktif di Revit. */
type Crop = {
  crop_min_x_mm: number | null;
  crop_min_y_mm: number | null;
  crop_max_x_mm: number | null;
  crop_max_y_mm: number | null;
};

/** Sedekat apa pun titik berdesakan, kanvas tidak diperbesar lebih dari ini. */
const MAX_ZOOM = 40;
const ZOOM_STEP = 1.25;

function cropBounds(crop?: Crop): Bounds | null {
  if (!crop) return null;

  const {crop_min_x_mm: minX, crop_min_y_mm: minY, crop_max_x_mm: maxX, crop_max_y_mm: maxY} = crop;
  if (minX === null || minY === null || maxX === null || maxY === null) return null;

  const width = maxX - minX;
  const height = maxY - minY;

  // Crop yang tak masuk akal lebih baik diabaikan daripada menghasilkan viewBox
  // kosong yang membuat denah hilang sama sekali.
  if (!(width > 0) || !(height > 0)) return null;

  return {minX, minY, width, height};
}

/** Simbol tidak pernah menyusut lebih dari ini terhadap ukuran dasarnya. */
const SMALLEST = 0.55;

/**
 * Jari-jari simbol yang disesuaikan dengan kerapatan titik.
 *
 * Ukuran tetap berdasarkan besar denah membuat simbol saling menindih justru di
 * tempat yang paling perlu dibaca: deretan titik yang berjarak rapat.
 *
 * Yang dipakai adalah **median** jarak ke tetangga terdekat. Percobaan pertama
 * memakai persentil ke-20 dan hasilnya terlalu kecil untuk dilihat: persentil bawah
 * didominasi pasangan yang hampir berimpit, jadi beberapa titik yang menempel
 * membuat seluruh denah ikut menciut. Median tidak peduli pada ekor itu.
 *
 * Batas bawahnya tegas. Simbol yang terbaca lebih penting daripada simbol yang
 * dijamin tidak bersinggungan — tumpang tindih sudah ditangani zoom dan klik
 * berulang, sedangkan titik sebesar debu tidak ada obatnya.
 */
function densityRadius(devices: Device[], fallback: number): number {
  if (devices.length < 2) return fallback;

  // Membandingkan semua pasangan tidak perlu: contoh beberapa ratus titik sudah
  // menggambarkan kerapatannya, dan biayanya tetap terikat pada denah sebesar apa pun.
  const step = Math.max(1, Math.ceil(devices.length / 300));
  const gaps: number[] = [];

  for (let i = 0; i < devices.length; i += step) {
    const a = devices[i]!;
    let best = Infinity;

    for (const b of devices) {
      if (b === a) continue;
      const dx = a.x_mm - b.x_mm;
      const dy = a.y_mm - b.y_mm;
      const squared = dx * dx + dy * dy;
      if (squared > 0 && squared < best) best = squared;
    }

    if (best < Infinity) gaps.push(Math.sqrt(best));
  }

  if (gaps.length === 0) return fallback;

  gaps.sort((a, b) => a - b);
  const median = gaps[Math.floor(gaps.length / 2)] ?? fallback;

  return Math.min(Math.max(median * 0.42, fallback * SMALLEST), fallback);
}

/**
 * Koordinat model dalam milimeter, Y ke atas. SVG memakai Y ke bawah, jadi Y
 * dicerminkan di sini — bukan lewat transform scale(1,-1), yang juga akan
 * mencerminkan teks dan simbol.
 */
function useLayout(devices: Device[], crop?: Crop) {
  return useMemo(() => {
    const xs = devices.map((device) => device.x_mm);
    const ys = devices.map((device) => device.y_mm);

    const minX = Math.min(...xs);
    const maxX = Math.max(...xs);
    const minY = Math.min(...ys);
    const maxY = Math.max(...ys);

    /**
     * Crop region view Revit, kalau view-nya memang punya. Memakainya membuat
     * denah di web membingkai area yang sama dengan sheet — proporsi dan posisi
     * titik cocok dengan cetakan, bukan sekadar pas-pasan mengikuti sebaran titik.
     */
    const region = cropBounds(crop);
    if (region) {
      const flipped = region.minY + (region.minY + region.height);
      const flipY = (y: number) => flipped - y;
      return {
        bounds: region,
        radius: densityRadius(devices, Math.max(Math.max(region.width, region.height) / 90, 60)),
        flipY,
        place: (device: Device) => ({x: device.x_mm, y: flipY(device.y_mm)})
      };
    }

    // Satu titik, atau semua titik sebaris, akan memberi lebar nol.
    const spanX = Math.max(maxX - minX, 1000);
    const spanY = Math.max(maxY - minY, 1000);
    const pad = Math.max(spanX, spanY) * 0.08;

    const bounds: Bounds = {
      minX: minX - pad,
      minY: minY - pad,
      width: spanX + pad * 2,
      height: spanY + pad * 2
    };

    const flip = minY + maxY;
    const flipY = (y: number) => flip - y;

    return {
      bounds,
      radius: densityRadius(devices, Math.max(Math.max(bounds.width, bounds.height) / 90, 60)),
      flipY,
      place: (device: Device) => ({x: device.x_mm, y: flipY(device.y_mm)})
    };
  }, [devices, crop]);
}

export function PlanCanvas({
  devices,
  selected,
  onSelect,
  symbolOverrides,
  highlighted,
  wiring,
  crop
}: {
  devices: Device[];
  selected: ReadonlySet<string>;
  onSelect: (ids: string[], mode: 'replace' | 'toggle' | 'add') => void;
  symbolOverrides: Record<string, string>;
  /** Device milik circuit yang sedang disorot di daftar samping. */
  highlighted?: ReadonlySet<string>;
  /**
   * Pratinjau wiring. Tanpa prop ini kanvas menggambar persis seperti sebelumnya —
   * seluruh perilaku seleksi, marquee, dan zoom tidak menyentuhnya sama sekali.
   */
  wiring?: WireRun[];
  /** Crop region view Revit; tanpa ini kanvas jatuh ke kotak pembatas device. */
  crop?: Crop;
}) {
  const t = useTranslations('plan');
  const {bounds, radius, flipY, place} = useLayout(devices, crop);
  const svgRef = useRef<SVGSVGElement | null>(null);
  const [marquee, setMarquee] = useState<{x1: number; y1: number; x2: number; y2: number} | null>(null);
  const additive = useRef(false);

  /** Null berarti seluruh denah termuat. Selalu sebangun dengan `bounds`. */
  const [view, setView] = useState<Bounds | null>(null);
  const panning = useRef<{x: number; y: number} | null>(null);

  /**
   * Klik berulang di titik yang sama menelusuri device yang bertumpuk, satu per
   * sekali klik. Zoom memisahkan titik yang berdekatan, tapi dua device di koordinat
   * yang benar-benar sama tidak akan pernah terpisah seberapa pun dekatnya.
   */
  const cycle = useRef<{x: number; y: number; index: number} | null>(null);

  // Ganti denah berarti ganti kerangka acuan; zoom lama tidak berarti apa-apa di sana.
  // Disetel saat render, bukan lewat effect: effect baru berjalan setelah satu kali
  // gambar, dan gambar itu memakai viewBox milik denah yang sudah tidak ada.
  const [framedBy, setFramedBy] = useState(bounds);
  if (framedBy !== bounds) {
    setFramedBy(bounds);
    setView(null);
  }

  const current = (framedBy === bounds ? view : null) ?? bounds;

  const zoomAt = useCallback(
    (fx: number, fy: number, factor: number) => {
      setView((previous) => {
        const from = previous ?? bounds;
        const width = Math.min(Math.max(from.width * factor, bounds.width / MAX_ZOOM), bounds.width);

        // Tinggi mengikuti lebar supaya rasio kotak tidak pernah berubah — itu yang
        // membuat pemetaan layar ke koordinat model tetap linear.
        const height = width * (bounds.height / bounds.width);
        if (width >= bounds.width) return null;

        return {
          // Titik di bawah kursor tetap di tempatnya.
          minX: from.minX + fx * (from.width - width),
          minY: from.minY + fy * (from.height - height),
          width,
          height
        };
      });
    },
    [bounds]
  );

  // Listener wheel dipasang sendiri karena React memasangnya sebagai passive, dan
  // listener passive tidak boleh memanggil preventDefault — tanpa itu halaman ikut
  // ter-scroll setiap kali denah di-zoom.
  useEffect(() => {
    const svg = svgRef.current;
    if (!svg) return;

    function onWheel(event: WheelEvent) {
      event.preventDefault();
      const rect = (event.currentTarget as SVGSVGElement).getBoundingClientRect();
      zoomAt(
        (event.clientX - rect.left) / rect.width,
        (event.clientY - rect.top) / rect.height,
        event.deltaY > 0 ? ZOOM_STEP : 1 / ZOOM_STEP
      );
    }

    svg.addEventListener('wheel', onWheel, {passive: false});
    return () => svg.removeEventListener('wheel', onWheel);
  }, [zoomAt]);

  /**
   * Berapa milimeter model yang ditempati satu piksel layar.
   *
   * Ambang klik dan besar sasaran harus diukur di layar, bukan di model: jari-jari
   * simbol mengecil saat denah dilihat utuh, dan ambang yang ikut mengecil membuat
   * getaran tangan beberapa piksel terbaca sebagai tarikan seleksi.
   */
  function userPerPixel() {
    const rect = svgRef.current?.getBoundingClientRect();
    return rect && rect.width > 0 ? current.width / rect.width : 1;
  }

  function toUser(event: {clientX: number; clientY: number}) {
    const svg = svgRef.current;
    if (!svg) return {x: 0, y: 0};

    // Rasio kotak SVG dibuat sama dengan rasio viewBox lewat aspect-ratio di CSS,
    // jadi pemetaan ini linear tanpa perlu memikirkan letterbox.
    const rect = svg.getBoundingClientRect();
    return {
      x: current.minX + ((event.clientX - rect.left) / rect.width) * current.width,
      y: current.minY + ((event.clientY - rect.top) / rect.height) * current.height
    };
  }

  function down(event: React.PointerEvent<SVGSVGElement>) {
    // Tombol tengah menggeser denah, sama seperti di Revit.
    if (event.button === 1) {
      event.preventDefault();
      panning.current = {x: event.clientX, y: event.clientY};
      event.currentTarget.setPointerCapture(event.pointerId);
      return;
    }

    if (event.button !== 0) return;

    const point = toUser(event);
    additive.current = event.shiftKey || event.metaKey || event.ctrlKey;
    setMarquee({x1: point.x, y1: point.y, x2: point.x, y2: point.y});
    event.currentTarget.setPointerCapture(event.pointerId);
  }

  function move(event: React.PointerEvent<SVGSVGElement>) {
    if (panning.current) {
      const svg = svgRef.current;
      if (!svg) return;

      const rect = svg.getBoundingClientRect();
      const dx = ((event.clientX - panning.current.x) / rect.width) * current.width;
      const dy = ((event.clientY - panning.current.y) / rect.height) * current.height;
      panning.current = {x: event.clientX, y: event.clientY};

      setView({...current, minX: current.minX - dx, minY: current.minY - dy});
      return;
    }

    if (!marquee) return;
    const point = toUser(event);
    setMarquee({...marquee, x2: point.x, y2: point.y});
  }

  function up(event: React.PointerEvent<SVGSVGElement>) {
    if (panning.current) {
      panning.current = null;
      event.currentTarget.releasePointerCapture(event.pointerId);
      return;
    }

    if (!marquee) return;
    event.currentTarget.releasePointerCapture(event.pointerId);

    const left = Math.min(marquee.x1, marquee.x2);
    const right = Math.max(marquee.x1, marquee.x2);
    const top = Math.min(marquee.y1, marquee.y2);
    const bottom = Math.max(marquee.y1, marquee.y2);

    setMarquee(null);

    // Tarikan sepanjang beberapa piksel dianggap klik, bukan pilihan area.
    const slack = userPerPixel() * 4;
    if (right - left < slack && bottom - top < slack) {
      click({x: (left + right) / 2, y: (top + bottom) / 2});
      return;
    }

    const inside = devices
      .filter(isSelectable)
      .filter((device) => {
        const point = place(device);
        return point.x >= left && point.x <= right && point.y >= top && point.y <= bottom;
      })
      .map((device) => device.revit_unique_id);

    onSelect(inside, additive.current ? 'add' : 'replace');
  }

  /**
   * Semua device yang tersentuh klik, terdekat lebih dulu. Sasaran klik sengaja
   * lebih besar dari simbolnya: yang dikejar user adalah titiknya, bukan garisnya.
   */
  function click(at: {x: number; y: number}) {
    // Sasaran tidak pernah lebih kecil dari sepetak jempol, seberapa pun jauh
    // denah dilihat.
    const reach = Math.max(radius * 1.8, userPerPixel() * 11);
    const hits = devices
      .filter(isSelectable)
      .map((device) => {
        const point = place(device);
        return {device, distance: Math.hypot(point.x - at.x, point.y - at.y)};
      })
      .filter((hit) => hit.distance <= reach)
      .sort((a, b) => a.distance - b.distance);

    if (hits.length === 0) {
      cycle.current = null;
      if (!additive.current) onSelect([], 'replace');
      return;
    }

    // Klik lagi di tempat yang sama berarti "bukan yang itu, yang di bawahnya".
    const again =
      cycle.current !== null && Math.hypot(cycle.current.x - at.x, cycle.current.y - at.y) <= reach / 2;

    const index = again ? (cycle.current!.index + 1) % hits.length : 0;
    cycle.current = {x: at.x, y: at.y, index};

    onSelect([hits[index]!.device.revit_unique_id], additive.current ? 'add' : 'toggle');
  }

  const zoomed = current !== bounds;

  return (
    <div className="relative">
      <svg
        ref={svgRef}
        viewBox={`${current.minX} ${current.minY} ${current.width} ${current.height}`}
        style={{aspectRatio: `${bounds.width} / ${bounds.height}`}}
        className="w-full touch-none rounded-[var(--radius-card)] border border-hairline bg-surface select-none"
        onPointerDown={down}
        onPointerMove={move}
        onPointerUp={up}
        onPointerCancel={() => {
          panning.current = null;
          setMarquee(null);
        }}
        onAuxClick={(event) => event.preventDefault()}
      >
        <defs>
          <pattern id="plan-grid" width={bounds.width / 24} height={bounds.width / 24} patternUnits="userSpaceOnUse">
            <path
              d={`M ${bounds.width / 24} 0 L 0 0 0 ${bounds.width / 24}`}
              fill="none"
              stroke="var(--hairline)"
              strokeWidth={bounds.width / 2400}
            />
          </pattern>
        </defs>

        <rect
          x={current.minX}
          y={current.minY}
          width={current.width}
          height={current.height}
          fill="url(#plan-grid)"
        />

        {/*
          Pratinjau wiring, digambar sebelum simbol supaya garis lewat di bawah titik.
          `pointerEvents: none` bukan hiasan: klik ditangani di tingkat SVG supaya
          device yang bertumpuk bisa ditelusuri bergantian, dan elemen baru yang
          menerima pointer akan menelan klik itu — simbol device pun memakainya.
        */}
        {wiring && wiring.length > 0 ? (
          <g style={{pointerEvents: 'none'}}>
            {wiring.map((run) => (
              <polyline
                key={`${run.roomKey}:${run.switchIndex}`}
                points={run.vertices.map((vertex) => `${vertex.x},${flipY(vertex.y)}`).join(' ')}
                fill="none"
                stroke="var(--accent)"
                strokeWidth={radius / 3}
                strokeLinejoin="round"
                strokeLinecap="round"
                // Kaki saklar dibedakan garis, bukan warna kedua. Warna sudah dipakai
                // status device, dan garis putus-putus tetap terbaca saat denah
                // dicetak hitam putih.
                strokeDasharray={run.switchIndex % 2 === 1 ? scaleDash('4 3', radius) : undefined}
                opacity={0.85}
              />
            ))}
          </g>
        ) : null}

        {devices.map((device) => {
          const point = place(device);
          const style = STATUS_STYLE[device.status];
          const shape = symbolFor(device.family_key, symbolOverrides);
          const geometry = geometryFor(shape, radius);
          const chosen = selected.has(device.revit_unique_id);
          const selectable = isSelectable(device);
          const stroke = style.color;
          const ringed = highlighted?.has(device.revit_unique_id) === true;

          const fill =
            style.fill === 'solid'
              ? stroke
              : style.fill === 'muted'
                ? 'color-mix(in oklab, var(--ink-muted) 20%, transparent)'
                : 'var(--surface)';

          return (
            <g
              key={device.revit_unique_id}
              transform={`translate(${point.x} ${point.y})`}
              role={selectable ? 'button' : undefined}
              tabIndex={selectable ? 0 : undefined}
              aria-pressed={selectable ? chosen : undefined}
              aria-label={`${device.family_key} · ${device.status}${device.circuit_number ? ` · ${device.circuit_number}` : ''}`}
              // Klik ditangani di tingkat SVG supaya device yang bertumpuk bisa
              // ditelusuri bergantian; di sini cukup keyboard dan bentuk kursor.
              className={selectable ? 'cursor-pointer' : undefined}
              style={{pointerEvents: 'none'}}
              onKeyDown={(event) => {
                if (!selectable) return;
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault();
                  onSelect([device.revit_unique_id], 'toggle');
                }
              }}
            >
              {chosen ? (
                <circle r={radius * 1.85} fill="color-mix(in oklab, var(--accent) 22%, transparent)" />
              ) : null}

              {ringed ? (
                <circle r={radius * 2.1} fill="none" stroke="var(--accent)" strokeWidth={radius / 4} />
              ) : null}

              {geometry.kind === 'circle' ? (
                <circle
                  r={geometry.r}
                  fill={fill}
                  stroke={stroke}
                  strokeWidth={radius / 3.5}
                  strokeDasharray={style.dash ? scaleDash(style.dash, radius) : undefined}
                />
              ) : (
                <polygon
                  points={geometry.points}
                  fill={fill}
                  stroke={stroke}
                  strokeWidth={radius / 3.5}
                  strokeDasharray={style.dash ? scaleDash(style.dash, radius) : undefined}
                />
              )}

              {style.pip ? <circle r={radius / 3} fill={stroke} /> : null}

              {style.struck ? (
                <line x1={-radius} y1={radius} x2={radius} y2={-radius} stroke={stroke} strokeWidth={radius / 4} />
              ) : null}

              {/* Nomor circuit dari Revit. Digariskan dengan warna latar lebih dulu
                  (paint-order: stroke) supaya tetap terbaca saat jatuh di atas garis
                  grid atau simbol tetangga. */}
              {device.circuit_number ? (
                <text
                  x={radius * 1.5}
                  y={-radius * 1.2}
                  fontSize={radius * 1.8}
                  fontWeight={600}
                  fill="var(--ink-muted)"
                  stroke="var(--surface)"
                  strokeWidth={radius / 3}
                  paintOrder="stroke"
                  className="pointer-events-none"
                >
                  {device.circuit_number}
                </text>
              ) : null}
            </g>
          );
        })}

        {marquee ? (
          <rect
            x={Math.min(marquee.x1, marquee.x2)}
            y={Math.min(marquee.y1, marquee.y2)}
            width={Math.abs(marquee.x2 - marquee.x1)}
            height={Math.abs(marquee.y2 - marquee.y1)}
            fill="color-mix(in oklab, var(--accent) 12%, transparent)"
            stroke="var(--accent)"
            strokeWidth={current.width / 400}
          />
        ) : null}
      </svg>

      <div className="absolute top-2 right-2 flex flex-col gap-1">
        <Zoom label={t('zoomIn')} onClick={() => zoomAt(0.5, 0.5, 1 / ZOOM_STEP)}>
          <Plus className="size-4" aria-hidden />
        </Zoom>
        <Zoom label={t('zoomOut')} onClick={() => zoomAt(0.5, 0.5, ZOOM_STEP)}>
          <Minus className="size-4" aria-hidden />
        </Zoom>
        <Zoom label={t('zoomFit')} onClick={() => setView(null)} disabled={!zoomed}>
          <Maximize2 className="size-4" aria-hidden />
        </Zoom>
      </div>
    </div>
  );
}

function Zoom({
  label,
  onClick,
  disabled,
  children
}: {
  label: string;
  onClick: () => void;
  disabled?: boolean;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={onClick}
      disabled={disabled}
      className="rounded-[var(--radius-control)] border border-hairline bg-surface/90 p-1.5 text-muted transition-colors duration-200 hover:text-ink disabled:pointer-events-none disabled:opacity-30"
    >
      {children}
    </button>
  );
}

/** strokeDasharray ada di satuan user, jadi harus ikut skala model. */
function scaleDash(dash: string, radius: number): string {
  return dash
    .split(' ')
    .map((part) => (Number(part) * radius) / 4)
    .join(' ');
}
