'use client';

import {ChevronRight, TriangleAlert} from 'lucide-react';
import {useTranslations} from 'next-intl';
import {useState} from 'react';
import {Badge, Card, CardHeader, Notice, cx} from '@/components/ui';
import {splitFamilyKey, type Panel} from '@/lib/contract';
import type {FamilyCount, PanelCircuit} from '@/lib/summaries';

/**
 * Daftar panel yang bisa dibuka untuk melihat isinya.
 *
 * Sebelum ini kartu panel hanya menyebut nama, tegangan, dan slot — angka yang benar
 * tapi tidak menjawab pertanyaan yang sebenarnya ditanyakan orang saat menatapnya:
 * panel ini memberi daya ke apa. Jawabannya sekarang satu klik jauhnya, dipecah per
 * circuit lalu per family, karena "14 titik" dan "14 downlight" bukan keterangan yang
 * sama gunanya saat memeriksa pekerjaan.
 */
export function PanelList({
  panels,
  contents
}: {
  panels: Panel[];
  contents: Record<string, PanelCircuit[]>;
}) {
  const t = useTranslations('project');

  const usable = panels.filter((panel) => panel.is_usable);
  const unusable = panels.filter((panel) => !panel.is_usable);

  return (
    <Card>
      <CardHeader title={t('panels')} hint={t('panelsHint')} />

      <p className="mb-2 text-[11px] font-semibold tracking-wide text-muted uppercase">
        {t('panelsUsable')}
      </p>
      {usable.length === 0 ? (
        <p className="text-[13px] text-muted">—</p>
      ) : (
        <ul className="divide-y divide-hairline">
          {usable.map((panel) => (
            <PanelRow
              key={panel.revit_unique_id}
              panel={panel}
              circuits={contents[panel.revit_unique_id] ?? []}
            />
          ))}
        </ul>
      )}

      {/*
        Panel yang tidak layak tidak disembunyikan: menghilangkannya tanpa
        penjelasan membuat user mencari panelnya dan mengira data hilang.
      */}
      {unusable.length > 0 ? (
        <div className="mt-6">
          <p className="mb-2 flex items-center gap-1.5 text-[11px] font-semibold tracking-wide text-muted uppercase">
            <TriangleAlert className="size-3.5 text-warn" aria-hidden />
            {t('panelsUnusable')}
          </p>
          <Notice tone="warn">{t('panelsUnusableReason')}</Notice>
          <ul className="mt-2 divide-y divide-hairline">
            {unusable.map((panel) => (
              <PanelRow
                key={panel.revit_unique_id}
                panel={panel}
                circuits={contents[panel.revit_unique_id] ?? []}
                muted
              />
            ))}
          </ul>
        </div>
      ) : null}
    </Card>
  );
}

function PanelRow({
  panel,
  circuits,
  muted = false
}: {
  panel: Panel;
  circuits: PanelCircuit[];
  muted?: boolean;
}) {
  const t = useTranslations('project');
  const [open, setOpen] = useState(false);

  const devices = circuits.reduce((sum, circuit) => sum + circuit.devices, 0);

  return (
    <li>
      <button
        type="button"
        onClick={() => setOpen((current) => !current)}
        aria-expanded={open}
        className="-mx-2 flex w-full flex-wrap items-center gap-x-3 gap-y-1 rounded-[var(--radius-control)] px-2 py-2.5 text-left transition-colors duration-200 hover:bg-sunken"
      >
        <ChevronRight
          className={cx('size-4 shrink-0 text-muted transition-transform', open && 'rotate-90')}
          aria-hidden
        />
        <p className={cx('text-[14px] font-semibold', muted && 'text-muted')}>{panel.name}</p>
        {panel.prefix ? <Badge>{panel.prefix}</Badge> : null}
        {panel.distribution_system ? (
          <p className="text-[12px] text-muted">{panel.distribution_system}</p>
        ) : null}

        <p className="ml-auto text-[12px] text-muted">
          {devices === 0
            ? t('panelNothing')
            : `${t('panelCircuits', {count: circuits.length})} · ${t('panelDevices', {count: devices})}`}
        </p>
        <p className="w-full text-[12px] text-muted sm:w-auto sm:pl-3">
          {panel.slots_total
            ? t('slots', {used: panel.slots_used ?? 0, total: panel.slots_total})
            : t('slotsUnknown')}
        </p>
      </button>

      {open ? (
        <div className="mb-2 ml-6 border-l border-hairline pl-4">
          {circuits.length === 0 ? (
            <p className="py-2 text-[12px] leading-relaxed text-muted">{t('panelEmpty')}</p>
          ) : (
            <ul className="space-y-2 py-1">
              {circuits.map((circuit) => (
                <li key={circuit.number ?? ''}>
                  <div className="flex flex-wrap items-baseline gap-x-3 gap-y-0.5">
                    <p className="text-[13px] font-semibold">
                      {circuit.number ?? t('circuitPending')}
                    </p>
                    <p className="text-[12px] text-muted">{composition(circuit, t)}</p>
                  </div>
                  <FamilyBreakdown families={circuit.families} />
                </li>
              ))}
            </ul>
          )}
        </div>
      ) : null}
    </li>
  );
}

/**
 * "14 lampu" saat isinya satu jenis, "10 lampu · 4 receptacle" saat campuran.
 *
 * Menyebut kedua jenis selalu — termasuk yang nol — membuat baris circuit lighting
 * memuat "0 receptacle", keterangan yang menyita ruang tanpa memberi tahu apa pun.
 */
function composition(
  circuit: {lighting: number; receptacle: number},
  t: ReturnType<typeof useTranslations<'project'>>
) {
  const parts: string[] = [];
  if (circuit.lighting > 0) parts.push(t('lightingCount', {count: circuit.lighting}));
  if (circuit.receptacle > 0) parts.push(t('receptacleCount', {count: circuit.receptacle}));
  return parts.join(' · ');
}

/** Family apa saja yang membentuk angka di atasnya, terbanyak lebih dulu. */
export function FamilyBreakdown({families}: {families: FamilyCount[]}) {
  if (families.length === 0) return null;

  return (
    <ul className="mt-1 space-y-0.5">
      {families.map((family) => {
        const {family: name, type} = splitFamilyKey(family.familyKey);
        return (
          <li key={family.familyKey} className="flex items-baseline gap-2 text-[12px] text-muted">
            <span className="min-w-0 flex-1 truncate">
              {type.length > 0 ? `${name} · ${type}` : name}
            </span>
            <span className="shrink-0 tabular-nums">{family.devices}</span>
          </li>
        );
      })}
    </ul>
  );
}
