'use client';

import {Send, Trash2, Zap} from 'lucide-react';
import {useTranslations} from 'next-intl';
import {useRouter} from 'next/navigation';
import {useMemo, useState, useTransition} from 'react';
import {Legend} from '@/components/plan/legend';
import {PlanCanvas} from '@/components/plan/plan-canvas';
import {Badge, Button, Card, CardHeader, Empty, Notice, Select, cx} from '@/components/ui';
import type {Circuit, Device, DeviceKind, Panel} from '@/lib/contract';
import {createCircuit, queueApply, removeCircuit} from './actions';

type Feedback = {tone: 'ok' | 'danger'; text: string} | null;

export function PlanView({
  projectId,
  kind,
  devices,
  panels,
  circuits,
  symbolOverrides
}: {
  projectId: string;
  kind: DeviceKind;
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
  const [panelId, setPanelId] = useState('');
  const [feedback, setFeedback] = useState<Feedback>(null);
  const [pending, startTransition] = useTransition();

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
  const highlighted = useMemo(() => {
    const circuit = circuits.find((candidate) => candidate.id === hovered);
    return new Set(circuit?.device_unique_ids ?? []);
  }, [hovered, circuits]);

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

  function act(work: () => Promise<{ok: boolean; reason?: string}>, done: string) {
    startTransition(async () => {
      const result = await work();

      if (result.ok) {
        setFeedback({tone: 'ok', text: done});
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
          circuits={circuits}
          selected={selected}
          onSelect={select}
          symbolOverrides={symbolOverrides}
          highlighted={highlighted}
          activeCircuitId={hovered}
        />

        <p className="text-[12px] text-muted">{t('hint')}</p>
      </div>

      <div className="space-y-5">
        <Card>
          <CardHeader title={c('heading')} />

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
                  {c('send')}
                </Button>
              ) : undefined
            }
          />

          {circuits.length === 0 ? (
            <Empty title={c('emptyTitle')} body={c('emptyBody')} />
          ) : (
            <ul className="divide-y divide-hairline">
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
                      hovered === circuit.id && 'bg-sunken'
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

        <Card>
          <Legend familyKeys={familyKeys} circuits={circuits} />
        </Card>
      </div>
    </div>
  );
}
