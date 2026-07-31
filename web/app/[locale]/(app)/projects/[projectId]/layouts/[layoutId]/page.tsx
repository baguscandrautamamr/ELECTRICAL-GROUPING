import {ArrowLeft} from 'lucide-react';
import {getTranslations} from 'next-intl/server';
import type {Metadata} from 'next';
import {notFound} from 'next/navigation';
import {Badge, Empty} from '@/components/ui';
import {SetupNeeded} from '@/components/setup-needed';
import {Link} from '@/i18n/navigation';
import type {
  Circuit,
  Device,
  Layout,
  LayoutDevice,
  LayoutLightingDevice,
  LightingDevice,
  LineStyle,
  Panel,
  SymbolOverride
} from '@/lib/contract';
import {classifyError, firstProblem, optional} from '@/lib/supabase/errors';
import {allRows} from '@/lib/supabase/pages';
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

/**
 * Seluruh device satu jenis dalam project. Penyaringan per layout terjadi setelah ini,
 * jadi daftar ini memang harus utuh.
 */
function allDevicesOfKind(
  supabase: Awaited<ReturnType<typeof createClient>>,
  projectId: string,
  kind: string
) {
  return allRows<Device>((from, to) =>
    supabase
      .from('devices')
      .select('*')
      .eq('project_id', projectId)
      .eq('kind', kind)
      .order('revit_unique_id')
      .range(from, to)
  );
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

  // Isi denah ditentukan view Revit-nya, bukan pasangan (level, kind): satu lantai bisa
  // punya denah lighting dan denah emergency/exit sekaligus, dan keduanya berlantai serta
  // berjenis sama. `layout_devices` yang membedakannya.
  const [
    project,
    devices,
    members,
    anyMember,
    panels,
    circuits,
    overrides,
    switches,
    switchMembers,
    anySwitchMember,
    anySwitch,
    lineStyles,
    wiringInModel
  ] = await Promise.all([
    supabase.from('projects').select('id, name').eq('id', projectId).maybeSingle(),
    allDevicesOfKind(supabase, projectId, layout.kind),
    allRows<LayoutDevice>((from, to) =>
      supabase
        .from('layout_devices')
        .select('device_unique_id')
        .eq('project_id', projectId)
        .eq('layout_unique_id', layoutKey)
        .order('device_unique_id')
        .range(from, to)
    ),
    // Membedakan "view ini memang kosong" dari "model ditarik add-in versi lama yang
    // belum mengirim keanggotaan sama sekali". Tanpa ini denah kosong yang sah akan
    // diam-diam jatuh ke perilaku lama dan menampilkan seluruh isi lantai.
    supabase.from('layout_devices').select('layout_unique_id').eq('project_id', projectId).limit(1),
    supabase.from('panels').select('*').eq('project_id', projectId).order('name'),
    allRows<Circuit>((from, to) =>
      supabase
        .from('circuits')
        .select('*')
        .eq('project_id', projectId)
        .eq('kind', layout.kind)
        .order('created_at')
        .order('id')
        .range(from, to)
    ),
    supabase.from('symbol_overrides').select('*').eq('project_id', projectId),
    // Saklar di lantai ini. Jumlahnya per ruangan memecah lampu jadi sebanyak itu
    // grouping — lihat `lib/wiring.ts`. Tabelnya dari migrasi yang lebih baru, jadi
    // dibaca lewat `optional()`.
    //
    // Diambil per lantai, lalu disaring per layout di bawah. Lantai adalah jaring
    // cadangan, bukan jawabannya: dua denah lighting di satu lantai punya `level_key`
    // yang sama, jadi tanpa penyaringan itu keduanya menerima seluruh saklar lantai.
    allRows<LightingDevice>((from, to) =>
      supabase
        .from('lighting_devices')
        .select('*')
        .eq('project_id', projectId)
        .eq('level_key', layout.level_key)
        .order('revit_unique_id')
        .range(from, to)
    ),
    allRows<LayoutLightingDevice>((from, to) =>
      supabase
        .from('layout_lighting_devices')
        .select('lighting_device_unique_id')
        .eq('project_id', projectId)
        .eq('layout_unique_id', layoutKey)
        .order('lighting_device_unique_id')
        .range(from, to)
    ),
    // Sama seperti `anyMember`, untuk saklar: "denah ini memang tidak punya saklar"
    // berbeda dari "add-in belum pernah mengirim keanggotaan saklar".
    supabase
      .from('layout_lighting_devices')
      .select('layout_unique_id')
      .eq('project_id', projectId)
      .limit(1),
    // Dan "project ini belum punya data saklar sama sekali" berbeda lagi: itu yang
    // pantas dapat peringatan di panel wiring, bukan denah yang saklarnya memang nol.
    supabase.from('lighting_devices').select('revit_unique_id').eq('project_id', projectId).limit(1),
    supabase.from('line_styles').select('*').eq('project_id', projectId).order('sort_order'),
    // Berapa ruas garis yang sedang ada di Revit untuk denah ini. Hanya jumlahnya yang
    // dipakai, jadi `head` — barisnya bisa ribuan di denah besar, dan tidak satu pun
    // dibutuhkan di sini.
    supabase
      .from('wiring_curves')
      .select('revit_unique_id', {count: 'exact', head: true})
      .eq('project_id', projectId)
      .eq('layout_unique_id', layoutKey)
  ]);

  // `layout_devices` datang dari migrasi yang lebih baru. Belum diterapkan berarti
  // halaman kembali ke penyaringan per level — bukan layar "database belum disiapkan"
  // di atas database yang sehat.
  const problem = firstProblem(
    project.error,
    devices.error,
    panels.error,
    circuits.error,
    overrides.error
  );
  if (problem) return <SetupNeeded problem={problem} />;

  if (!project.data) notFound();

  const ofKind = (devices.data ?? []) as Device[];
  const memberIds = new Set((optional(members) ?? []).map((row) => row.device_unique_id));

  /**
   * Keanggotaan menang atas `level_key`. Level sebuah device tidak selalu bisa
   * ditentukan Revit — stop kontak yang di-host di dinding sering tidak punya
   * `LevelId` sendiri — dan menyaring dengannya membuat device seperti itu hilang
   * dari denah meski jelas terlihat di view-nya.
   */
  const deviceRows = (optional(anyMember) ?? []).length > 0
    ? ofKind.filter((device) => memberIds.has(device.revit_unique_id))
    : ofKind.filter((device) => device.level_key === layout.level_key);
  const t = await getTranslations('plan');
  const nav = await getTranslations('nav');

  // Circuit tidak menyimpan layout. Yang relevan di sini adalah circuit yang
  // menyentuh setidaknya satu device di layout ini.
  const onThisLayout = new Set(deviceRows.map((device) => device.revit_unique_id));
  const circuitRows = ((circuits.data ?? []) as Circuit[]).filter((circuit) =>
    circuit.device_unique_ids.some((id) => onThisLayout.has(id))
  );

  /**
   * Saklar denah ini. Keanggotaan per view menang atas `level_key`, sama seperti pada
   * device — dan di sini justru lebih penting: saklar yang jatuh ke denah yang salah tidak
   * menampilkan apa pun yang keliru, ia hanya memecah lampu jadi jumlah grouping yang
   * salah, tanpa peringatan.
   *
   * Selama keanggotaannya belum pernah dikirim, halaman kembali ke penyaringan per lantai —
   * bukan mengosongkan saklar, yang akan membuat fitur ini seolah rusak di model yang
   * ditarik add-in versi lama.
   */
  const switchMemberIds = new Set(
    (optional(switchMembers) ?? []).map((row) => row.lighting_device_unique_id)
  );
  const onFloor = (switches.data ?? []) as LightingDevice[];
  const lightingDevices = (optional(anySwitchMember) ?? []).length > 0
    ? onFloor.filter((device) => switchMemberIds.has(device.revit_unique_id))
    : onFloor;

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
          lightingDevices={lightingDevices}
          lineStyles={(optional(lineStyles) ?? []) as LineStyle[]}
          // Dropdown kosong punya dua sebab yang jalan keluarnya berbeda: tabelnya belum
          // ada (jalankan migrasi) atau model belum ditarik (tarik dari add-in). Yang
          // kedua adalah petunjuk yang salah untuk yang pertama.
          lineStylesUnavailable={classifyError(lineStyles.error) === 'schema'}
          switchDataMissing={(optional(anySwitch) ?? []).length === 0}
          // Nol saat tabelnya belum ada, dan itu jawaban yang benar: belum ada garis yang
          // tercatat, jadi kiriman berikutnya memang belum bisa menggantikan apa pun.
          wiringInModel={wiringInModel.error ? 0 : (wiringInModel.count ?? 0)}
        />
      )}
    </div>
  );
}
