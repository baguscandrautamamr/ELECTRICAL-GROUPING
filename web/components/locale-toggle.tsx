'use client';

import {useLocale, useTranslations} from 'next-intl';
import {useTransition} from 'react';
import {usePathname, useRouter} from '@/i18n/navigation';
import {locales} from '@/i18n/routing';
import {Button} from './ui';

/**
 * Bahasa ada di URL, jadi menggantinya berarti pindah halaman ke path yang sama
 * dengan prefix lain — tautan yang dibagikan tetap membawa bahasanya.
 */
export function LocaleToggle() {
  const t = useTranslations('locale');
  const active = useLocale();
  const pathname = usePathname();
  const router = useRouter();
  const [pending, startTransition] = useTransition();

  const next = locales.find((candidate) => candidate !== active) ?? active;

  return (
    <Button
      tone="quiet"
      aria-label={`${t('label')}: ${t(next)}`}
      title={t(next)}
      disabled={pending}
      onClick={() => startTransition(() => router.replace(pathname, {locale: next}))}
      className="px-2 font-mono text-[12px] tracking-wider uppercase"
    >
      {active}
    </Button>
  );
}
