import {ChevronRight, Lightbulb, PlugZap, TriangleAlert} from 'lucide-react';
import {getTranslations} from 'next-intl/server';
import type {Metadata} from 'next';
import {notFound} from 'next/navigation';
import {Badge, Card, CardHeader, Empty, Notice} from '@/components/ui';
import {Link} from '@/i18n/navigation';
import {DEVICE_KINDS, type Device, type Level, type Panel, type Project} from '@/lib/contract';
import {createClient} from '@/lib/supabase/server';

type Params = {params: Promise<{projectId: string}>};

async function load(projectId: string) {
  const supabase = await createClient();

  const [project, levels, panels, devices] = await Promise.all([
    supabase.from('projects').select('id, name, owner_id, created_at, updated_at').eq('id', projectId).maybeSingle(),
    supabase.from('levels').select('*').eq('project_id', projectId).order('sort_order'),
    supabase.from('panels').select('*').eq('project_id', projectId).order('name'),
    supabase.from('devices').select('kind, level_key').eq('project_id', projectId)
  ]);

  return {
    project: project.data as Project | null,
    levels: (levels.data ?? []) as Level[],
    panels: (panels.data ?? []) as Panel[],
    devices: (devices.data ?? []) as Pick<Device, 'kind' | 'level_key'>[]
  };
}

export async function generateMetadata({params}: Params): Promise<Metadata> {
  const {projectId} = await params;
  const {project} = await load(projectId);
  return {title: project?.name ?? 'Project'};
}

export default async function ProjectPage({params}: Params) {
  const {projectId} = await params;
  const {project, levels, panels, devices} = await load(projectId);

  // RLS mengembalikan nol baris untuk project yang bukan milik user, jadi
  // "tidak ada" dan "bukan milik saya" sengaja tidak dibedakan di sini.
  if (!project) notFound();

  const t = await getTranslations('project');
  const usable = panels.filter((panel) => panel.is_usable);
  const unusable = panels.filter((panel) => !panel.is_usable);

  function count(levelKey: string, kind: string) {
    return devices.filter((device) => device.level_key === levelKey && device.kind === kind).length;
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-[24px] font-semibold tracking-tight">{project.name}</h1>
        <p className="mt-1 text-[13px] text-muted">{t('levelsSubheading')}</p>
      </div>

      {devices.length === 0 ? <Empty title={t('emptyTitle')} body={t('emptyBody')} /> : null}

      {levels.length > 0 ? (
        <Card>
          <CardHeader title={t('levels')} />
          <ul className="divide-y divide-hairline">
            {levels.map((level) => (
              <li key={level.level_key} className="py-3 first:pt-0 last:pb-0">
                <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
                  <p className="min-w-24 text-[15px] font-semibold">{level.name}</p>
                  <p className="text-[12px] text-muted">
                    {t('deviceSummary', {
                      lighting: count(level.level_key, 'lighting'),
                      receptacle: count(level.level_key, 'receptacle')
                    })}
                  </p>
                  <div className="ml-auto flex gap-2">
                    {DEVICE_KINDS.map((kind) => (
                      <Link
                        key={kind}
                        href={`/projects/${project.id}/${encodeURIComponent(level.level_key)}/${kind}`}
                        className="inline-flex items-center gap-1.5 rounded-[var(--radius-control)] border border-hairline bg-sunken px-3 py-1.5 text-[12px] font-semibold transition-colors duration-200 hover:border-muted"
                      >
                        {kind === 'lighting' ? (
                          <Lightbulb className="size-3.5" aria-hidden />
                        ) : (
                          <PlugZap className="size-3.5" aria-hidden />
                        )}
                        {t(kind)}
                        <ChevronRight className="size-3.5 text-muted" aria-hidden />
                      </Link>
                    ))}
                  </div>
                </div>
              </li>
            ))}
          </ul>
        </Card>
      ) : null}

      {panels.length > 0 ? (
        <Card>
          <CardHeader title={t('panels')} />

          <p className="mb-2 text-[11px] font-semibold tracking-wide text-muted uppercase">
            {t('panelsUsable')}
          </p>
          {usable.length === 0 ? (
            <p className="text-[13px] text-muted">—</p>
          ) : (
            <ul className="divide-y divide-hairline">
              {usable.map((panel) => (
                <li key={panel.revit_unique_id} className="flex flex-wrap items-center gap-3 py-2.5">
                  <p className="text-[14px] font-semibold">{panel.name}</p>
                  {panel.prefix ? <Badge>{panel.prefix}</Badge> : null}
                  <p className="text-[12px] text-muted">{panel.distribution_system}</p>
                  <p className="ml-auto text-[12px] text-muted">
                    {panel.slots_total
                      ? t('slots', {used: panel.slots_used ?? 0, total: panel.slots_total})
                      : t('slotsUnknown')}
                  </p>
                </li>
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
                  <li key={panel.revit_unique_id} className="py-2.5 text-[14px] text-muted">
                    {panel.name}
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
        </Card>
      ) : null}
    </div>
  );
}
