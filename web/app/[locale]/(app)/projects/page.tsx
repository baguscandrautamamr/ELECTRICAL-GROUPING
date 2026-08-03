import {getFormatter, getTranslations} from 'next-intl/server';
import {Card, CardHeader, Empty} from '@/components/ui';
import {SetupNeeded} from '@/components/setup-needed';
import type {Project} from '@/lib/contract';
import {classifyError, optional} from '@/lib/supabase/errors';
import {createClient} from '@/lib/supabase/server';
import {HiddenNote} from './hidden-note';
import {NewProjectForm} from './new-project-form';
import {ProjectRow} from './project-row';

export default async function ProjectsPage() {
  const t = await getTranslations('projects');
  const format = await getFormatter();
  const supabase = await createClient();

  // RLS yang membatasi ini ke project milik user; tidak ada filter owner di query.
  const {data, error} = await supabase
    .from('projects')
    .select('id, name, owner_id, created_at, updated_at')
    .order('updated_at', {ascending: false});

  // Query yang gagal pernah berakhir sebagai daftar kosong di sini, jadi database
  // yang belum dimigrasi tampil persis seperti akun baru. Dibedakan sekarang.
  const problem = classifyError(error);
  if (problem) return <SetupNeeded problem={problem} />;

  // `optional`: tabelnya datang dari migrasi yang lebih baru, jadi database yang belum
  // menerimanya harus kembali ke perilaku sebelum fitur ini ada — daftar penuh — bukan
  // memasang layar "database belum disiapkan" di atas database yang sebenarnya sehat.
  const hidden = optional(await supabase.from('project_hidden').select('project_id'));
  const hiddenIds = new Set((hidden ?? []).map((row) => row.project_id));

  const all = (data ?? []) as Project[];
  const projects = all.filter((project) => !hiddenIds.has(project.id));

  return (
    <div className="grid gap-6 lg:grid-cols-[1fr_20rem]">
      <div className="space-y-4">
        <div>
          <h1 className="text-[24px] font-semibold tracking-tight">{t('heading')}</h1>
          <p className="mt-1 text-[13px] text-muted">{t('subheading')}</p>
        </div>

        {projects.length === 0 ? (
          // Akun yang project-nya ada tapi semuanya disembunyikan bukan akun baru, dan
          // mengajaknya membuat project justru menjauhkannya dari yang sudah dia punya.
          hiddenIds.size > 0 ? (
            <Empty title={t('allHiddenTitle')} body={t('allHiddenBody')} />
          ) : (
            <Empty title={t('emptyTitle')} body={t('emptyBody')} />
          )
        ) : (
          <ul className="card divide-y divide-hairline overflow-hidden p-0">
            {projects.map((project) => (
              <ProjectRow
                key={project.id}
                id={project.id}
                name={project.name}
                updatedLabel={t('openedAt', {
                  when: format.relativeTime(new Date(project.updated_at))
                })}
              />
            ))}
          </ul>
        )}

        <HiddenNote count={hiddenIds.size} />
      </div>

      <Card className="h-fit">
        <CardHeader title={t('create')} />
        <NewProjectForm />
      </Card>
    </div>
  );
}
