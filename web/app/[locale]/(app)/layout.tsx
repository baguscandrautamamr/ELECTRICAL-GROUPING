import {getTranslations} from 'next-intl/server';
import Image from 'next/image';
import type {ReactNode} from 'react';
import {LocaleToggle} from '@/components/locale-toggle';
import {ThemeToggle} from '@/components/theme-toggle';
import {Button} from '@/components/ui';
import {Link} from '@/i18n/navigation';
import {signOut} from './actions';

export default async function AppLayout({children}: {children: ReactNode}) {
  const nav = await getTranslations('nav');
  const app = await getTranslations('app');

  return (
    <div className="min-h-dvh">
      <header className="chrome sticky top-0 z-20">
        <div className="mx-auto flex h-14 w-full max-w-6xl items-center gap-3 px-5">
          <Link href="/projects" className="flex items-center gap-2.5">
            <Image src="/icon-192.png" alt="" width={26} height={26} className="rounded-[7px]" priority />
            <span className="text-[15px] font-semibold tracking-tight">{app('name')}</span>
          </Link>

          <nav className="ml-2 hidden sm:block">
            <Link
              href="/projects"
              className="rounded-md px-2 py-1 text-[13px] text-muted transition-colors hover:text-ink"
            >
              {nav('projects')}
            </Link>
          </nav>

          <div className="ml-auto flex items-center gap-1">
            <LocaleToggle />
            <ThemeToggle />
            <form action={signOut}>
              <Button type="submit" tone="quiet">
                {nav('signOut')}
              </Button>
            </form>
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-6xl px-5 py-8">{children}</main>
    </div>
  );
}
