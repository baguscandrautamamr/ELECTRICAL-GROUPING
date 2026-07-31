import {ChevronRight, Lightbulb, PlugZap} from 'lucide-react';
import {getTranslations} from 'next-intl/server';
import type {Metadata} from 'next';
import {notFound} from 'next/navigation';
import {AutoRefresh} from '@/components/auto-refresh';
import {PanelList} from '@/components/panels/panel-list';
import {UnconnectedDevices} from '@/components/panels/unconnected-devices';
import {Badge, Card, CardHeader, Empty, Notice} from '@/components/ui';
import {SetupNeeded} from '@/components/setup-needed';
import {Link} from '@/i18n/navigation';
import {type Device, type Layout, type Panel, type Project} from '@/lib/contract';
import {
  groupPanelContents,
  groupUnconnected,
  type PanelContentRow,
  type UnconnectedRow
} from '@/lib/summaries';
import {firstProblem, optional} from '@/lib/supabase/errors';
import {createClient} from '@/lib/supabase/server';

type Params = {params: Promise<{projectId: string}>};

async function load(projectId: string) {
  const supabase = await createClient();

  /**
   * Jumlah dihitung per view, bukan per (level, kind) — dua denah lighting di lantai
   * yang sama punya isi berbeda. Satu panggilan untuk seluruh layout: menghitungnya di
   * web berarti satu request per denah, dan halaman ini menyegarkan diri berkala.
   */
  const [project, layouts, panels, devices, counts, orphans, contents, unconnected, unconnectedTotal] =
    await Promise.all([
      supabase.from('projects').select('id, name, owner_id, created_at, updated_at').eq('id', projectId).maybeSingle(),
      supabase.from('layouts').select('*').eq('project_id', projectId).order('sort_order'),
      supabase.from('panels').select('*').eq('project_id', projectId).order('name'),
      supabase.from('devices').select('kind, level_key').eq('project_id', projectId),
      supabase.rpc('layout_device_counts', {p_project: projectId}),
      supabase.rpc('devices_without_layout', {p_project: projectId}),
      supabase.rpc('panel_contents', {p_project: projectId}),
      supabase.rpc('unconnected_devices', {p_project: projectId}),
      supabase.rpc('unconnected_total', {p_project: projectId})
    ]);

  // Panggilan rpc ini datang dari migrasi yang lebih baru. Kalau belum diterapkan,
  // halaman kembali ke perilaku sebelum fiturnya ada — bukan memasang layar "database
  // belum disiapkan" di atas database yang sehat.
  const countRows = (optional(counts) ?? []) as {layout_unique_id: string; devices: number}[];

  return {
    // Tanpa ini, database yang belum dimigrasi berakhir sebagai 404: project-nya
    // null bukan karena tidak ada, tapi karena tabelnya belum ada.
    problem: firstProblem(project.error, layouts.error, panels.error, devices.error),
    project: project.data as Project | null,
    layouts: (layouts.data ?? []) as Layout[],
    panels: (panels.data ?? []) as Panel[],
    devices: (devices.data ?? []) as Pick<Device, 'kind' | 'level_key'>[],
    // Kosong berarti model terakhir ditarik add-in versi lama yang belum mengirim
    // keanggotaan; jatuh kembali ke perhitungan per (level, kind).
    counted: countRows.length > 0
      ? new Map(countRows.map((row) => [row.layout_unique_id, Number(row.devices)]))
      : null,
    // Device yang tidak tampak di denah mana pun. Disebut apa adanya supaya
    // "jumlahnya kurang" punya petunjuk, bukan sekadar terasa janggal.
    orphans: countRows.length > 0 ? Number(optional(orphans) ?? 0) : 0,
    contents: groupPanelContents((optional(contents) ?? []) as PanelContentRow[]),
    unconnected: groupUnconnected((optional(unconnected) ?? []) as UnconnectedRow[]),
    unconnectedTotal: Number(optional(unconnectedTotal) ?? 0)
  };
}

export async function generateMetadata({params}: Params): Promise<Metadata> {
  const {projectId} = await params;
  const {project} = await load(projectId);
  return {title: project?.name ?? 'Project'};
}

export default async function ProjectPage({params}: Params) {
  const {projectId} = await params;
  const {problem, project, layouts, panels, devices, counted, orphans, contents, unconnected, unconnectedTotal} =
    await load(projectId);

  if (problem) return <SetupNeeded problem={problem} />;

  // RLS mengembalikan nol baris untuk project yang bukan milik user, jadi
  // "tidak ada" dan "bukan milik saya" sengaja tidak dibedakan di sini.
  if (!project) notFound();

  const t = await getTranslations('project');

  // Jumlahnya harus sama dengan yang terlihat di view Revit. Pasangan (level, kind)
  // hanya dipakai kalau model ditarik add-in versi lama yang belum mengirim
  // keanggotaan layout sama sekali.
  function count(layout: Layout) {
    return (
      counted?.get(layout.revit_unique_id) ??
      devices.filter(
        (device) => device.level_key === layout.level_key && device.kind === layout.kind
      ).length
    );
  }

  return (
    <div className="space-y-6">
      {/* Jumlah titik per denah berubah begitu add-in mengirim ulang model. Lambat
          saja: yang membuatnya terasa seketika adalah penyegaran saat tab kembali. */}
      <AutoRefresh seconds={60} />

      <div>
        <h1 className="text-[24px] font-semibold tracking-tight">{project.name}</h1>
        <p className="mt-1 text-[13px] text-muted">{t('layoutsSubheading')}</p>
      </div>

      {devices.length === 0 ? <Empty title={t('emptyTitle')} body={t('emptyBody')} /> : null}

      {orphans > 0 ? <Notice tone="warn">{t('orphanDevices', {count: orphans})}</Notice> : null}

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

      {panels.length > 0 ? <PanelList panels={panels} contents={contents} /> : null}

      {devices.length > 0 ? (
        <UnconnectedDevices
          projectId={project.id}
          groups={unconnected}
          layouts={layouts}
          total={unconnectedTotal}
        />
      ) : null}
    </div>
  );
}
