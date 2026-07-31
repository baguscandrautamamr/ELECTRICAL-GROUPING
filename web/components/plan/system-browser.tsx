'use client';

import {ChevronRight} from 'lucide-react';
import {useTranslations} from 'next-intl';
import {useMemo, useState} from 'react';
import {Card, CardHeader, cx} from '@/components/ui';
import type {Circuit, Device, Panel} from '@/lib/contract';

/**
 * Padanan System Browser di Revit, dipersempit ke lighting dan receptacle.
 *
 * Yang membuatnya berguna bukan pohon panel → circuit-nya, tapi simpul
 * **Unassigned**: di Revit itulah satu-satunya tempat yang menyebut berapa banyak
 * device belum masuk circuit mana pun. Di sini angkanya dipecah per family supaya
 * sisa pekerjaan bisa dibaca tanpa menghitung titik di denah.
 */
export function SystemBrowser({
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
  const t = useTranslations('browser');
  const c = useTranslations('circuits');

  const [open, setOpen] = useState<ReadonlySet<string>>(new Set(['unassigned']));

  function toggle(key: string) {
    setOpen((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  const byId = useMemo(
    () => new Map(devices.map((device) => [device.revit_unique_id, device] as const)),
    [devices]
  );

  /** Panel yang benar-benar punya circuit di layout ini, beserta circuit-nya. */
  const tree = useMemo(() => {
    const grouped = new Map<string, Circuit[]>();

    for (const circuit of circuits) {
      const list = grouped.get(circuit.panel_unique_id);
      if (list) list.push(circuit);
      else grouped.set(circuit.panel_unique_id, [circuit]);
    }

    return [...grouped.entries()]
      .map(([panelId, list]) => ({
        panelId,
        name: panels.find((panel) => panel.revit_unique_id === panelId)?.name ?? '—',
        circuits: list
      }))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [circuits, panels]);

  /**
   * Belum masuk circuit mana pun. `no_connector` sengaja tidak dihitung — device
   * itu tidak akan pernah bisa di-circuit, jadi memasukkannya ke daftar sisa
   * pekerjaan hanya membuat angkanya tidak pernah mencapai nol.
   */
  const unassigned = useMemo(
    () => devices.filter((device) => device.status === 'unwired'),
    [devices]
  );

  const unassignedByFamily = useMemo(() => {
    const grouped = new Map<string, Device[]>();
    for (const device of unassigned) {
      const list = grouped.get(device.family_key);
      if (list) list.push(device);
      else grouped.set(device.family_key, [device]);
    }
    return [...grouped.entries()].sort((a, b) => b[1].length - a[1].length);
  }, [unassigned]);

  const ids = (list: Device[]) => list.map((device) => device.revit_unique_id);

  return (
    <Card>
      <CardHeader title={t('heading')} hint={t('hint')} />

      {/* Pohonnya bisa memuat ratusan family dan circuit sekaligus; tanpa batas tinggi
          kartu ini sendirian membuat halaman berkali-kali lipat lebih panjang. */}
      <ul className="max-h-[24rem] space-y-0.5 overflow-y-auto pr-1 text-[13px]">
        <Node
          label={t('unassigned')}
          count={unassigned.length}
          tone="danger"
          expanded={open.has('unassigned')}
          onToggle={() => toggle('unassigned')}
          onHighlight={() => onHighlight(ids(unassigned))}
          onLeave={() => onHighlight([])}
        />

        {open.has('unassigned') ? (
          <li>
            <ul className="ml-4 border-l border-hairline pl-3">
              {unassignedByFamily.length === 0 ? (
                <li className="py-1.5 text-[12px] text-muted">{t('nothingLeft')}</li>
              ) : (
                unassignedByFamily.map(([familyKey, list]) => (
                  <li key={familyKey}>
                    <button
                      type="button"
                      onMouseEnter={() => onHighlight(ids(list))}
                      onMouseLeave={() => onHighlight([])}
                      className="flex w-full items-center gap-2 py-1 text-left"
                    >
                      <span className="min-w-0 flex-1 truncate text-[12px]">
                        {familyKey.replace('::', ' · ')}
                      </span>
                      <span className="text-[12px] text-muted">{list.length}</span>
                    </button>
                  </li>
                ))
              )}
            </ul>
          </li>
        ) : null}

        <Node
          label={t('power')}
          count={circuits.length}
          tone="neutral"
          expanded={open.has('power')}
          onToggle={() => toggle('power')}
          onHighlight={() => undefined}
          onLeave={() => onHighlight([])}
        />

        {open.has('power') ? (
          <li>
            <ul className="ml-4 border-l border-hairline pl-3">
              {tree.length === 0 ? (
                <li className="py-1.5 text-[12px] text-muted">{t('noCircuits')}</li>
              ) : (
                tree.map((group) => (
                  <li key={group.panelId}>
                    <p className="py-1 text-[12px] font-semibold">{group.name}</p>
                    <ul className="ml-2 border-l border-hairline pl-3">
                      {group.circuits.map((circuit) => {
                        const members = circuit.device_unique_ids
                          .map((id) => byId.get(id))
                          .filter((device): device is Device => device !== undefined);

                        return (
                          <li key={circuit.id}>
                            <button
                              type="button"
                              onMouseEnter={() => onHighlight(ids(members))}
                              onMouseLeave={() => onHighlight([])}
                              className="flex w-full items-center gap-2 py-1 text-left"
                            >
                              <span className="min-w-0 flex-1 truncate text-[12px]">
                                {circuit.circuit_number ?? c('numberPending')}
                              </span>
                              <span className="text-[12px] text-muted">{members.length}</span>
                            </button>
                          </li>
                        );
                      })}
                    </ul>
                  </li>
                ))
              )}
            </ul>
          </li>
        ) : null}
      </ul>
    </Card>
  );
}

function Node({
  label,
  count,
  tone,
  expanded,
  onToggle,
  onHighlight,
  onLeave
}: {
  label: string;
  count: number;
  tone: 'danger' | 'neutral';
  expanded: boolean;
  onToggle: () => void;
  onHighlight: () => void;
  onLeave: () => void;
}) {
  return (
    <li>
      <button
        type="button"
        onClick={onToggle}
        onMouseEnter={onHighlight}
        onMouseLeave={onLeave}
        aria-expanded={expanded}
        className="flex w-full items-center gap-1.5 rounded-[7px] px-1 py-1.5 text-left transition-colors hover:bg-sunken"
      >
        <ChevronRight
          className={cx('size-3.5 shrink-0 text-muted transition-transform', expanded && 'rotate-90')}
          aria-hidden
        />
        <span className="font-semibold">{label}</span>
        <span
          className={cx(
            'ml-auto text-[12px] font-semibold',
            tone === 'danger' && count > 0 ? 'text-danger' : 'text-muted'
          )}
        >
          {count}
        </span>
      </button>
    </li>
  );
}
