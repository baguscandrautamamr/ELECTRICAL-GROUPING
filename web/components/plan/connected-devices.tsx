'use client';

import {useTranslations} from 'next-intl';
import {useMemo} from 'react';
import {Badge, Card, CardHeader} from '@/components/ui';
import type {Circuit, Device, Panel} from '@/lib/contract';

/**
 * Device yang sudah tersambung ke circuit dan panel, dikeluarkan dari denah ke
 * daftar tersendiri di bawahnya.
 *
 * Alasannya praktis: pada denah dengan ratusan titik, yang sudah selesai tetap
 * memenuhi layar dan menyulitkan melihat sisa pekerjaan. Titiknya tetap digambar
 * di denah — ini daftar baca, bukan penyaring — tapi di sini ia terkelompok per
 * circuit sehingga hasil yang sudah jadi bisa diperiksa tanpa mengklik satu-satu.
 */
export function ConnectedDevices({
  devices,
  circuits,
  panels,
  onHighlight
}: {
  devices: Device[];
  circuits: Circuit[];
  panels: Panel[];
  onHighlight: (deviceIds: string[]) => void;
}) {
  const t = useTranslations('connected');
  const c = useTranslations('circuits');

  const panelName = useMemo(
    () => new Map(panels.map((panel) => [panel.revit_unique_id, panel.name] as const)),
    [panels]
  );

  const byId = useMemo(
    () => new Map(devices.map((device) => [device.revit_unique_id, device] as const)),
    [devices]
  );

  /**
   * Sumber kebenarannya `device.status`, bukan keberadaan circuit di web: status
   * itu yang ditulis balik Revit setelah apply. Circuit yang masih usulan tidak
   * boleh muncul di sini seolah-olah sudah jadi.
   */
  const groups = useMemo(() => {
    const connected = devices.filter((device) => device.status === 'connected');
    const claimed = new Set<string>();

    const fromCircuits = circuits
      .filter((circuit) => circuit.status === 'applied')
      .map((circuit) => {
        const members = circuit.device_unique_ids
          .map((id) => byId.get(id))
          .filter((device): device is Device => device?.status === 'connected');

        members.forEach((device) => claimed.add(device.revit_unique_id));

        return {
          key: circuit.id,
          title: circuit.circuit_number ?? panelName.get(circuit.panel_unique_id) ?? '—',
          panel: panelName.get(circuit.panel_unique_id) ?? '—',
          members
        };
      })
      .filter((group) => group.members.length > 0);

    // Device bisa tersambung di Revit tanpa circuit-nya pernah lewat web —
    // misalnya model yang sudah di-circuit sebelum project ini dibuat.
    const orphans = connected.filter((device) => !claimed.has(device.revit_unique_id));

    return {fromCircuits, orphans};
  }, [devices, circuits, byId, panelName]);

  const total = groups.fromCircuits.reduce((sum, group) => sum + group.members.length, 0) +
    groups.orphans.length;

  return (
    <Card>
      <CardHeader
        title={t('heading')}
        hint={total === 0 ? undefined : t('summary', {count: total})}
        action={total > 0 ? <Badge tone="ok">{total}</Badge> : undefined}
      />

      {total === 0 ? (
        <p className="text-[13px] leading-relaxed text-muted">{t('empty')}</p>
      ) : (
        <ul className="divide-y divide-hairline">
          {groups.fromCircuits.map((group) => (
            <li key={group.key} className="py-2.5 first:pt-0">
              <button
                type="button"
                onMouseEnter={() => onHighlight(group.members.map((d) => d.revit_unique_id))}
                onMouseLeave={() => onHighlight([])}
                onFocus={() => onHighlight(group.members.map((d) => d.revit_unique_id))}
                onBlur={() => onHighlight([])}
                className="flex w-full flex-wrap items-center gap-x-3 gap-y-1 text-left"
              >
                <span className="size-2 shrink-0 rotate-45 bg-ok" aria-hidden />
                <span className="text-[13px] font-semibold">{group.title}</span>
                <span className="text-[12px] text-muted">{group.panel}</span>
                <span className="ml-auto text-[12px] text-muted">
                  {c('deviceCount', {count: group.members.length})}
                </span>
              </button>
            </li>
          ))}

          {groups.orphans.length > 0 ? (
            <li className="py-2.5">
              <button
                type="button"
                onMouseEnter={() => onHighlight(groups.orphans.map((d) => d.revit_unique_id))}
                onMouseLeave={() => onHighlight([])}
                onFocus={() => onHighlight(groups.orphans.map((d) => d.revit_unique_id))}
                onBlur={() => onHighlight([])}
                className="flex w-full flex-wrap items-center gap-x-3 gap-y-1 text-left"
              >
                <span className="size-2 shrink-0 rotate-45 bg-ok" aria-hidden />
                <span className="text-[13px] font-semibold">{t('fromRevit')}</span>
                <span className="text-[12px] text-muted">{t('fromRevitHint')}</span>
                <span className="ml-auto text-[12px] text-muted">
                  {c('deviceCount', {count: groups.orphans.length})}
                </span>
              </button>
            </li>
          ) : null}
        </ul>
      )}
    </Card>
  );
}
