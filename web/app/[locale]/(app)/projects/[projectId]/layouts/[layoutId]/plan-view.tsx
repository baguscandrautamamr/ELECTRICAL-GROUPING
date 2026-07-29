'use client';

import {Check, Pencil, Send, Trash2, X, Zap} from 'lucide-react';
import {useTranslations} from 'next-intl';
import {useRouter} from 'next/navigation';
import {useEffect, useMemo, useState, useTransition} from 'react';
import {ConnectedDevices} from '@/components/plan/connected-devices';
import {Legend} from '@/components/plan/legend';
import {PlanCanvas} from '@/components/plan/plan-canvas';
import {SystemBrowser} from '@/components/plan/system-browser';
import {Badge, Button, Card, CardHeader, Empty, Notice, Select, cx} from '@/components/ui';
import type {Circuit, Device, DeviceKind, Layout, Panel} from '@/lib/contract';
import {createCircuit, queueApply, removeCircuit, updateCircuit} from './actions';

/** Jeda penyegaran selagi ada circuit yang menunggu dikerjakan add-in. */
const WAITING_POLL_MS = 5000;

type Feedback = {tone: 'ok' | 'danger'; text: string} | null;

export function PlanView({
  projectId,
  kind,
  layout,
  devices,
  panels,
  circuits,
  symbolOverrides
}: {
  projectId: string;
  kind: DeviceKind;
  layout: Layout;
  devices: Device[];
  panels: Panel[];
  circuits: Circuit[];
  symbolOverrides: Record<string, string>;
}) {
  const t = useTranslations('plan');
  const c = useTranslations('circuits');
  const errors = useTranslations('errors');
  const revitErrors = useTranslations('revitErrors');
  const router = useRouter();

  /**
   * Add-in menulis kunci pesan ke `circuits.error`, bukan teks. Kunci yang tidak
   * dikenal ditampilkan sebagai pesan umum — bukan kunci mentah, yang tidak berarti
   * apa pun bagi engineer yang membacanya.
   */
  function explainRevit(key: string): string {
    return revitErrors.has(key) ? revitErrors(key) : errors('unknown');
  }

  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [hovered, setHovered] = useState<string | null>(null);
  /** Sorotan yang datang dari daftar di bawah denah dan dari System Browser. */
  const [pinned, setPinned] = useState<ReadonlySet<string> | null>(null);
  const [panelId, setPanelId] = useState('');
  const [feedback, setFeedback] = useState<Feedback>(null);
  const [pending, startTransition] = useTransition();
  /** Id circuit yang sedang diubah. Null berarti kartu samping sedang membuat yang baru. */
  const [editing, setEditing] = useState<string | null>(null);

  const usablePanels = useMemo(() => panels.filter((panel) => panel.is_usable), [panels]);
  const byId = useMemo(
    () => new Map(devices.map((device) => [device.revit_unique_id, device] as const)),
    [devices]
  );

  const familyKeys = useMemo(
    () => [...new Set(devices.map((device) => device.family_key))].sort(),
    [devices]
  );

  const load = useMemo(() => {
    let total = 0;
    let missing = 0;
    for (const id of selected) {
      const va = byId.get(id)?.va;
      if (va && va > 0) total += va;
      else missing++;
    }
    return {total, missing};
  }, [selected, byId]);

  const drafts = circuits.filter((circuit) => circuit.status === 'draft' || circuit.status === 'failed');

  /**
   * Add-in menulis hasilnya langsung ke database, jadi halaman ini hanya perlu melihat
   * lagi — tanpa itu titik yang sudah tersambung baru berubah hijau setelah user menekan
   * muat ulang sendiri, dan tampak seolah pengirimannya gagal.
   */
  const waitingForRevit = circuits.some((circuit) => circuit.status === 'queued');

  useEffect(() => {
    if (!waitingForRevit) return;

    const timer = setInterval(() => router.refresh(), WAITING_POLL_MS);
    return () => clearInterval(timer);
  }, [waitingForRevit, router]);
  const highlighted = useMemo(() => {
    if (pinned) return pinned;
    const circuit = circuits.find((candidate) => candidate.id === hovered);
    return new Set(circuit?.device_unique_ids ?? []);
  }, [pinned, hovered, circuits]);

  function select(ids: string[], mode: 'replace' | 'toggle' | 'add') {
    setSelected((current) => {
      if (mode === 'replace') return new Set(ids);

      const next = new Set(current);
      for (const id of ids) {
        if (mode === 'toggle' && next.has(id)) next.delete(id);
        else next.add(id);
      }
      return next;
    });
  }

  function act(work: () => Promise<{ok: boolean; reason?: string}>, done: string, onDone?: () => void) {
    startTransition(async () => {
      const result = await work();

      if (result.ok) {
        setFeedback({tone: 'ok', text: done});
        onDone?.();
        router.refresh();
        return;
      }

      setFeedback({
        tone: 'danger',
        text:
          result.reason === 'selection'
            ? c('needSelection')
            : result.reason === 'panel'
              ? c('needPanel')
              : errors('unknown')
      });
    });
  }

  /**
   * Mengubah circuit memakai alat yang sama dengan membuatnya: isi circuit masuk ke
   * pilihan di denah, lalu user menambah atau mengurangi titik seperti biasa.
   */
  function startEdit(circuit: Circuit) {
    setEditing(circuit.id);
    setSelected(new Set(circuit.device_unique_ids));
    setPanelId(circuit.panel_unique_id);
    setPinned(new Set(circuit.device_unique_ids));
    setFeedback(null);
  }

  function stopEdit() {
    setEditing(null);
    setSelected(new Set());
    setPanelId('');
    setPinned(null);
  }

  const edited = editing === null ? null : circuits.find((circuit) => circuit.id === editing);

  return (
    <div className="grid gap-5 lg:grid-cols-[1fr_22rem]">
      <div className="space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <p className="text-[13px] font-semibold">{t('selected', {count: selected.size})}</p>
          {selected.size > 0 ? (
            <>
              <Badge tone="accent">{t('totalLoad', {va: Math.round(load.total)})}</Badge>
              {load.missing > 0 ? <Badge tone="warn">{t('missingLoad', {count: load.missing})}</Badge> : null}
              <Button tone="quiet" onClick={() => setSelected(new Set())}>
                {t('clear')}
              </Button>
            </>
          ) : (
            <Button
              tone="quiet"
              onClick={() =>
                setSelected(
                  new Set(
                    devices
                      .filter((device) => device.status === 'unwired')
                      .map((device) => device.revit_unique_id)
                  )
                )
              }
            >
              {t('selectAllUnwired')}
            </Button>
          )}
        </div>

        <PlanCanvas
          devices={devices}
          selected={selected}
          onSelect={select}
          symbolOverrides={symbolOverrides}
          highlighted={highlighted}
          crop={layout}
        />

        <p className="text-[12px] text-muted">{t('hint')}</p>

        <ConnectedDevices
          devices={devices}
          circuits={circuits}
          panels={panels}
          onHighlight={(ids) => setPinned(ids.length > 0 ? new Set(ids) : null)}
        />
      </div>

      <div className="space-y-5">
        <Card>
          <CardHeader
            title={edited ? c('editHeading') : c('heading')}
            hint={
              edited
                ? c('editHint', {
                    number: edited.circuit_number ?? c('numberPending')
                  })
                : undefined
            }
          />

          {usablePanels.length === 0 ? (
            <Notice tone="warn">{c('noUsablePanel')}</Notice>
          ) : (
            <div className="space-y-3">
              <Select label={c('panel')} value={panelId} onChange={(event) => setPanelId(event.target.value)}>
                <option value="">{c('choosePanel')}</option>
                {usablePanels.map((panel) => (
                  <option key={panel.revit_unique_id} value={panel.revit_unique_id}>
                    {panel.name}
                    {panel.prefix ? ` · ${panel.prefix}` : ''}
                  </option>
                ))}
              </Select>

              {edited ? (
                <div className="flex flex-wrap gap-2">
                  <Button
                    tone="primary"
                    disabled={pending}
                    onClick={() =>
                      act(
                        () =>
                          updateCircuit({
                            circuitId: edited.id,
                            panelUniqueId: panelId,
                            deviceUniqueIds: [...selected]
                          }),
                        c('updated'),
                        stopEdit
                      )
                    }
                  >
                    <Check className="size-4" aria-hidden />
                    {c('saveEdit')}
                  </Button>
                  <Button tone="quiet" disabled={pending} onClick={stopEdit}>
                    <X className="size-4" aria-hidden />
                    {c('cancelEdit')}
                  </Button>
                </div>
              ) : (
                <Button
                  tone="primary"
                  disabled={pending}
                  onClick={() =>
                    act(
                      () =>
                        createCircuit({
                          projectId,
                          panelUniqueId: panelId,
                          kind,
                          deviceUniqueIds: [...selected]
                        }),
                      c('created')
                    )
                  }
                >
                  <Zap className="size-4" aria-hidden />
                  {c('create')}
                </Button>
              )}
            </div>
          )}

          {feedback ? (
            <div className="mt-3">
              <Notice tone={feedback.tone}>{feedback.text}</Notice>
            </div>
          ) : null}
        </Card>

        <Card>
          <CardHeader
            title={c('draft')}
            action={
              drafts.length > 0 ? (
                <Button
                  tone="secondary"
                  disabled={pending}
                  onClick={() =>
                    act(
                      () => queueApply(projectId, drafts.map((circuit) => circuit.id)),
                      c('sent')
                    )
                  }
                >
                  <Send className="size-4" aria-hidden />
                  {c('sendCount', {count: drafts.length})}
                </Button>
              ) : undefined
            }
          />

          {circuits.length === 0 ? (
            <Empty title={c('emptyTitle')} body={c('emptyBody')} />
          ) : (
            /**
             * Daftar circuit tumbuh seiring pekerjaan dan bisa mencapai puluhan baris.
             * Dibatasi tingginya supaya kartu di bawahnya tetap terjangkau tanpa harus
             * menggulung seluruh halaman.
             */
            <ul className="max-h-[28rem] divide-y divide-hairline overflow-y-auto pr-1">
              {circuits.map((circuit) => {
                const panel = panels.find((candidate) => candidate.revit_unique_id === circuit.panel_unique_id);
                const va = circuit.device_unique_ids.reduce(
                  (sum, id) => sum + (byId.get(id)?.va ?? 0),
                  0
                );

                return (
                  <li
                    key={circuit.id}
                    onMouseEnter={() => setHovered(circuit.id)}
                    onMouseLeave={() => setHovered(null)}
                    className={cx(
                      'flex items-start gap-3 py-3 transition-colors duration-200 first:pt-0 last:pb-0',
                      hovered === circuit.id && 'bg-sunken',
                      editing === circuit.id && 'bg-accent/10'
                    )}
                  >
                    <div className="min-w-0 flex-1">
                      <p className="text-[13px] font-semibold">
                        {circuit.circuit_number
                          ? c('number', {number: circuit.circuit_number})
                          : c('numberPending')}
                      </p>
                      <p className="mt-0.5 text-[12px] text-muted">
                        {panel?.name ?? circuit.panel_unique_id} ·{' '}
                        {c('deviceCount', {count: circuit.device_unique_ids.length})} ·{' '}
                        {c('load', {va: Math.round(va)})}
                      </p>
                      {circuit.error ? (
                        <p className="mt-1 text-[12px] text-danger">
                          {c('errorPrefix')}: {explainRevit(circuit.error)}
                        </p>
                      ) : null}
                      {circuit.status === 'queued' ? (
                        <p className="mt-1 text-[12px] text-muted">{c('queuedNote')}</p>
                      ) : null}
                    </div>

                    <div className="flex shrink-0 items-center gap-1">
                      <Badge
                        tone={
                          circuit.status === 'applied'
                            ? 'ok'
                            : circuit.status === 'failed'
                              ? 'danger'
                              : circuit.status === 'queued'
                                ? 'warn'
                                : 'neutral'
                        }
                      >
                        {c(circuit.status)}
                      </Badge>

                      {/* Circuit yang sedang dikerjakan add-in tidak boleh diubah di tengah jalan. */}
                      {circuit.status === 'queued' ? null : (
                        <Button
                          tone="quiet"
                          aria-label={c('edit')}
                          title={c('edit')}
                          disabled={pending}
                          className="px-2"
                          onClick={() => startEdit(circuit)}
                        >
                          <Pencil className="size-4" aria-hidden />
                        </Button>
                      )}

                      {circuit.status === 'draft' || circuit.status === 'failed' ? (
                        <Button
                          tone="danger"
                          aria-label={c('remove')}
                          title={c('remove')}
                          disabled={pending}
                          className="px-2"
                          onClick={() => act(() => removeCircuit(circuit.id), c('removed'))}
                        >
                          <Trash2 className="size-4" aria-hidden />
                        </Button>
                      ) : null}
                    </div>
                  </li>
                );
              })}
            </ul>
          )}
        </Card>

        <SystemBrowser
          devices={devices}
          circuits={circuits}
          panels={panels}
          onHighlight={(ids) => setPinned(ids.length > 0 ? new Set(ids) : null)}
        />

        <Card>
          <Legend familyKeys={familyKeys} />
        </Card>
      </div>
    </div>
  );
}
