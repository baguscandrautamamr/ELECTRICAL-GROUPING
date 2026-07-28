import {getTranslations} from 'next-intl/server';
import {Empty} from '@/components/ui';
import {Link} from '@/i18n/navigation';

export default async function NotFound() {
  const t = await getTranslations('errors');
  const nav = await getTranslations('nav');

  return (
    <main className="mx-auto flex min-h-dvh w-full max-w-lg items-center px-5">
      <div className="w-full">
        <Empty
          title={t('notFound')}
          body={t('notFoundBody')}
          action={
            <Link
              href="/projects"
              className="rounded-[var(--radius-control)] border border-accent bg-accent px-3.5 py-2 text-[13px] font-semibold text-accent-ink transition-opacity duration-200 hover:opacity-90"
            >
              {nav('backToProjects')}
            </Link>
          }
        />
      </div>
    </main>
  );
}
