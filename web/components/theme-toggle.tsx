'use client';

import {Moon, Sun} from 'lucide-react';
import {useTheme} from 'next-themes';
import {useTranslations} from 'next-intl';
import {Button} from './ui';

/**
 * Ikon dan nama aksesibilitasnya ditukar lewat CSS, bukan lewat state — tema
 * sebenarnya baru diketahui di browser, dan menunggu effect untuk tahu berarti
 * ikonnya berkedip sekali di setiap muat halaman.
 */
export function ThemeToggle() {
  const t = useTranslations('theme');
  const {resolvedTheme, setTheme} = useTheme();

  return (
    <Button
      tone="quiet"
      title={t('label')}
      onClick={() => setTheme(resolvedTheme === 'dark' ? 'light' : 'dark')}
      className="px-2"
    >
      <Moon className="size-4 dark:hidden" aria-hidden />
      <Sun className="hidden size-4 dark:block" aria-hidden />
      <span className="sr-only dark:hidden">{t('dark')}</span>
      <span className="sr-only hidden dark:inline">{t('light')}</span>
    </Button>
  );
}
