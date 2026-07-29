import {ChevronRight, Lightbulb, PlugZap, TriangleAlert} from 'lucide-react';
import {getTranslations} from 'next-intl/server';
import type {Metadata} from 'next';
import {notFound} from 'next/navigation';
import {Badge, Card, CardHeader, Empty, Notice} from '@/components/ui';
import {SetupNeeded} from '@/components/setup-needed';
import {Link} from '@/i18n/navigation';
import {type Device, type Layout, type Panel, type Project} from '@/lib/contract';
import {firstProblem} from '@/lib/supabase/errors';
import {createClient} from '@/lib/supabase/server';

type Params = {params: Promise<{projectId: string}>};

async function load(projectId: string) {
  const supabase = await createClient();

  const [project, layouts, panels, devices] = await Promise.all([
    supabase.from('projects').select('id, name, owner_id, created_at, updated_at').eq('id', projectId).maybeSingle(),
    supabase.from('layouts').select('*').eq('project_id', projectId).order('sort_order'),
    supabase.from('panels').select('*').eq('project_id', projectId).order('name'),
    supabase.from('devices').select('kind, level_key').eq('project_id', projectId)
  ]);

  return {
    // Tanpa ini, database yang belum dimigrasi berakhir sebagai 404: project-nya
    // null bukan karena tidak ada, tapi karena tabelnya belum ada.
    problem: firstProblem(project.error, layouts.error, panels.error, devices.error),
    project: project.data as Project | null,
    layouts: (layouts.data ?? []) as Layout[],
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
  const {problem, project, layouts, panels, devices} = await load(projectId);

  if (problem) return <SetupNeeded problem={problem} />;

  // RLS mengembalikan nol baris untuk project yang bukan milik user, jadi
  // "tidak ada" dan "bukan milik saya" sengaja tidak dibedakan di sini.
  if (!project) notFound();

  const t = await getTranslations('project');
  const usable = panels.filter((panel) => panel.is_usable);
  const unusable = panels.filter((panel) => !panel.is_usable);

  // Layout membawa lantai dan jenisnya sendiri, jadi jumlah device dihitung dari
  // pasangan itu — bukan dari nama view, yang hanya label.
  function count(layout: Layout) {
    return devices.filter(
      (device) => device.level_key === layout.level_key && device.kind === layout.kind
    ).length;
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-[24px] font-semibold tracking-tight">{project.name}</h1>
        <p className="mt-1 text-[13px] text-muted">{t('layoutsSubheading')}</p>
      </div>

      {devices.length === 0 ? <Empty title={t('emptyTitle')} body={t('emptyBody')} /> : null}

      {layouts.length > 0 ? (
        <Card>
          <CardHeader title={t('layouts')} hint={t('layoutsHint')} />
          <ul className="divide-y divide-hairline">
            {layouts.map((layout) => (
              <li key={layout.revit_unique_id}>
                <Link
                  href={`/projects/${project.id}/layouts/${encodeURIComponent(layout.revit_unique_id)}`}
                  className="-mx-2 flex flex-wrap items-center gap-x-3 gap-y-1 rounded-[var(--radius-control)] px-2 py-3 transition-colors duration-200 hover:bg-sunken"
                >
                  {layout.kind === 'lighting' ? (
                    <Lightbulb className="size-4 shrink-0 text-muted" aria-hidden />
                  ) : (
                    <PlugZap className="size-4 shrink-0 text-muted" aria-hidden />
                  )}
                  <p className="min-w-0 flex-1 text-[14px] font-semibold">{layout.name}</p>
                  {layout.scale ? <Badge>{t('scale', {scale: layout.scale})}</Badge> : null}
                  <p className="text-[12px] text-muted">{t('deviceCount', {count: count(layout)})}</p>
                  <ChevronRight className="size-4 shrink-0 text-muted" aria-hidden />
                </Link>
              </li>
            ))}
          </ul>
        </Card>
      ) : (
        <Empty title={t('noLayoutsTitle')} body={t('noLayoutsBody')} />
      )}

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
