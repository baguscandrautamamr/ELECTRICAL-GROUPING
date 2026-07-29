import {ChevronRight} from 'lucide-react';
import {getFormatter, getTranslations} from 'next-intl/server';
import {Badge, Card, CardHeader, Empty} from '@/components/ui';
import {SetupNeeded} from '@/components/setup-needed';
import {Link} from '@/i18n/navigation';
import type {Project} from '@/lib/contract';
import {classifyError} from '@/lib/supabase/errors';
import {createClient} from '@/lib/supabase/server';
import {NewProjectForm} from './new-project-form';

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

  const projects = (data ?? []) as Project[];

  return (
    <div className="grid gap-6 lg:grid-cols-[1fr_20rem]">
      <div className="space-y-4">
        <div>
          <h1 className="text-[24px] font-semibold tracking-tight">{t('heading')}</h1>
          <p className="mt-1 text-[13px] text-muted">{t('subheading')}</p>
        </div>

        {projects.length === 0 ? (
          <Empty title={t('emptyTitle')} body={t('emptyBody')} />
        ) : (
          <ul className="card divide-y divide-hairline overflow-hidden p-0">
            {projects.map((project) => (
              <li key={project.id}>
                <Link
                  href={`/projects/${project.id}`}
                  className="flex items-center gap-4 px-5 py-4 transition-colors duration-200 hover:bg-sunken"
                >
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-[15px] font-semibold">{project.name}</p>
                    <p className="mt-0.5 text-[12px] text-muted">
                      {t('openedAt', {
                        when: format.relativeTime(new Date(project.updated_at))
                      })}
                    </p>
                  </div>
                  <Badge tone="accent">{t('open')}</Badge>
                  <ChevronRight className="size-4 shrink-0 text-muted" aria-hidden />
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>

      <Card className="h-fit">
        <CardHeader title={t('create')} />
        <NewProjectForm />
      </Card>
    </div>
  );
}
