'use client';

import {useMemo, useRef, useState} from 'react';
import type {Circuit, Device} from '@/lib/contract';
import {isSelectable} from '@/lib/contract';
import {orderForRouting, roundedPath, routeThrough, seriesFor} from '@/lib/routing';
import {STATUS_STYLE, geometryFor, symbolFor} from '@/lib/symbols';

type Bounds = {minX: number; minY: number; width: number; height: number};

/**
 * Koordinat model dalam milimeter, Y ke atas. SVG memakai Y ke bawah, jadi Y
 * dicerminkan di sini — bukan lewat transform scale(1,-1), yang juga akan
 * mencerminkan teks dan simbol.
 */
function useLayout(devices: Device[]) {
  return useMemo(() => {
    const xs = devices.map((device) => device.x_mm);
    const ys = devices.map((device) => device.y_mm);

    const minX = Math.min(...xs);
    const maxX = Math.max(...xs);
    const minY = Math.min(...ys);
    const maxY = Math.max(...ys);

    // Satu titik, atau semua titik sebaris, akan memberi lebar nol.
    const spanX = Math.max(maxX - minX, 1000);
    const spanY = Math.max(maxY - minY, 1000);
    const reach = Math.max(spanX, spanY);

    // Sama dengan `max(bounds) / 90` — ditulis dari `reach` supaya padding di bawah
    // bisa ikut menimbang radius, yang butuh dihitung lebih dulu.
    const radius = Math.max((reach * 1.16) / 90, 60);

    // Padding juga harus menampung lengkung terluar garis circuit: pada denah yang
    // sangat rapat, garis yang memutar di tepi akan terpotong batas viewBox kalau
    // padding hanya mengikuti rentang model.
    const pad = Math.max(reach * 0.08, radius * 4);

    const bounds: Bounds = {
      minX: minX - pad,
      minY: minY - pad,
      width: spanX + pad * 2,
      height: spanY + pad * 2
    };

    const flip = minY + maxY;

    return {
      bounds,
      radius,
      place: (device: Device) => ({x: device.x_mm, y: flip - device.y_mm})
    };
  }, [devices]);
}

export function PlanCanvas({
  devices,
  circuits,
  selected,
  onSelect,
  symbolOverrides,
  highlighted,
  activeCircuitId
}: {
  devices: Device[];
  /** Urutannya menentukan warna garis, jadi harus sama dengan yang dipakai keterangan. */
  circuits: Circuit[];
  selected: ReadonlySet<string>;
  onSelect: (ids: string[], mode: 'replace' | 'toggle' | 'add') => void;
  symbolOverrides: Record<string, string>;
  /** Device milik circuit yang sedang disorot di daftar samping. */
  highlighted?: ReadonlySet<string>;
  /** Circuit yang sedang disorot; sisanya diredupkan, tidak disembunyikan. */
  activeCircuitId?: string | null;
}) {
  const {bounds, radius, place} = useLayout(devices);

  /**
   * Satu path per circuit. Device milik circuit lain jadi penghalang, jadi garis
   * memutar melewatinya — dan tiap circuit memutar ke sisinya sendiri.
   */
  const routes = useMemo(() => {
    const positions = new Map(devices.map((device) => [device.revit_unique_id, place(device)] as const));

    return circuits
      .map((circuit, index) => {
        const own = new Set(circuit.device_unique_ids);

        // Circuit boleh memuat device dari lantai lain; yang tidak ada di halaman ini dilewati.
        const stops = orderForRouting(
          circuit.device_unique_ids.flatMap((id) => {
            const point = positions.get(id);
            return point ? [point] : [];
          }),
          radius * 3
        );

        const obstacles = devices
          .filter((device) => !own.has(device.revit_unique_id))
          .map((device) => place(device));

        const series = seriesFor(index);

        return {
          id: circuit.id,
          series,
          d: roundedPath(routeThrough(stops, obstacles, radius * 2, series.side), radius * 1.2)
        };
      })
      .filter((route) => route.d.includes('L'));
  }, [circuits, devices, place, radius]);

  const weight = radius / 2.6;
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

      {/* Garis digambar sebelum simbol supaya simbol device tetap di atas. */}
      <g fill="none" strokeLinecap="round" strokeLinejoin="round">
        {routes.map((route) => (
          <path
            key={route.id}
            d={route.d}
            stroke={route.series.color}
            strokeWidth={weight}
            strokeDasharray={
              route.series.dash ? route.series.dash.map((part) => part * weight).join(' ') : undefined
            }
            opacity={activeCircuitId && activeCircuitId !== route.id ? 0.22 : 1}
            className="pointer-events-none transition-opacity duration-200"
          />
        ))}
      </g>

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
