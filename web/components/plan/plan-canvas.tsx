'use client';

import {useMemo, useRef, useState} from 'react';
import type {Device} from '@/lib/contract';
import {isSelectable} from '@/lib/contract';
import {STATUS_STYLE, geometryFor, symbolFor} from '@/lib/symbols';

type Bounds = {minX: number; minY: number; width: number; height: number};

/** Bagian layout yang dipakai kanvas. Null di keempatnya berarti crop tidak aktif di Revit. */
type Crop = {
  crop_min_x_mm: number | null;
  crop_min_y_mm: number | null;
  crop_max_x_mm: number | null;
  crop_max_y_mm: number | null;
};

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
      return {
        bounds: region,
        radius: Math.max(Math.max(region.width, region.height) / 90, 60),
        place: (device: Device) => ({x: device.x_mm, y: flipped - device.y_mm})
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
    const radius = Math.max(Math.max(bounds.width, bounds.height) / 90, 60);

    return {
      bounds,
      radius,
      place: (device: Device) => ({x: device.x_mm, y: flip - device.y_mm})
    };
  }, [devices, crop]);
}

export function PlanCanvas({
  devices,
  selected,
  onSelect,
  symbolOverrides,
  highlighted,
  crop
}: {
  devices: Device[];
  selected: ReadonlySet<string>;
  onSelect: (ids: string[], mode: 'replace' | 'toggle' | 'add') => void;
  symbolOverrides: Record<string, string>;
  /** Device milik circuit yang sedang disorot di daftar samping. */
  highlighted?: ReadonlySet<string>;
  /** Crop region view Revit; tanpa ini kanvas jatuh ke kotak pembatas device. */
  crop?: Crop;
}) {
  const {bounds, radius, place} = useLayout(devices, crop);
  const svgRef = useRef<SVGSVGElement | null>(null);
  const [marquee, setMarquee] = useState<{x1: number; y1: number; x2: number; y2: number} | null>(null);
  const additive = useRef(false);

  function toUser(event: React.PointerEvent) {
    const svg = svgRef.current;
    if (!svg) return {x: 0, y: 0};

    // Rasio kotak SVG dibuat sama dengan rasio viewBox lewat aspect-ratio di CSS,
    // jadi pemetaan ini linear tanpa perlu memikirkan letterbox.
    const rect = svg.getBoundingClientRect();
    return {
      x: bounds.minX + ((event.clientX - rect.left) / rect.width) * bounds.width,
      y: bounds.minY + ((event.clientY - rect.top) / rect.height) * bounds.height
    };
  }

  function startMarquee(event: React.PointerEvent<SVGSVGElement>) {
    if (event.button !== 0) return;

    const point = toUser(event);
    additive.current = event.shiftKey || event.metaKey || event.ctrlKey;
    setMarquee({x1: point.x, y1: point.y, x2: point.x, y2: point.y});
    event.currentTarget.setPointerCapture(event.pointerId);
  }

  function moveMarquee(event: React.PointerEvent<SVGSVGElement>) {
    if (!marquee) return;
    const point = toUser(event);
    setMarquee({...marquee, x2: point.x, y2: point.y});
  }

  function endMarquee(event: React.PointerEvent<SVGSVGElement>) {
    if (!marquee) return;
    event.currentTarget.releasePointerCapture(event.pointerId);

    const left = Math.min(marquee.x1, marquee.x2);
    const right = Math.max(marquee.x1, marquee.x2);
    const top = Math.min(marquee.y1, marquee.y2);
    const bottom = Math.max(marquee.y1, marquee.y2);

    // Tarikan sangat kecil dianggap klik di area kosong: mengosongkan pilihan.
    const tiny = right - left < radius && bottom - top < radius;
    setMarquee(null);

    if (tiny) {
      if (!additive.current) onSelect([], 'replace');
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

  return (
    <svg
      ref={svgRef}
      viewBox={`${bounds.minX} ${bounds.minY} ${bounds.width} ${bounds.height}`}
      style={{aspectRatio: `${bounds.width} / ${bounds.height}`}}
      className="w-full touch-none rounded-[var(--radius-card)] border border-hairline bg-surface select-none"
      onPointerDown={startMarquee}
      onPointerMove={moveMarquee}
      onPointerUp={endMarquee}
      onPointerCancel={() => setMarquee(null)}
    >
      <defs>
        <pattern
          id="plan-grid"
          width={radius * 10}
          height={radius * 10}
          patternUnits="userSpaceOnUse"
        >
          <path
            d={`M ${radius * 10} 0 L 0 0 0 ${radius * 10}`}
            fill="none"
            stroke="var(--hairline)"
            strokeWidth={radius / 12}
          />
        </pattern>
      </defs>

      <rect
        x={bounds.minX}
        y={bounds.minY}
        width={bounds.width}
        height={bounds.height}
        fill="url(#plan-grid)"
      />

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
            className={selectable ? 'cursor-pointer' : 'pointer-events-none'}
            onPointerDown={(event) => {
              if (!selectable) return;
              // Menahan event supaya klik pada titik tidak ikut memulai marquee.
              event.stopPropagation();
            }}
            onClick={(event) => {
              if (!selectable) return;
              event.stopPropagation();
              onSelect([device.revit_unique_id], event.shiftKey ? 'add' : 'toggle');
            }}
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
              <circle
                r={radius * 2.1}
                fill="none"
                stroke="var(--accent)"
                strokeWidth={radius / 4}
              />
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
              <line
                x1={-radius}
                y1={radius}
                x2={radius}
                y2={-radius}
                stroke={stroke}
                strokeWidth={radius / 4}
              />
            ) : null}

            {device.circuit_number ? (
              <text
                x={radius * 1.6}
                y={-radius * 1.1}
                fontSize={radius * 1.7}
                fill="var(--ink-muted)"
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
          strokeWidth={radius / 5}
        />
      ) : null}
    </svg>
  );
}

/** strokeDasharray ada di satuan user, jadi harus ikut skala model. */
function scaleDash(dash: string, radius: number): string {
  return dash
    .split(' ')
    .map((part) => (Number(part) * radius) / 4)
    .join(' ');
}
