import {defineRouting} from 'next-intl/routing';

export const locales = ['id', 'en'] as const;
export type Locale = (typeof locales)[number];

export const routing = defineRouting({
  locales,
  defaultLocale: 'id',
  // Prefix selalu ada, termasuk untuk bahasa default: URL jadi satu bentuk saja,
  // dan tautan yang dibagikan tidak berubah arti tergantung siapa yang membuka.
  localePrefix: 'always'
});

export function isLocale(value: string | undefined): value is Locale {
  return value !== undefined && (locales as readonly string[]).includes(value);
}
