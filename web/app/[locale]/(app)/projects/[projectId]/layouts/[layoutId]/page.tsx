import {ArrowLeft} from 'lucide-react';
import {getTranslations} from 'next-intl/server';
import type {Metadata} from 'next';
import {notFound} from 'next/navigation';
import {Badge, Empty} from '@/components/ui';
import {SetupNeeded} from '@/components/setup-needed';
import {Link} from '@/i18n/navigation';
import type {Circuit, Device, Layout, Panel, SymbolOverride} from '@/lib/contract';
import {firstProblem} from '@/lib/supabase/errors';
import {createClient} from '@/lib/supabase/server';
import {PlanView} from './plan-view';

type Params = {params: Promise<{projectId: string; layoutId: string}>};

/** Layout dikunci `revit_unique_id` view, jadi tautan tetap sah antar tarikan model. */
async function loadLayout(projectId: string, layoutId: string) {
  const supabase = await createClient();
  return supabase
    .from('layouts')
    .select('*')
    .eq('project_id', projectId)
    .eq('revit_unique_id', layoutId)
    .maybeSingle();
}

export async function generateMetadata({params}: Params): Promise<Metadata> {
  const {projectId, layoutId} = await params;
  const {data} = await loadLayout(projectId, decodeURIComponent(layoutId));
  return {title: (data as Layout | null)?.name ?? 'Denah'};
}

export default async function PlanPage({params}: Params) {
  const {projectId, layoutId} = await params;
  const layoutKey = decodeURIComponent(layoutId);

  const supabase = await createClient();
  const layoutResult = await loadLayout(projectId, layoutKey);
  const layout = layoutResult.data as Layout | null;

  const layoutProblem = firstProblem(layoutResult.error);
  if (layoutProblem) return <SetupNeeded problem={layoutProblem} />;
  if (!layout) notFound();

  // Layout membawa lantai dan jenisnya sendiri; device dipilih dari keduanya.
  const [project, devices, panels, circuits, overrides] = await Promise.all([
    supabase.from('projects').select('id, name').eq('id', projectId).maybeSingle(),
    supabase
      .from('devices')
      .select('*')
      .eq('project_id', projectId)
      .eq('level_key', layout.level_key)
      .eq('kind', layout.kind)
      .order('revit_unique_id'),
    supabase.from('panels').select('*').eq('project_id', projectId).order('name'),
    supabase
      .from('circuits')
      .select('*')
      .eq('project_id', projectId)
      .eq('kind', layout.kind)
      .order('created_at'),
    supabase.from('symbol_overrides').select('*').eq('project_id', projectId)
  ]);

  const problem = firstProblem(
    project.error,
    devices.error,
    panels.error,
    circuits.error,
    overrides.error
  );
  if (problem) return <SetupNeeded problem={problem} />;

  if (!project.data) notFound();

  const deviceRows = (devices.data ?? []) as Device[];
  const t = await getTranslations('plan');
  const nav = await getTranslations('nav');

  // Circuit tidak menyimpan layout. Yang relevan di sini adalah circuit yang
  // menyentuh setidaknya satu device di layout ini.
  const onThisLayout = new Set(deviceRows.map((device) => device.revit_unique_id));
  const circuitRows = ((circuits.data ?? []) as Circuit[]).filter((circuit) =>
    circuit.device_unique_ids.some((id) => onThisLayout.has(id))
  );

  const symbolOverrides = Object.fromEntries(
    ((overrides.data ?? []) as SymbolOverride[]).map((row) => [row.family_key, row.symbol])
  );

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center gap-3">
        <Link
          href={`/projects/${projectId}`}
          className="inline-flex items-center gap-1.5 text-[13px] text-muted transition-colors hover:text-ink"
        >
          <ArrowLeft className="size-4" aria-hidden />
          {nav('backToProject')}
        </Link>
        <h1 className="text-[20px] font-semibold tracking-tight">{layout.name}</h1>
        {layout.scale ? <Badge>{t('scale', {scale: layout.scale})}</Badge> : null}
      </div>

      {deviceRows.length === 0 ? (
        <Empty title={t('emptyTitle')} body={t('emptyBody')} />
      ) : (
        <PlanView
          projectId={projectId}
          kind={layout.kind}
          layout={layout}
          devices={deviceRows}
          panels={(panels.data ?? []) as Panel[]}
          circuits={circuitRows}
          symbolOverrides={symbolOverrides}
        />
      )}
    </div>
  );
}
