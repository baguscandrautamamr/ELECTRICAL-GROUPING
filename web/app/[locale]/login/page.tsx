import {getTranslations, setRequestLocale} from 'next-intl/server';
import type {Metadata} from 'next';
import Image from 'next/image';
import {LocaleToggle} from '@/components/locale-toggle';
import {ThemeToggle} from '@/components/theme-toggle';
import {Card} from '@/components/ui';
import {LoginForm} from './login-form';

export async function generateMetadata({
  params
}: {
  params: Promise<{locale: string}>;
}): Promise<Metadata> {
  const {locale} = await params;
  const t = await getTranslations({locale, namespace: 'auth'});
  return {title: t('heading')};
}

export default async function LoginPage({
  params,
  searchParams
}: {
  params: Promise<{locale: string}>;
  searchParams: Promise<{error?: string}>;
}) {
  const {locale} = await params;
  setRequestLocale(locale);

  const {error} = await searchParams;
  const t = await getTranslations('auth');
  const app = await getTranslations('app');

  return (
    <main className="mx-auto flex min-h-dvh w-full max-w-md flex-col justify-center gap-6 px-5 py-12">
      <div className="flex items-start justify-between">
        <div className="flex items-center gap-3">
          <Image src="/icon-192.png" alt="" width={40} height={40} className="rounded-[10px]" priority />
          <div>
            <p className="text-[17px] font-semibold tracking-tight">{app('name')}</p>
            <p className="text-[13px] leading-snug text-muted">{app('tagline')}</p>
          </div>
        </div>
        <div className="flex shrink-0 items-center gap-1">
          <LocaleToggle />
          <ThemeToggle />
        </div>
      </div>

      <Card>
        <h1 className="text-[20px] font-semibold tracking-tight">{t('heading')}</h1>
        <p className="mt-1.5 text-[13px] leading-relaxed text-muted">{t('subheading')}</p>
        <div className="mt-5">
          <LoginForm callbackFailed={error === 'callback'} />
        </div>
      </Card>
    </main>
  );
}
