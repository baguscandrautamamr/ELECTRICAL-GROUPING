import {ArrowLeft} from 'lucide-react';
import {getTranslations} from 'next-intl/server';
import type {Metadata} from 'next';
import {notFound} from 'next/navigation';
import {Empty} from '@/components/ui';
import {Link} from '@/i18n/navigation';
import {
  isKind,
  type Circuit,
  type Device,
  type Level,
  type Panel,
  type SymbolOverride
} from '@/lib/contract';
import {createClient} from '@/lib/supabase/server';
import {PlanView} from './plan-view';

type Params = {params: Promise<{projectId: string; levelId: string; kind: string}>};

export async function generateMetadata({params}: Params): Promise<Metadata> {
  const {kind} = await params;
  const t = await getTranslations('project');
  return {title: isKind(kind) ? t(kind) : 'Plan'};
}

export default async function PlanPage({params}: Params) {
  const {projectId, levelId, kind} = await params;
  if (!isKind(kind)) notFound();

  const levelKey = decodeURIComponent(levelId);
  const supabase = await createClient();

  const [project, level, devices, panels, circuits, overrides] = await Promise.all([
    supabase.from('projects').select('id, name').eq('id', projectId).maybeSingle(),
    supabase.from('levels').select('*').eq('project_id', projectId).eq('level_key', levelKey).maybeSingle(),
    supabase
      .from('devices')
      .select('*')
      .eq('project_id', projectId)
      .eq('level_key', levelKey)
      .eq('kind', kind)
      .order('revit_unique_id'),
    supabase.from('panels').select('*').eq('project_id', projectId).order('name'),
    supabase.from('circuits').select('*').eq('project_id', projectId).eq('kind', kind).order('created_at'),
    supabase.from('symbol_overrides').select('*').eq('project_id', projectId)
  ]);

  if (!project.data) notFound();

  const deviceRows = (devices.data ?? []) as Device[];
  const t = await getTranslations('plan');
  const nav = await getTranslations('nav');
  const project_ = await getTranslations('project');

  const levelName = (level.data as Level | null)?.name ?? levelKey;

  // Circuit tidak menyimpan level. Yang relevan untuk halaman ini adalah circuit
  // yang menyentuh setidaknya satu device di lantai ini.
  const onThisLevel = new Set(deviceRows.map((device) => device.revit_unique_id));
  const circuitRows = ((circuits.data ?? []) as Circuit[]).filter((circuit) =>
    circuit.device_unique_ids.some((id) => onThisLevel.has(id))
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
        <h1 className="text-[20px] font-semibold tracking-tight">
          {t('heading', {level: levelName, kind: project_(kind)})}
        </h1>
      </div>

      {deviceRows.length === 0 ? (
        <Empty title={t('emptyTitle')} body={t('emptyBody')} />
      ) : (
        <PlanView
          projectId={projectId}
          kind={kind}
          devices={deviceRows}
          panels={(panels.data ?? []) as Panel[]}
          circuits={circuitRows}
          symbolOverrides={symbolOverrides}
        />
      )}
    </div>
  );
}
