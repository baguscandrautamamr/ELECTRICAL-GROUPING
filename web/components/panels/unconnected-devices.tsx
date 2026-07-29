'use client';

import {ChevronRight} from 'lucide-react';
import {useTranslations} from 'next-intl';
import {useState} from 'react';
import {FamilyBreakdown} from '@/components/panels/panel-list';
import {Badge, Card, CardHeader, cx} from '@/components/ui';
import {Link} from '@/i18n/navigation';
import type {Layout} from '@/lib/contract';
import type {UnconnectedGroup} from '@/lib/summaries';

/**
 * Lampu dan receptacle yang belum tersambung ke panel mana pun, per denah.
 *
 * Pasangannya kartu panel di atasnya: yang satu menyebut apa yang sudah jadi, yang ini
 * menyebut apa yang belum. Dipecah per denah karena di situlah pekerjaannya dilakukan —
 * angka gabungan seluruh project memberi tahu bahwa ada sisa, tapi tidak ke mana harus
 * pergi untuk menghabiskannya.
 */
export function UnconnectedDevices({
  projectId,
  groups,
  layouts,
  total
}: {
  projectId: string;
  groups: UnconnectedGroup[];
  layouts: Layout[];
  total: number;
}) {
  const t = useTranslations('project');

  const names = new Map(layouts.map((layout) => [layout.revit_unique_id, layout.name] as const));

  return (
    <Card>
      <CardHeader
        title={t('unconnected')}
        hint={total === 0 ? undefined : t('unconnectedHint')}
        action={total > 0 ? <Badge tone="danger">{total}</Badge> : undefined}
      />

      {total === 0 ? (
        <p className="text-[13px] leading-relaxed text-muted">{t('unconnectedNone')}</p>
      ) : (
        <ul className="divide-y divide-hairline">
          {groups.map((group) => (
            <Group
              key={group.layoutUniqueId ?? ''}
              projectId={projectId}
              group={group}
              name={
                group.layoutUniqueId === null
                  ? t('unconnectedOutside')
                  : (names.get(group.layoutUniqueId) ?? t('unconnectedOutside'))
              }
            />
          ))}
        </ul>
      )}
    </Card>
  );
}

function Group({
  projectId,
  group,
  name
}: {
  projectId: string;
  group: UnconnectedGroup;
  name: string;
}) {
  const t = useTranslations('project');
  const [open, setOpen] = useState(false);

  const parts: string[] = [];
  if (group.lighting > 0) parts.push(t('lightingCount', {count: group.lighting}));
  if (group.receptacle > 0) parts.push(t('receptacleCount', {count: group.receptacle}));

  return (
    <li className="py-1">
      <div className="flex flex-wrap items-center gap-x-3">
        <button
          type="button"
          onClick={() => setOpen((current) => !current)}
          aria-expanded={open}
          className="-mx-2 flex min-w-0 flex-1 flex-wrap items-center gap-x-3 gap-y-1 rounded-[var(--radius-control)] px-2 py-2 text-left transition-colors duration-200 hover:bg-sunken"
        >
          <ChevronRight
            className={cx('size-4 shrink-0 text-muted transition-transform', open && 'rotate-90')}
            aria-hidden
          />
          <span className="min-w-0 flex-1 truncate text-[14px] font-semibold">{name}</span>
          <span className="text-[12px] text-danger">{parts.join(' · ')}</span>
        </button>

        {/* Denah yang tidak diketahui tidak bisa dibuka — device-nya tidak tampak di
            view mana pun, dan itu masalah di Revit, bukan sesuatu yang bisa
            diselesaikan dari halaman denah. */}
        {group.layoutUniqueId === null ? null : (
          <Link
            href={`/projects/${projectId}/layouts/${encodeURIComponent(group.layoutUniqueId)}`}
            className="shrink-0 text-[12px] font-semibold text-accent transition-opacity duration-200 hover:opacity-80"
          >
            {t('openLayout')}
          </Link>
        )}
      </div>

      {open ? (
        <div className="mb-2 ml-6 border-l border-hairline pl-4">
          {group.noPanel > 0 ? (
            <p className="py-1 text-[12px] leading-relaxed text-muted">
              {t('unconnectedNoPanel', {count: group.noPanel})}
            </p>
          ) : null}
          <FamilyBreakdown families={group.families} />
        </div>
      ) : null}
    </li>
  );
}
